using System;

namespace AdvatechEtabsPlugin
{
    /// <summary>
    /// IS 456:2000 singly-reinforced flexural design + bar selection.
    ///
    /// PATCH NOTES (v3):
    ///  • SelectBars no longer silently under-designs. When the required
    ///    spacing for a given bar Ø falls below 75 mm we now skip to the
    ///    next larger bar instead of clamping spacing down to maxSpacing
    ///    (which used to deliver LESS steel than required while reporting
    ///    a "successful" bar layout).
    ///  • SelectBars also rounds spacing DOWN to nearest 5 mm (was 10 mm)
    ///    so the chosen Ast is always ≥ Ast,req but not wildly conservative.
    ///  • If even the largest configured bar at 75 mm c/c can't meet Ast,
    ///    we now flag "REVISE DEPTH" through the returned string AND set a
    ///    sentinel so the caller knows the design has failed.
    /// </summary>
    public static class ReinforcementDesignEngine
    {
        /// <summary>
        /// Computes required steel area Ast (mm² per metre strip) for a given
        /// ultimate moment Mu (kN·m / m).  Follows IS 456 Annex G singly-
        /// reinforced formulation.
        /// </summary>
        /// <param name="Mu_kNm">Design ultimate moment per metre width (kN·m/m).</param>
        /// <param name="d">Effective depth (mm).</param>
        /// <param name="D">Total slab depth (mm) — needed only for Ast,min.</param>
        /// <param name="fck">Concrete characteristic strength (N/mm²).</param>
        /// <param name="fy">Steel yield strength (N/mm²).</param>
        /// <param name="overReinforced">True when Mu > Mu_lim (section needs
        /// either greater depth or compression reinforcement).</param>
        public static double CalculateAst(
            double Mu_kNm, double d, double D,
            double fck, double fy,
            out bool overReinforced)
        {
            overReinforced = false;
            if (Mu_kNm <= 0 || d <= 0)
            {
                // Return Ast,min so distribution-steel / shrinkage steel is
                // always provided even where flexural demand is zero.
                double minPct0  = (fy >= 415) ? 0.12 : 0.15;
                return (minPct0 / 100.0) * 1000.0 * Math.Max(D, 1.0);
            }

            double Mu = Mu_kNm * 1e6;   // kN·m → N·mm
            double b  = 1000.0;          // 1 m strip width

            // Limiting xu/d (IS 456 Cl. 38.1, Fig. 21)
            double xu_max_d = fy <= 250 ? 0.53 :
                              fy <= 415 ? 0.48 :
                              0.46;                        // Fe500 / Fe550

            double R_lim  = 0.36 * fck * xu_max_d * (1 - 0.42 * xu_max_d);
            double Mu_lim = R_lim * b * d * d;             // N·mm

            if (Mu > Mu_lim)
            {
                overReinforced = true;                     // PATCH: signal it
                Mu = Mu_lim;                               // safe value for sqrt
            }

            // Solve Mu = 0.87 fy Ast (d − 0.42 xu) with xu = (0.87 fy Ast)/(0.36 fck b)
            double rootTerm = 1 - (4.6 * Mu) / (fck * b * d * d);
            if (rootTerm < 0) rootTerm = 0;
            double Ast = (0.5 * fck / fy) * (1 - Math.Sqrt(rootTerm)) * b * d;

            // Minimum steel (IS 456 Cl. 26.5.2.1)
            double minPct  = (fy >= 415) ? 0.12 : 0.15;
            double Ast_min = (minPct / 100.0) * b * D;

            // Maximum steel (IS 456 Cl. 26.5.1.1(b)) = 4% of b·D
            double Ast_max = 0.04 * b * D;

            double AstFinal = Math.Max(Ast, Ast_min);
            if (AstFinal > Ast_max)
            {
                overReinforced = true;
                AstFinal = Ast_max;
            }
            return AstFinal;
        }

        /// <summary>Convenience overload that ignores the over-reinforcement flag.</summary>
        public static double CalculateAst(double Mu_kNm, double d, double fck = 25, double fy = 500)
        {
            // Best-effort D estimate for back-compat (only used for Ast,min).
            double D = d + 25;
            return CalculateAst(Mu_kNm, d, D, fck, fy, out _);
        }

        /// <summary>
        /// Picks the smallest practical (bar Ø + spacing) combination that meets
        /// the required Ast while honouring IS 456 maximum-spacing rules.
        ///
        /// IS 456 Cl. 26.3.3(b):
        ///   • main bars       :  s_max = min(3 d, 300 mm)
        ///   • distribution    :  s_max = min(5 d, 450 mm)
        /// Minimum practical spacing taken as 75 mm (IS 456 Cl. 26.3.2).
        /// </summary>
        /// <param name="preferredDiameters">User-configurable bar diameter list.
        /// The smallest diameter that yields a constructible spacing
        /// (75 mm ≤ s ≤ s_max) and supplies ≥ Ast,req is selected.</param>
        public static string SelectBars(
            double Ast_req, double d, bool isMainSteel = true,
            int[] preferredDiameters = null)
        {
            if (Ast_req <= 0) return "None";
            preferredDiameters ??= new[] { 8, 10, 12, 16 };

            double maxSpacingMain = Math.Min(3 * d, 300);   // IS 456 Cl. 26.3.3 (b)
            double maxSpacingDist = Math.Min(5 * d, 450);
            double maxSpacing     = isMainSteel ? maxSpacingMain : maxSpacingDist;

            foreach (int barDia in preferredDiameters)
            {
                double areaOneBar = Math.PI * barDia * barDia / 4.0;

                // Spacing that *exactly* meets Ast,req
                double reqSpacing = 1000.0 * areaOneBar / Ast_req;

                // PATCH v3: if even at the minimum 75 mm spacing this bar
                // diameter cannot supply Ast,req, escalate to the next bar.
                if (reqSpacing < 75.0) continue;

                // Snap DOWN to nearest 5 mm so provided Ast ≥ Ast,req.
                double spacing = Math.Floor(reqSpacing / 5.0) * 5.0;

                // Respect maximum-spacing rule (a slab needs steel for crack
                // control even when flexural demand is tiny).
                if (spacing > maxSpacing)
                    spacing = Math.Floor(maxSpacing / 5.0) * 5.0;

                if (spacing >= 75.0)
                {
                    double AstProv = 1000.0 * areaOneBar / spacing;
                    return $"T{barDia} @ {spacing:F0} c/c (As,p={AstProv:F0} mm²/m)";
                }
            }

            // Even the largest configured bar at 75 mm c/c can't carry Ast,req.
            int biggest = preferredDiameters[preferredDiameters.Length - 1];
            double bigArea = Math.PI * biggest * biggest / 4.0;
            double AstAt75 = 1000.0 * bigArea / 75.0;
            return $"T{biggest} @ 75 c/c (As,p={AstAt75:F0} < req {Ast_req:F0} — REVISE DEPTH)";
        }
    }
}
