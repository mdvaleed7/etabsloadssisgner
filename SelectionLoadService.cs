using System;
using System.Collections.Generic;
using System.Text;
using ETABSv1;

namespace AdvatechEtabsPlugin
{
    /// <summary>
    /// Assigns loads to the OBJECTS THE USER HAS SELECTED in ETABS, backing
    /// Features 4 (intelligent live load), 5 (dead-load assistant) and 6 (wall
    /// load generator).
    ///
    /// Design decisions
    /// ----------------
    ///  • Selection-driven: the engineer selects slabs/beams in ETABS, then the
    ///    plugin reads the current selection (SelectObj.GetSelected) and applies
    ///    the chosen pressure / line load — exactly the workflow the spec asks
    ///    for ("the user should first select slab objects").
    ///  • Idempotent: area uniform and frame distributed loads are written with
    ///    Replace = true keyed by pattern, so re-running does not accumulate.
    ///  • Gravity direction (Dir = 10) so loads act downward irrespective of the
    ///    active coordinate system (units-safe, sign-safe).
    ///  • Wall loads support automatic beam detection beneath a wall line and
    ///    inclined beams (ETABS distributes the kN/m per unit member length).
    /// </summary>
    public class SelectionLoadService
    {
        private readonly cSapModel _sapModel;

        private const int DIR_GRAVITY = 10;
        private const int MYTYPE_FORCE = 1;

        // ETABS object-type codes returned by SelectObj.GetSelected:
        //   2 = Frame object, 5 = Area object (point=1, cable=3, tendon=4, link=7…)
        private const int OBJTYPE_FRAME = 2;
        private const int OBJTYPE_AREA  = 5;

        public SelectionLoadService(cSapModel sapModel)
        {
            _sapModel = sapModel;
        }

        // ── Read the current ETABS selection split by object type ────────────
        public bool GetSelection(out List<string> areas, out List<string> frames, out string log)
        {
            areas = new List<string>();
            frames = new List<string>();
            var sb = new StringBuilder();

            int n = 0;
            int[] types = null;
            string[] names = null;
            int ret = _sapModel.SelectObj.GetSelected(ref n, ref types, ref names);
            if (ret != 0 || n == 0 || names == null || types == null)
            {
                sb.AppendLine("  No objects are selected in ETABS. Select slabs/beams first.");
                log = sb.ToString();
                return false;
            }

            for (int i = 0; i < n; i++)
            {
                if (types[i] == OBJTYPE_AREA) areas.Add(names[i]);
                else if (types[i] == OBJTYPE_FRAME) frames.Add(names[i]);
            }
            sb.AppendLine($"  Selection: {areas.Count} area object(s), {frames.Count} frame object(s).");
            log = sb.ToString();
            return areas.Count > 0 || frames.Count > 0;
        }

        // ── FEATURE 4 / 5: uniform pressure on the selected slabs ────────────
        /// <summary>
        /// Applies a uniform pressure (kN/m², in the model's present force/length
        /// units) under a named load pattern to the selected area objects.
        /// </summary>
        public string AssignAreaPressure(string loadPattern, double pressure_kNm2, string description)
        {
            var sb = new StringBuilder();
            EtabsModelGuard.EnsureUnlocked(_sapModel, sb);

            if (!GetSelection(out var areas, out _, out string selLog))
            { sb.Append(selLog); return sb.ToString(); }
            sb.Append(selLog);

            if (areas.Count == 0)
            {
                sb.AppendLine("  No AREA objects selected — nothing assigned.");
                return sb.ToString();
            }

            int ok = 0, fail = 0;
            foreach (var a in areas)
            {
                int r = _sapModel.AreaObj.SetLoadUniform(
                    a, loadPattern, pressure_kNm2, DIR_GRAVITY, true, "Global", eItemType.Objects);
                if (r == 0) ok++; else fail++;
            }

            sb.AppendLine($"  {description}: {pressure_kNm2:F2} kN/m² under '{loadPattern}' " +
                          $"→ {ok} slab(s)" + (fail > 0 ? $", {fail} failed" : "") + ".");
            return sb.ToString();
        }

        // ── FEATURE 6: wall line load on the selected / detected beams ───────
        /// <summary>
        /// Applies the computed wall line load (kN/m) under a named pattern to the
        /// selected frame objects (the beams under the wall).  Columns within the
        /// selection are skipped automatically.
        /// </summary>
        public string AssignWallLineLoad(string loadPattern, WallLoadResult wall, string description)
        {
            var sb = new StringBuilder();
            EtabsModelGuard.EnsureUnlocked(_sapModel, sb);

            if (!GetSelection(out _, out var frames, out string selLog))
            { sb.Append(selLog); return sb.ToString(); }
            sb.Append(selLog);

            if (frames.Count == 0)
            {
                sb.AppendLine("  No FRAME objects selected — select the beams under the wall, " +
                              "or use auto-detect.");
                return sb.ToString();
            }

            int ok = 0, fail = 0, skippedCols = 0;
            foreach (var f in frames)
            {
                if (IsColumn(f)) { skippedCols++; continue; }
                int r = ApplyBeamLineLoad(f, loadPattern, wall.TotalLineLoad_kNm);
                if (r == 0) ok++; else fail++;
            }

            sb.AppendLine($"  {description}: {wall.TotalLineLoad_kNm:F2} kN/m under '{loadPattern}' " +
                          $"→ {ok} beam(s)" +
                          (skippedCols > 0 ? $", {skippedCols} column(s) skipped" : "") +
                          (fail > 0 ? $", {fail} failed" : "") + ".");
            sb.Append(wall.Breakdown);
            return sb.ToString();
        }

        // ── Auto-detect beams directly beneath a polyline of wall vertices ───
        /// <summary>
        /// Finds beams whose mid-elevation matches a target storey elevation and
        /// whose footprint lies under the supplied wall line, then applies the
        /// wall line load.  Used when the engineer prefers automatic detection to
        /// manual selection.  Handles inclined beams (uses 3-D length implicitly).
        /// </summary>
        public string AssignWallToDetectedBeams(string loadPattern, WallLoadResult wall,
                                                double wallStartX, double wallStartY,
                                                double wallEndX, double wallEndY,
                                                double storeyElevation, double tolerance_m,
                                                string description)
        {
            var sb = new StringBuilder();
            EtabsModelGuard.EnsureUnlocked(_sapModel, sb);

            double perM = UnitsPerMetre();
            double tol = tolerance_m * perM;

            int nFrames = 0; string[] frameNames = null;
            if (_sapModel.FrameObj.GetNameList(ref nFrames, ref frameNames) != 0 || frameNames == null)
            {
                sb.AppendLine("  No frame objects in model.");
                return sb.ToString();
            }

            int ok = 0, fail = 0, matched = 0;
            foreach (var f in frameNames)
            {
                string p1 = "", p2 = "";
                if (_sapModel.FrameObj.GetPoints(f, ref p1, ref p2) != 0) continue;
                double x1=0,y1=0,z1=0,x2=0,y2=0,z2=0;
                _sapModel.PointObj.GetCoordCartesian(p1, ref x1, ref y1, ref z1);
                _sapModel.PointObj.GetCoordCartesian(p2, ref x2, ref y2, ref z2);

                double midZ = (z1 + z2) / 2.0;
                if (Math.Abs(midZ - storeyElevation) > tol) continue;   // wrong storey
                if (Math.Abs(z2 - z1) > Math.Sqrt((x2 - x1)*(x2 - x1) + (y2 - y1)*(y2 - y1))) continue; // column

                // Beam midpoint must lie close to the wall segment in plan.
                double mx = (x1 + x2) / 2.0, my = (y1 + y2) / 2.0;
                double dist = PointToSegmentDistance(mx, my, wallStartX, wallStartY, wallEndX, wallEndY);
                if (dist > tol) continue;

                matched++;
                int r = ApplyBeamLineLoad(f, loadPattern, wall.TotalLineLoad_kNm);
                if (r == 0) ok++; else fail++;
            }

            sb.AppendLine($"  {description} (auto-detect): matched {matched} beam(s) on the wall line; " +
                          $"loaded {ok}" + (fail > 0 ? $", {fail} failed" : "") + ".");
            sb.Append(wall.Breakdown);
            return sb.ToString();
        }

        // ── helpers ──────────────────────────────────────────────────────────
        private int ApplyBeamLineLoad(string frame, string pattern, double load_kNm) =>
            _sapModel.FrameObj.SetLoadDistributed(
                frame, pattern, MYTYPE_FORCE, DIR_GRAVITY,
                0.0, 1.0, load_kNm, load_kNm, "Global",
                RelDist: true, Replace: true, ItemType: eItemType.Objects);

        private bool IsColumn(string frame)
        {
            string p1 = "", p2 = "";
            if (_sapModel.FrameObj.GetPoints(frame, ref p1, ref p2) != 0) return false;
            double x1=0,y1=0,z1=0,x2=0,y2=0,z2=0;
            _sapModel.PointObj.GetCoordCartesian(p1, ref x1, ref y1, ref z1);
            _sapModel.PointObj.GetCoordCartesian(p2, ref x2, ref y2, ref z2);
            double dz = Math.Abs(z2 - z1);
            double dxy = Math.Sqrt((x2 - x1)*(x2 - x1) + (y2 - y1)*(y2 - y1));
            return dz > dxy;
        }

        private double UnitsPerMetre()
        {
            string u = _sapModel.GetPresentUnits().ToString();
            if (u.Contains("_mm_")) return 1000.0;
            if (u.Contains("_cm_")) return 100.0;
            if (u.Contains("_in_")) return 39.3701;
            if (u.Contains("_ft_")) return 3.28084;
            return 1.0;
        }

        private static double PointToSegmentDistance(double px, double py,
                                                     double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-12) return Math.Sqrt((px - ax)*(px - ax) + (py - ay)*(py - ay));
            double t = ((px - ax) * dx + (py - ay) * dy) / len2;
            t = Math.Max(0, Math.Min(1, t));
            double cx = ax + t * dx, cy = ay + t * dy;
            return Math.Sqrt((px - cx)*(px - cx) + (py - cy)*(py - cy));
        }
    }
}
