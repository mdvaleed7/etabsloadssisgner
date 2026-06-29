using System;
using System.Collections.Generic;
using ETABSv1;

namespace AdvatechEtabsPlugin
{
    /// <summary>
    /// Assigns gravity loads to area (slab) and frame (beam) objects.
    ///
    /// Strategy
    /// --------
    /// 1. The top-most story receives ROOF loads (RoofLL, Parapet); all lower
    ///    stories receive FLOOR loads (LL, SDL, Cladding).
    /// 2. Area objects (slabs):
    ///    • SDL applied uniformly to every slab.
    ///    • LL  applied uniformly (floor LL on floors, roof LL on the roof slab).
    ///    • Self-weight is carried by the DEAD pattern (SW multiplier = 1.0).
    /// 3. Frame objects (beams):
    ///    • Cladding line load on EXTERIOR floor beams (perimeter detection).
    ///    • Parapet line load on roof-level exterior beams.
    ///    • Columns are skipped (members predominantly vertical).
    ///
    /// Direction convention: MyDir = 10 (Gravity) so the load acts downward in the
    /// global -Z sense regardless of the active coordinate system. Using a POSITIVE
    /// magnitude with the "Gravity" direction is the unambiguous, units-safe way to
    /// apply downward loads in ETABS (avoids the "negative value + Global Z" trap,
    /// which flips sign if someone applies it in a rotated local system).
    ///
    /// Idempotency: gravity loads use Replace=true keyed by (object, pattern), so
    /// re-running the step does NOT accumulate duplicate loads.
    /// </summary>
    public class LoadAssigner
    {
        private readonly cSapModel _sapModel;

        // ETABS load direction codes: 6 = Global Z, 10 = Gravity (downward).
        private const int DIR_GRAVITY = 10;
        private const int MYTYPE_FORCE = 1;

        // Tolerance (metres) for perimeter beam detection and roof-elevation match.
        private const double PERIMETER_TOL_M = 0.30;
        private const double ELEV_TOL_M = 0.10;

        public LoadAssigner(cSapModel sapModel)
        {
            _sapModel = sapModel;
        }

        public string AssignAllLoads(BuildingConfig cfg)
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine("=== Assigning Loads ===");

            EtabsModelGuard.EnsureUnlocked(_sapModel, log);

            // Length scale: model-units-per-metre, used to convert tolerances.
            double unitsPerMetre = GetLengthUnitsPerMetre();
            double perimTol = PERIMETER_TOL_M * unitsPerMetre;
            double elevTol  = ELEV_TOL_M * unitsPerMetre;

            string roofStory = GetRoofStory(log, out double roofElev);

            AssignSlabLoads(cfg, roofElev, elevTol, log);
            AssignBeamLoads(cfg, roofElev, perimTol, elevTol, log);

            log.AppendLine("  NOTE  Loads applied in the model's PRESENT units. Verify Define > Units.");
            return log.ToString();
        }

        // -- Length conversion: how many model length units equal one metre -----
        private double GetLengthUnitsPerMetre()
        {
            string u = _sapModel.GetPresentUnits().ToString();
            if (u.Contains("_mm_")) return 1000.0;
            if (u.Contains("_cm_")) return 100.0;
            if (u.Contains("_in_")) return 39.3701;
            if (u.Contains("_ft_")) return 3.28084;
            return 1.0; // metres
        }

        // -- Identify the top-most story ----------------------------------------
        private string GetRoofStory(System.Text.StringBuilder log, out double maxElev)
        {
            maxElev = 0;
            int nStories = 0;
            string[] storyNames = null;
            double[] storyElev = null;
            double[] storyHt = null;
            bool[] isMaster = null;
            string[] simTo = null;
            bool[] spliceAbove = null;
            double[] spliceHt = null;

            int ret = _sapModel.Story.GetStories(
                ref nStories, ref storyNames, ref storyElev,
                ref storyHt, ref isMaster, ref simTo,
                ref spliceAbove, ref spliceHt);

            if (ret != 0 || nStories == 0 || storyNames == null || storyElev == null)
            {
                log.AppendLine("  WARN  Could not read story list — roof detection skipped " +
                               "(all areas treated as floors)");
                maxElev = double.NaN;
                return "";
            }

            int topIdx = 0;
            maxElev = storyElev[0];
            for (int i = 1; i < nStories; i++)
                if (storyElev[i] > maxElev) { maxElev = storyElev[i]; topIdx = i; }

            string roofStory = storyNames[topIdx];
            log.AppendLine($"  INFO  Roof story: '{roofStory}' at elevation {maxElev:F3} (model units)");
            return roofStory;
        }

        // -- Slab / Area object loads -------------------------------------------
        private void AssignSlabLoads(BuildingConfig cfg, double roofElev, double elevTol,
                                     System.Text.StringBuilder log)
        {
            int nAreas = 0;
            string[] areaNames = null;
            int ret = _sapModel.AreaObj.GetNameList(ref nAreas, ref areaNames);
            if (ret != 0 || nAreas == 0 || areaNames == null)
            {
                log.AppendLine("  WARN  No area objects found — slab load assignment skipped");
                return;
            }

            int floorSDL = 0, floorLL = 0, roofLL = 0, failSDL = 0, failLL = 0;

            foreach (string aName in areaNames)
            {
                double z = GetAreaCentroidZ(aName);
                bool isRoof = !double.IsNaN(roofElev) && Math.Abs(z - roofElev) <= elevTol;

                // SDL (finishes, waterproofing). Replace=true => idempotent per pattern.
                int r1 = _sapModel.AreaObj.SetLoadUniform(
                    aName, cfg.PatternSDL, cfg.SDL, DIR_GRAVITY, true, "Global", eItemType.Objects);
                if (r1 == 0) floorSDL++; else failSDL++;

                // LL (floor or roof).
                double ll = isRoof ? cfg.RoofLiveLoad : cfg.LiveLoad;
                int r2 = _sapModel.AreaObj.SetLoadUniform(
                    aName, cfg.PatternLive, ll, DIR_GRAVITY, true, "Global", eItemType.Objects);
                if (r2 == 0) { if (isRoof) roofLL++; else floorLL++; } else failLL++;
            }

            log.AppendLine($"  OK    SDL={cfg.SDL} kN/m² → {floorSDL} areas");
            log.AppendLine($"  OK    Floor LL={cfg.LiveLoad} kN/m² → {floorLL} areas");
            log.AppendLine($"  OK    Roof LL={cfg.RoofLiveLoad} kN/m² → {roofLL} areas");
            if (failSDL > 0) log.AppendLine($"  WARN  {failSDL} SDL assignment(s) failed");
            if (failLL > 0)  log.AppendLine($"  WARN  {failLL} LL assignment(s) failed");
        }

        // Average Z of an area's corner points (more robust than a single corner).
        private double GetAreaCentroidZ(string areaName)
        {
            int numPoints = 0;
            string[] pointNames = null;
            if (_sapModel.AreaObj.GetPoints(areaName, ref numPoints, ref pointNames) != 0
                || pointNames == null || numPoints == 0)
                return double.NaN;

            double sumZ = 0; int count = 0;
            foreach (var p in pointNames)
            {
                double x = 0, y = 0, z = 0;
                if (_sapModel.PointObj.GetCoordCartesian(p, ref x, ref y, ref z) == 0)
                { sumZ += z; count++; }
            }
            return count > 0 ? sumZ / count : double.NaN;
        }

        // -- Beam / Frame object loads: cladding on exterior beams --------------
        private void AssignBeamLoads(BuildingConfig cfg, double roofElev, double perimTol,
                                     double elevTol, System.Text.StringBuilder log)
        {
            int nFrames = 0;
            string[] frameNames = null;
            int ret = _sapModel.FrameObj.GetNameList(ref nFrames, ref frameNames);
            if (ret != 0 || nFrames == 0 || frameNames == null)
            {
                log.AppendLine("  WARN  No frame objects found — beam load assignment skipped");
                return;
            }

            // Single pass: read each frame's end coordinates ONCE and cache them.
            // (The previous implementation queried coordinates twice per frame.)
            var beams = new List<BeamGeom>(nFrames);
            double xMin = double.MaxValue, xMax = double.MinValue;
            double yMin = double.MaxValue, yMax = double.MinValue;

            foreach (string fName in frameNames)
            {
                string pt1 = "", pt2 = "";
                if (_sapModel.FrameObj.GetPoints(fName, ref pt1, ref pt2) != 0) continue;

                double x1 = 0, y1 = 0, z1 = 0, x2 = 0, y2 = 0, z2 = 0;
                if (_sapModel.PointObj.GetCoordCartesian(pt1, ref x1, ref y1, ref z1) != 0) continue;
                if (_sapModel.PointObj.GetCoordCartesian(pt2, ref x2, ref y2, ref z2) != 0) continue;

                double dz = Math.Abs(z2 - z1);
                double dxy = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
                bool isColumn = dz > dxy; // predominantly vertical => column

                var g = new BeamGeom
                {
                    Name = fName,
                    MidX = (x1 + x2) / 2.0,
                    MidY = (y1 + y2) / 2.0,
                    MidZ = (z1 + z2) / 2.0,
                    IsColumn = isColumn
                };
                beams.Add(g);

                if (!isColumn)
                {
                    if (g.MidX < xMin) xMin = g.MidX;
                    if (g.MidX > xMax) xMax = g.MidX;
                    if (g.MidY < yMin) yMin = g.MidY;
                    if (g.MidY > yMax) yMax = g.MidY;
                }
            }

            int nCladding = 0, nParapet = 0, nFail = 0;

            foreach (var g in beams)
            {
                if (g.IsColumn) continue;

                bool onPerim = (g.MidX <= xMin + perimTol) || (g.MidX >= xMax - perimTol) ||
                               (g.MidY <= yMin + perimTol) || (g.MidY >= yMax - perimTol);
                if (!onPerim) continue;

                bool isRoof = !double.IsNaN(roofElev) && Math.Abs(g.MidZ - roofElev) <= elevTol;

                double load = isRoof ? cfg.ParapetLoad_kNm : cfg.CladdingLoad_kNm;
                int r = ApplyBeamLineLoad(g.Name, cfg.PatternSDL, load);
                if (r == 0) { if (isRoof) nParapet++; else nCladding++; } else nFail++;
            }

            log.AppendLine($"  OK    Cladding {cfg.CladdingLoad_kNm} kN/m → {nCladding} perimeter floor beams");
            log.AppendLine($"  OK    Parapet  {cfg.ParapetLoad_kNm} kN/m → {nParapet} roof perimeter beams");
            if (nFail > 0) log.AppendLine($"  WARN  {nFail} beam assignment(s) failed");
        }

        /// <summary>
        /// Applies a uniform distributed gravity line load (kN/m) to a frame.
        /// MyType=1 (force), Dir=10 (Gravity, downward), full span, Replace=true so
        /// the SDL cladding/parapet contribution is idempotent on re-run.
        /// </summary>
        private int ApplyBeamLineLoad(string frameName, string patternName, double load_kNm)
        {
            return _sapModel.FrameObj.SetLoadDistributed(
                frameName, patternName,
                MYTYPE_FORCE,
                DIR_GRAVITY,
                0.0,          // Dist1 (start, relative)
                1.0,          // Dist2 (end, relative)
                load_kNm,     // Val1 (Gravity direction => downward, positive magnitude)
                load_kNm,     // Val2
                "Global",
                RelDist: true,
                Replace: true,  // idempotent: replaces this pattern's load on the frame
                ItemType: eItemType.Objects);
        }

        private struct BeamGeom
        {
            public string Name;
            public double MidX, MidY, MidZ;
            public bool IsColumn;
        }
    }
}
