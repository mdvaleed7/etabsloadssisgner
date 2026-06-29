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

            // FIX: the original build DISABLED this call ("ret = 1") claiming the
            // API was missing.  In reality Func.FuncRS.SetUser exists across
            // ETABS v18..v23 — it is invoked here through the version-tolerant
            // EtabsApi adapter so the IS 1893 spectrum is now defined automatically.
            int ret = EtabsApi.SetUserResponseSpectrum(_sapModel, funcName, p, v, 0.05, out string rsDet);
            if (ret != 0)
            {
                log.AppendLine($"  WARN  RS function '{funcName}' could not be set automatically " +
                               $"(ret={ret}, {rsDet}). Define the IS 1893 spectrum manually in ETABS.");
                return;
            }

            log.AppendLine($"  OK    RS function '{funcName}' — {p.Length} (T,Sa/g) points, " +
                           $"soil {cfg.SoilType}, baseline 5% damping [{rsDet}]");
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

            // FIX: these three calls were DISABLED in the original build. They are
            // genuinely available on ETABS v18..v23 and are now invoked through the
            // version-tolerant EtabsApi adapter (which probes SetModalComb /
            // SetModalComb_1 etc.), so the RS case is fully IS 1893-compliant.

            // IS 1893:2016 Cl. 7.7.5.4 — modal combination CQC (closely-spaced modes).
            int rmc = EtabsApi.SetModalCombination(_sapModel, caseName, MODALCOMB_CQC, out string mcDet);
            if (rmc != 0) log.AppendLine($"  WARN  RS case '{caseName}' SetModalComb (ret={rmc}, {mcDet})");

            // Directional combination SRSS.
            int rdc = EtabsApi.SetDirectionalCombination(_sapModel, caseName, DIRCOMB_SRSS, out string dcDet);
            if (rdc != 0) log.AppendLine($"  WARN  RS case '{caseName}' SetDirComb (ret={rdc}, {dcDet})");

            // Constant modal damping per IS 1893 Cl. 7.2.4 (5% for RC/steel).
            int rdamp = EtabsApi.SetConstantDamping(_sapModel, caseName, cfg.DampingRatio, out string dmpDet);
            if (rdamp != 0) log.AppendLine($"  WARN  RS case '{caseName}' SetDampConstant (ret={rdamp}, {dmpDet})");

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
            // FIX: the original build DISABLED the mass-source call and threw.
            // The mass source IS settable across ETABS v18..v23 — on some builds
            // via SapModel.SourceMass.SetMassSource and on others via
            // SapModel.PropMaterial.SetMassSource_1.  The EtabsApi adapter probes
            // both homes and the 8-/9-argument signatures, so this now succeeds
            // automatically and only degrades to a manual hint if no variant is
            // present on the host build.
            int ret = EtabsApi.SetMassSource(_sapModel, "MsSrc1", loadPats, sf, out string msDet);
            if (ret != 0)
            {
                log.AppendLine($"  WARN  Mass source not set automatically (ret={ret}, {msDet}). " +
                               "Set Define ▸ Mass Source manually.");
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
