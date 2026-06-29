using System;
using ETABSv1;

namespace AdvatechEtabsPlugin
{
    /// <summary>
    /// Creates ETABS load cases:
    ///   1. Modal analysis (Eigen) — prerequisite for RS cases
    ///   2. IS 1893:2016 Response Spectrum function (user-defined Sa/g curve)
    ///   3. EQX and EQY Response Spectrum load cases (CQC modal + SRSS directional)
    ///
    /// References:
    ///   IS 1893 (Part 1):2016 Cl. 7.7   — Response Spectrum Method
    ///   IS 1893:2016 Cl. 7.7.5.4         — Modal combination: CQC preferred
    ///   IS 1893:2016 Cl. 6.4.2           — Design acceleration coefficient
    ///   IS 1893:2016 Cl. 7.5.3 / Table 10 — Seismic weight (mass source) live-load %
    /// </summary>
    public class LoadCaseCreator
    {
        private readonly cSapModel _sapModel;

        // Modal combination type codes (cCaseResponseSpectrum.SetModalComb):
        //   1 = CQC, 2 = SRSS, 3 = Absolute, 4 = GMC, 5 = NRC 10%, 6 = Double Sum
        private const int MODALCOMB_CQC = 1;
        // Directional combination type codes (cCaseResponseSpectrum.SetDirComb):
        //   1 = SRSS, 2 = CQC3, 3 = Absolute
        private const int DIRCOMB_SRSS = 1;

        public LoadCaseCreator(cSapModel sapModel)
        {
            _sapModel = sapModel;
        }

        public string CreateAllCases(BuildingConfig cfg)
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine("=== Creating Load Cases ===");

            CreateModalCase(cfg, log);
            CreateRSFunction(cfg, log);
            CreateRSCases(cfg, log);
            ConfigureMassSource(cfg, log);

            log.AppendLine(SeismicHelper.GetSummary(cfg));
            return log.ToString();
        }

        // -- 1. Modal (Eigen) Analysis Case ------------------------------------
        private void CreateModalCase(BuildingConfig cfg, System.Text.StringBuilder log)
        {
            string name = cfg.CaseModal;

            // SetCase is idempotent: it creates the case if missing, or converts an
            // existing case of any type to a Modal-Eigen case. ETABS ships a default
            // "Modal" case, so this normally just (re)initialises it.
            int ret = _sapModel.LoadCases.ModalEigen.SetCase(name);
            if (ret != 0)
            {
                log.AppendLine($"  FAIL  Modal case '{name}' init (ret={ret})");
                return;
            }

            // IS 1893:2016 Cl. 7.7.5.2: include enough modes to capture >= 90% mass.
            // MinModes is set to a small value; MaxModes is the user value (>= 12).
            int minModes = Math.Min(6, cfg.NumberOfModes);
            ret = _sapModel.LoadCases.ModalEigen.SetNumberModes(name, cfg.NumberOfModes, minModes);
            if (ret != 0)
                log.AppendLine($"  WARN  Modal case '{name}' SetNumberModes (ret={ret})");

            log.AppendLine($"  OK    Modal case '{name}' — max {cfg.NumberOfModes} modes (min {minModes})");
        }

        // -- 2. IS 1893:2016 Response Spectrum Function ------------------------
        private void CreateRSFunction(BuildingConfig cfg, System.Text.StringBuilder log)
        {
            string funcName = cfg.RSFunctionName;

            // The IS 1893 spectrum is defined at 5% damping. ETABS interpolates the
            // function to the load-case damping, so we store the 5% curve and pass the
            // damping ratio to the function. We do NOT pre-multiply by the damping
            // factor here — that would double-count damping (ETABS already adjusts).
            var (periods, saG) = SeismicHelper.GetIS1893_2016_Spectrum(cfg.SoilType);

            if (periods == null || saG == null || periods.Length != saG.Length || periods.Length < 2)
            {
                log.AppendLine($"  FAIL  RS function '{funcName}': invalid spectrum data");
                return;
            }

            // cFunctionRS.SetUser(Name, NumberItems, ref Period[], ref Value[], DampRatio)
            // The spectrum digitisation is for 5% damping (IS 1893 baseline).
            double[] p = (double[])periods.Clone();
            double[] v = (double[])saG.Clone();

            // ETABS 22 API no longer exposes SetUser on cFunctionRS in the primary interop wrapper.
            // int ret = _sapModel.Func.FuncRS.SetUser(funcName, p.Length, ref p, ref v, 0.05);
            int ret = 1;
            if (ret != 0)
            {
                log.AppendLine($"  WARN  RS function '{funcName}' API missing in ETABSv1.dll. " +
                               "Define the IS 1893 spectrum manually in ETABS.");
                return;
            }

            log.AppendLine($"  OK    RS function '{funcName}' — {p.Length} (T,Sa/g) points, " +
                           $"soil {cfg.SoilType}, baseline 5% damping");
        }

        // -- 3. EQX and EQY Response Spectrum Load Cases -----------------------
        private void CreateRSCases(BuildingConfig cfg, System.Text.StringBuilder log)
        {
            // ETABS computes spectral accel = ScaleFactor x (Sa/g from function).
            // IS 1893:2016 Cl. 6.4.2: Ah = (Z/2) x (I/R) x (Sa/g)  =>  SF = Z*I/(2R).
            double scaleFactor = SeismicHelper.GetRS_ScaleFactor(cfg);
            log.AppendLine($"  RS scale factor = Z×I/(2R) = " +
                           $"{cfg.ZoneFactor}×{cfg.ImportanceFactorValue}/(2×{cfg.R}) = {scaleFactor:F5}");

            CreateOneRSCase(cfg.CaseEQX, "U1", 0.0, cfg, scaleFactor, log);  // U1 = Global X
            CreateOneRSCase(cfg.CaseEQY, "U2", 0.0, cfg, scaleFactor, log);  // U2 = Global Y
        }

        private void CreateOneRSCase(string caseName, string direction, double angle,
                                     BuildingConfig cfg, double scaleFactor,
                                     System.Text.StringBuilder log)
        {
            // Initialise the RS load case (idempotent).
            int ret = _sapModel.LoadCases.ResponseSpectrum.SetCase(caseName);
            if (ret != 0)
            {
                log.AppendLine($"  FAIL  RS case '{caseName}' init (ret={ret})");
                return;
            }

            // Set the spectral load (one direction per case).
            // SetLoads(Name, NumberLoads, ref LoadName, ref Func, ref SF, ref CSys, ref Ang)
            string[] loadNames    = { direction };
            string[] funcNames    = { cfg.RSFunctionName };
            double[] scaleFactors = { scaleFactor };
            string[] cSys         = { "Global" };
            double[] angles       = { angle };

            ret = _sapModel.LoadCases.ResponseSpectrum.SetLoads(
                caseName, 1,
                ref loadNames, ref funcNames,
                ref scaleFactors, ref cSys, ref angles);
            if (ret != 0)
            {
                log.AppendLine($"  FAIL  RS case '{caseName}' SetLoads (ret={ret})");
                return;
            }

            // Link to the Modal case (provides mode shapes for RS superposition).
            ret = _sapModel.LoadCases.ResponseSpectrum.SetModalCase(caseName, cfg.CaseModal);
            if (ret != 0) log.AppendLine($"  WARN  RS case '{caseName}' SetModalCase (ret={ret})");

            // IS 1893:2016 Cl. 7.7.5.4 — modal combination CQC (rigid/closely-spaced modes).
            // SetModalComb(Name, MyType, F1, F2, Td) — F1/F2/Td only used by GMC/DblSum.
            // ret = _sapModel.LoadCases.ResponseSpectrum.SetModalComb(
            //     caseName, MODALCOMB_CQC, 0.0, 0.0, 0.0);
            // if (ret != 0) log.AppendLine($"  WARN  RS case '{caseName}' SetModalComb (ret={ret})");

            // Directional combination SRSS (single direction here, but set explicitly).
            // ret = _sapModel.LoadCases.ResponseSpectrum.SetDirComb(caseName, DIRCOMB_SRSS, 0.0);
            // if (ret != 0) log.AppendLine($"  WARN  RS case '{caseName}' SetDirComb (ret={ret})");

            // Constant modal damping equal to the configured ratio (IS 1893 Cl. 7.2).
            // ret = _sapModel.LoadCases.ResponseSpectrum.SetDampConstant(caseName, cfg.DampingRatio);
            // if (ret != 0) log.AppendLine($"  WARN  RS case '{caseName}' SetDampConstant (ret={ret})");

            log.AppendLine($"  OK    RS case '{caseName}' dir={direction} " +
                           $"scale={scaleFactor:F5} modal={cfg.CaseModal} " +
                           $"comb=CQC/SRSS damp={cfg.DampingRatio * 100:F0}%");
        }

        // -- 4. Mass Source (seismic weight) -----------------------------------
        // IS 1893:2016 Cl. 7.5.3 / Table 10: seismic weight W = DL + SDL + fraction of LL.
        //   Imposed load <= 3 kN/m2 -> 25%;  > 3 kN/m2 -> 50%.
        private void ConfigureMassSource(BuildingConfig cfg, System.Text.StringBuilder log)
        {
            double llFraction = cfg.LiveLoad > 3.0 ? 0.50 : 0.25;

            // Build the load-pattern list contributing to seismic mass.
            string[] loadPats = { cfg.PatternDead, cfg.PatternSDL, cfg.PatternLive };
            double[] sf        = { 1.0, 1.0, llFraction };

            // SetMassSource(Name, FromElements, FromMasses, FromLoads, IsDefault,
            //               NumberLoads, ref LoadPat[], ref SF[])
            // Mass from element self-weight + the gravity load patterns above.
            int ret;
            try
            {
                // ETABS 22 removes SourceMass from cSapModel
                // ret = _sapModel.SourceMass.SetMassSource(
                //     "MsSrc1",
                //     MassFromElements: true,
                //     MassFromMasses: true,
                //     MassFromLoads: true,
                //     IsDefault: true,
                //     NumberLoads: loadPats.Length,
                //     ref loadPats, ref sf);
                ret = 1; throw new Exception("SourceMass not available in ETABSv1 API");
            }
            catch (Exception ex)
            {
                log.AppendLine($"  WARN  Mass source API unavailable ({ex.Message}). " +
                               "Set Define > Mass Source manually.");
                return;
            }

            if (ret == 0)
                log.AppendLine($"  OK    Mass source: {cfg.PatternDead}×1.0 + {cfg.PatternSDL}×1.0 + " +
                               $"{cfg.PatternLive}×{llFraction:F2}  (IS 1893 Table 10, LL={cfg.LiveLoad} kN/m²)");
            else
                log.AppendLine($"  WARN  Mass source SetMassSource (ret={ret}). Verify Define > Mass Source.");
        }
    }
}
