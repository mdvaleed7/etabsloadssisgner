using System;
using System.Collections.Generic;
using System.Text;
using ETABSv1;

namespace AdvatechEtabsPlugin
{
    /// <summary>Outcome of one RS-scaling run, surfaced to the UI / report.</summary>
    public class RsScalingResult
    {
        public bool Success;
        public double StaticBaseShear;       // V_B (equivalent static), kN
        public double SpectrumBaseShearInit; // V_spec before scaling, kN
        public double SpectrumBaseShearFinal;// V_spec after scaling, kN
        public double InitialScaleFactor;
        public double FinalScaleFactor;
        public int Iterations;
        public string Log = "";
    }

    /// <summary>
    /// FEATURE 3 — Automatic Response-Spectrum base-shear scaling
    /// (IS 1893 (Part 1):2016 Cl. 7.7.3).
    ///
    /// ───────────────────────────────────────────────────────────────────────
    /// ENGINEERING LOGIC
    /// ───────────────────────────────────────────────────────────────────────
    /// IS 1893:2016 Cl. 7.7.3 requires that when the dynamic (response-spectrum)
    /// base shear VB,dyn is LESS than the equivalent-static base shear VB,stat,
    /// all dynamic response quantities (member forces, drifts, …) be scaled up
    /// by the factor
    ///
    ///       λ = VB,stat / VB,dyn        (applied only when VB,dyn < VB,stat)
    ///
    /// (The 2016 code dropped the earlier "0.9·VB" relaxation that IS 1893:2002
    ///  applied to regular buildings, so the target is the FULL static shear.)
    ///
    /// ───────────────────────────────────────────────────────────────────────
    /// ALGORITHM
    /// ───────────────────────────────────────────────────────────────────────
    ///   1. Ensure both the equivalent-static EQ case and the RS case are
    ///      flagged to run; run the analysis.
    ///   2. Read VB,stat (from the static EQ case) and VB,dyn (from the RS case)
    ///      via Results.BaseReact.
    ///   3. If VB,dyn ≥ VB,stat → no scaling needed (λ target = 1).
    ///   4. Else compute λ = VB,stat / VB,dyn, multiply it INTO the RS-case scale
    ///      factor, re-run, and re-read VB,dyn.  Repeat until
    ///      |VB,dyn − VB,stat| / VB,stat ≤ tol (default 1%) or max iterations.
    ///
    /// Because the response spectrum is linear, a single λ correction is
    /// theoretically exact; the loop simply confirms convergence and absorbs
    /// any rounding / participating-mass round-off, satisfying the "±1%"
    /// requirement in the specification.
    ///
    /// ───────────────────────────────────────────────────────────────────────
    /// SCALING TARGET (spec option)
    /// ───────────────────────────────────────────────────────────────────────
    /// The scale is applied THROUGH THE RESPONSE-SPECTRUM LOAD CASE (the
    /// physically-correct place per Cl. 7.7.3 — it scales every dynamic result
    /// consistently).  An alternative "apply λ in the load combinations" mode is
    /// also provided (<see cref="ScaleViaCombination"/>) for projects that must
    /// keep the RS case at its code scale and instead multiply the seismic term
    /// inside the design combinations.
    /// </summary>
    public class ResponseSpectrumScaler
    {
        private readonly cSapModel _sapModel;

        public double Tolerance { get; set; } = 0.01;   // ±1%
        public int MaxIterations { get; set; } = 8;

        public ResponseSpectrumScaler(cSapModel sapModel)
        {
            _sapModel = sapModel;
        }

        /// <summary>
        /// Iteratively scales the RS case so its base shear matches the static
        /// base shear within tolerance.
        /// </summary>
        /// <param name="staticCase">Equivalent-static EQ load case/pattern name.</param>
        /// <param name="rsCase">Response-spectrum load case name.</param>
        /// <param name="alongX">True to compare the X base-shear component, false for Y.</param>
        public RsScalingResult ScaleViaCase(string staticCase, string rsCase, bool alongX)
        {
            var r = new RsScalingResult();
            var log = new StringBuilder();
            log.AppendLine("=== Response-Spectrum Scaling (IS 1893:2016 Cl. 7.7.3) ===");
            log.AppendLine($"  Static case: {staticCase}   RS case: {rsCase}   " +
                           $"direction: {(alongX ? "X" : "Y")}   tol: ±{Tolerance * 100:F1}%");

            if (_sapModel == null) { log.AppendLine("  FAIL  not connected"); r.Log = log.ToString(); return r; }

            // Read the current RS scale factor (so λ multiplies the EXISTING value).
            int gs = EtabsApi.GetResponseSpectrumScale(_sapModel, rsCase, out double currentSF, out string sfDet);
            if (gs != 0 || double.IsNaN(currentSF) || currentSF <= 0)
            {
                log.AppendLine($"  WARN  could not read current RS scale ({sfDet}); assuming 1.0.");
                currentSF = 1.0;
            }
            r.InitialScaleFactor = currentSF;
            log.AppendLine($"  Initial RS scale factor = {currentSF:F5}");

            // Run analysis once to obtain the baseline shears.
            if (!RunAndRead(staticCase, rsCase, alongX, log,
                            out double vStat, out double vDyn)) { r.Log = log.ToString(); return r; }

            r.StaticBaseShear = vStat;
            r.SpectrumBaseShearInit = vDyn;
            log.AppendLine($"  VB,static = {vStat:F2} kN   VB,dynamic = {vDyn:F2} kN   " +
                           $"(VB,dyn/VB,stat = {SafeRatio(vDyn, vStat):F3})");

            if (vStat <= 0 || vDyn <= 0)
            {
                log.AppendLine("  FAIL  non-positive base shear — check that both cases ran and produced results.");
                r.Log = log.ToString(); return r;
            }

            if (vDyn >= vStat * (1.0 - Tolerance))
            {
                log.AppendLine("  RESULT  VB,dynamic already ≥ VB,static (within tolerance) — no up-scaling required (Cl. 7.7.3).");
                r.Success = true;
                r.FinalScaleFactor = currentSF;
                r.SpectrumBaseShearFinal = vDyn;
                r.Iterations = 0;
                r.Log = log.ToString();
                return r;
            }

            double sf = currentSF;
            int iter = 0;
            while (iter < MaxIterations)
            {
                iter++;
                double lambda = vStat / vDyn;
                sf *= lambda;
                log.AppendLine($"  Iter {iter}: λ = VB,stat/VB,dyn = {vStat:F2}/{vDyn:F2} = {lambda:F4} " +
                               $"→ new RS scale = {sf:F5}");

                int setRet = SetRsScale(rsCase, sf, out string setDet);
                if (setRet != 0)
                {
                    log.AppendLine($"  FAIL  could not update RS scale (ret={setRet}, {setDet}).");
                    break;
                }

                if (!RunAndRead(staticCase, rsCase, alongX, log, out vStat, out vDyn)) break;
                log.AppendLine($"         VB,dynamic = {vDyn:F2} kN  (ratio {SafeRatio(vDyn, vStat):F3})");

                if (Math.Abs(vDyn - vStat) / vStat <= Tolerance)
                {
                    log.AppendLine($"  CONVERGED in {iter} iteration(s): within ±{Tolerance * 100:F1}%.");
                    r.Success = true;
                    break;
                }
            }

            r.FinalScaleFactor = sf;
            r.SpectrumBaseShearFinal = vDyn;
            r.Iterations = iter;
            if (!r.Success)
                log.AppendLine($"  WARN  did not converge in {MaxIterations} iterations; final scale = {sf:F5}.");

            log.AppendLine("  SUMMARY");
            log.AppendLine($"    Static base shear   : {r.StaticBaseShear:F2} kN");
            log.AppendLine($"    Spectrum (initial)  : {r.SpectrumBaseShearInit:F2} kN");
            log.AppendLine($"    Spectrum (final)    : {r.SpectrumBaseShearFinal:F2} kN");
            log.AppendLine($"    Initial scale factor: {r.InitialScaleFactor:F5}");
            log.AppendLine($"    Final scale factor  : {r.FinalScaleFactor:F5}");
            log.AppendLine($"    Iterations          : {r.Iterations}");
            r.Log = log.ToString();
            return r;
        }

        /// <summary>
        /// Alternative (Cl. 7.7.3 option): keep the RS case at its code scale and
        /// instead report the multiplier to apply to the seismic term inside the
        /// design load combinations.  Does NOT modify the RS case scale.
        /// </summary>
        public RsScalingResult ScaleViaCombination(string staticCase, string rsCase, bool alongX)
        {
            var r = new RsScalingResult();
            var log = new StringBuilder();
            log.AppendLine("=== RS Scaling via Load Combinations (IS 1893:2016 Cl. 7.7.3) ===");

            if (!RunAndRead(staticCase, rsCase, alongX, log, out double vStat, out double vDyn))
            { r.Log = log.ToString(); return r; }

            r.StaticBaseShear = vStat;
            r.SpectrumBaseShearInit = vDyn;
            double lambda = (vDyn > 0 && vDyn < vStat) ? vStat / vDyn : 1.0;
            r.InitialScaleFactor = 1.0;
            r.FinalScaleFactor = lambda;
            r.SpectrumBaseShearFinal = vDyn * lambda;
            r.Iterations = 0;
            r.Success = true;

            log.AppendLine($"  VB,static={vStat:F2} kN  VB,dynamic={vDyn:F2} kN");
            log.AppendLine($"  RECOMMENDED combination multiplier on '{rsCase}' = {lambda:F4}");
            log.AppendLine("  (Apply this factor to the RS term in each seismic combination; " +
                           "the RS case itself keeps its IS 1893 Cl. 6.4.2 scale.)");
            r.Log = log.ToString();
            return r;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private bool RunAndRead(string staticCase, string rsCase, bool alongX,
                                StringBuilder log, out double vStat, out double vDyn)
        {
            vStat = vDyn = double.NaN;

            int ra = EtabsApi.RunAnalysis(_sapModel, out string runDet);
            if (ra != 0)
            {
                log.AppendLine($"  FAIL  RunAnalysis returned {ra} ({runDet}). " +
                               "Ensure the model is analysable (sections, supports, mass).");
                return false;
            }

            EtabsApi.SetCaseSelectedForOutput(_sapModel, staticCase, out _);
            int b1 = EtabsApi.GetBaseShear(_sapModel, staticCase, out double sx, out double sy, out string d1);
            EtabsApi.SetCaseSelectedForOutput(_sapModel, rsCase, out _);
            int b2 = EtabsApi.GetBaseShear(_sapModel, rsCase, out double dx, out double dy, out string d2);

            if (b1 != 0) { log.AppendLine($"  FAIL  base shear for '{staticCase}' ({d1})."); return false; }
            if (b2 != 0) { log.AppendLine($"  FAIL  base shear for '{rsCase}' ({d2})."); return false; }

            vStat = Math.Abs(alongX ? sx : sy);
            vDyn  = Math.Abs(alongX ? dx : dy);
            return true;
        }

        private int SetRsScale(string rsCase, double newScale, out string detail)
        {
            detail = "";
            // Re-read the RS load definition, then re-set with the new scale.
            // We reuse SetLoads via the strongly-typed interface for the common
            // single-direction case (the EtabsApi GetResponseSpectrumScale read
            // confirmed the structure); fall back to a direct SetLoads here.
            var rs = _sapModel.LoadCases.ResponseSpectrum;
            int num = 0;
            string[] ln = null, fn = null, cs = null; double[] sf = null, ang = null;
            int g = rs.GetLoads(rsCase, ref num, ref ln, ref fn, ref sf, ref cs, ref ang);
            if (g != 0 || num <= 0 || ln == null)
            {
                detail = $"GetLoads ret={g}";
                return g == 0 ? -1 : g;
            }
            for (int i = 0; i < sf.Length; i++) sf[i] = newScale;
            int s = rs.SetLoads(rsCase, num, ref ln, ref fn, ref sf, ref cs, ref ang);
            detail = "SetLoads";
            return s;
        }

        private static double SafeRatio(double a, double b) => b == 0 ? 0 : a / b;
    }
}
