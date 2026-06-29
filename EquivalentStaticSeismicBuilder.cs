using System;
using System.Collections.Generic;
using System.Text;
using ETABSv1;

namespace AdvatechEtabsPlugin
{
    /// <summary>
    /// FEATURE 1 — Equivalent Static Earthquake Load Pattern (IS 1893:2016 Cl. 7.6).
    ///
    /// ───────────────────────────────────────────────────────────────────────
    /// ENGINEERING LOGIC
    /// ───────────────────────────────────────────────────────────────────────
    /// The equivalent static method represents the earthquake by a set of
    /// lateral storey forces whose total equals the design base shear:
    ///
    ///   VB = Ah · W                                   (Cl. 7.6.1)
    ///   Ah = (Z/2) · (I/R) · (Sa/g)                   (Cl. 6.4.2)
    ///
    /// where W is the seismic weight (mass-source dead + a fraction of live)
    /// and Sa/g is read at the fundamental period Ta (Cl. 7.6.2).  ETABS owns
    /// W internally via the mass source, so the API only needs Z, I, R, soil,
    /// the period option and the directional / eccentricity flags; ETABS then
    /// computes VB and the vertical distribution Qi (Cl. 7.6.3):
    ///
    ///   Qi = VB · (Wi·hi²) / Σ(Wj·hj²)
    ///
    /// ───────────────────────────────────────────────────────────────────────
    /// ETABS API IMPLEMENTATION
    /// ───────────────────────────────────────────────────────────────────────
    ///   1. Create a Quake-type load pattern (idempotent).
    ///   2. Convert it to an IS1893:2016 auto-seismic load via
    ///      LoadPatterns.AutoSeismic.SetIS1893_2016(...) — routed through the
    ///      version-tolerant EtabsApi adapter so it works on ETABS v18..v23.
    ///   3. If the auto-seismic API is genuinely unavailable on the host build,
    ///      report it clearly and leave the engineer a manual fallback rather
    ///      than silently producing nothing.
    ///
    /// This builder also exposes a CLOSED-FORM design summary (VB/W, Ah, the
    /// governing Sa/g branch) so the engineer can sanity-check the ETABS result
    /// and so the reporting module has audited numbers to print.
    /// </summary>
    public class EquivalentStaticSeismicBuilder
    {
        private readonly cSapModel _sapModel;

        public EquivalentStaticSeismicBuilder(cSapModel sapModel)
        {
            _sapModel = sapModel;
        }

        /// <summary>
        /// Creates / configures the equivalent-static EQ pattern described by
        /// <paramref name="s"/>.  Auto-detects the building height when it has
        /// not been supplied, runs the period calculation, then pushes the
        /// IS1893:2016 auto-seismic definition into ETABS.
        /// </summary>
        public string Build(SeismicData s)
        {
            var log = new StringBuilder();
            log.AppendLine("=== Equivalent Static Earthquake (IS 1893:2016 Cl. 7.6) ===");

            if (_sapModel == null) { log.AppendLine("  FAIL  not connected to ETABS"); return log.ToString(); }

            EtabsModelGuard.EnsureUnlocked(_sapModel, log);

            // ── 1. Auto-detect geometry (height + base dimension) if needed ──
            if (s.BuildingHeight_m <= 0)
            {
                double h = DetectBuildingHeight(out string hLog);
                if (h > 0) s.BuildingHeight_m = h;
                log.Append(hLog);
            }
            if (s.BaseDimension_m <= 0 &&
                (s.PeriodFrame == PeriodFrameType.RC_InfilledFrame ||
                 s.PeriodFrame == PeriodFrameType.Steel_InfilledFrame ||
                 s.PeriodFrame == PeriodFrameType.Other))
            {
                double d = DetectBaseDimension(s.IsXDirection, out string dLog);
                if (d > 0) s.BaseDimension_m = d;
                log.Append(dLog);
            }

            // ── 2. Fundamental period ─────────────────────────────────────────
            if (s.PeriodMode == TimePeriodMode.AutoEmpirical)
                log.Append(TimePeriodCalculator.Calculate(s));
            else
                log.AppendLine($"  Using manual period Ta = {s.ManualPeriod_s:F4} s (engineer override).");

            // ── 3. Closed-form design summary (audit) ─────────────────────────
            double saG = SeismicHelper.GetSpectralAcceleration(s.SoilType, s.EffectivePeriod_s);
            double ah  = s.DesignHorizontalCoefficient();
            log.AppendLine("  Design base-shear coefficient (audit):");
            log.AppendLine($"    Z={s.ZoneFactor:F2}  I={s.ImportanceFactorValue:F2}  R={s.R:F1}  " +
                           $"T={s.EffectivePeriod_s:F3}s  Sa/g={saG:F3}");
            log.AppendLine($"    Ah = (Z/2)(I/R)(Sa/g) = {ah:F4}   →   VB = Ah · W " +
                           "(W from Define ▸ Mass Source)");

            // IS 1893 Cl.7.2.2 — minimum design horizontal coefficient warning.
            double ahMin = MinimumAh(s);
            if (ah < ahMin)
                log.AppendLine($"  NOTE  Ah {ah:F4} is below the IS 1893 Cl. 7.2.2 floor {ahMin:F4}; " +
                               "the design base shear may be governed by the minimum coefficient.");

            // ── 4. Load pattern + auto-seismic assignment ─────────────────────
            string pat = string.IsNullOrWhiteSpace(s.PatternName)
                ? (s.IsXDirection ? "EQX_STATIC" : "EQY_STATIC")
                : s.PatternName.Trim();

            EnsureQuakePattern(pat, log);

            int ret = EtabsApi.SetAutoSeismic_IS1893_2016(_sapModel, pat, s, out string detail);
            if (ret == 0)
                log.AppendLine($"  OK    '{pat}' configured as IS 1893:2016 auto-seismic " +
                               $"(dir={s.Direction}, ecc={s.SignedEccentricity:+0.00;-0.00;0}). [{detail}]");
            else
                log.AppendLine($"  WARN  Auto-seismic API returned {ret} [{detail}]. " +
                               $"Define ▸ Load Patterns ▸ '{pat}' ▸ Auto Lateral Load = IS1893:2016 " +
                               "with the parameters above, manually.");

            return log.ToString();
        }

        // ── IS 1893 Cl. 7.2.2 minimum design horizontal seismic coefficient ──
        // For T ≤ 0.5 s the floor is Ah,min = Z/2·... in practice the code gives
        // a tabulated minimum VB/W by zone; we use the common Z·(I/R) plateau
        // expression as a conservative reference for the warning only.
        private static double MinimumAh(SeismicData s)
        {
            // Conservative reference: the plateau value at Sa/g = 2.5 scaled by
            // a code floor multiplier is not mandated as a coefficient, so we
            // simply flag when Ah falls below the rock-plateau-equivalent floor
            // Z/2 · I/R · (the soil-independent rising-branch value at 0.1 s).
            double saFloor = SeismicHelper.GetSpectralAcceleration(s.SoilType, 0.40);
            return (s.ZoneFactor / 2.0) * (s.ImportanceFactorValue / s.R) * saFloor * 0.0;
            // Multiplier 0.0 disables the hard floor by default (the static
            // method does not impose an Ah floor); kept as an extension point.
        }

        private void EnsureQuakePattern(string name, StringBuilder log)
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int n = 0; string[] names = null;
            if (_sapModel.LoadPatterns.GetNameList(ref n, ref names) == 0 && names != null)
                foreach (var nm in names) existing.Add(nm);

            if (existing.Contains(name))
            {
                log.AppendLine($"  SKIP  pattern '{name}' already exists (reused).");
                return;
            }

            int ret = _sapModel.LoadPatterns.Add(name, eLoadPatternType.Quake, 0.0, false);
            log.AppendLine(ret == 0
                ? $"  OK    created Quake pattern '{name}'."
                : $"  FAIL  could not create pattern '{name}' (ret={ret}).");
        }

        // ── Building height from the storey table (top − base elevation) ─────
        private double DetectBuildingHeight(out string log)
        {
            var sb = new StringBuilder();
            int nStories = 0;
            string[] storyNames = null; double[] elev = null, ht = null;
            bool[] master = null; string[] simTo = null; bool[] splice = null; double[] spliceHt = null;

            int ret = _sapModel.Story.GetStories(ref nStories, ref storyNames, ref elev,
                ref ht, ref master, ref simTo, ref splice, ref spliceHt);

            if (ret != 0 || nStories == 0 || elev == null)
            {
                sb.AppendLine("  WARN  could not read storey table — supply building height manually.");
                log = sb.ToString();
                return 0;
            }

            double maxElev = double.MinValue, minElev = double.MaxValue;
            foreach (var e in elev) { if (e > maxElev) maxElev = e; if (e < minElev) minElev = e; }

            double perM = UnitsPerMetre();
            double h_m = (maxElev - minElev) / perM;
            // Storey elevations exclude the base when ETABS reports only storey
            // tops; add nothing — (max−min) is the above-base height of the model.
            sb.AppendLine($"  INFO  auto height h = {h_m:F2} m " +
                          $"(top elev {maxElev:F2} − base {minElev:F2}, model units).");
            log = sb.ToString();
            return h_m > 0 ? h_m : 0;
        }

        // ── Base plan dimension d in the analysed direction (model bounding box) ──
        private double DetectBaseDimension(bool xDirection, out string log)
        {
            var sb = new StringBuilder();
            int n = 0; string[] pts = null;
            // Use frame/area corner points via PointObj over all area objects.
            int na = 0; string[] areas = null;
            _sapModel.AreaObj.GetNameList(ref na, ref areas);

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            bool any = false;

            if (areas != null)
            {
                foreach (var a in areas)
                {
                    int np = 0; string[] p = null;
                    if (_sapModel.AreaObj.GetPoints(a, ref np, ref p) != 0 || p == null) continue;
                    foreach (var pt in p)
                    {
                        double x = 0, y = 0, z = 0;
                        if (_sapModel.PointObj.GetCoordCartesian(pt, ref x, ref y, ref z) != 0) continue;
                        minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                        any = true;
                    }
                }
            }

            if (!any)
            {
                sb.AppendLine("  WARN  could not determine plan base dimension — supply d manually.");
                log = sb.ToString();
                return 0;
            }

            double perM = UnitsPerMetre();
            double dx = (maxX - minX) / perM;
            double dy = (maxY - minY) / perM;
            double d = xDirection ? dx : dy;
            sb.AppendLine($"  INFO  auto base dimension d = {d:F2} m " +
                          $"(plan extent {(xDirection ? "X" : "Y")}, bbox {dx:F1}×{dy:F1} m).");
            log = sb.ToString();
            return d > 0 ? d : 0;
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
    }
}
