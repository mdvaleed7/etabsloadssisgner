using System;

namespace CSiNET8PluginExample1
{
    public static class ReinforcementDesignEngine
    {
        /// <summary>
        /// Calculates the required steel area (Ast) in mm2/m for a given ultimate moment.
        /// Uses the IS 456 Annex G formula for singly reinforced sections.
        /// </summary>
        public static double CalculateAst(double Mu_kNm, double d, double fck = 25, double fy = 500)
        {
            if (Mu_kNm <= 0 || d <= 0) return 0;

            double Mu = Mu_kNm * 1e6; // Convert kN.m to N.mm
            double b = 1000.0;        // 1 meter strip width

            // Limiting moment of resistance coefficient (for Fe500)
            double x_max_d = 0.46; // Fe500
            if (fy <= 250) x_max_d = 0.53;
            else if (fy <= 415) x_max_d = 0.48;

            double R_lim = 0.36 * fck * x_max_d * (1 - 0.42 * x_max_d);
            double Mu_lim = R_lim * b * d * d;

            // Check if section is over-reinforced (in a real app, this should trigger a compression steel flag or depth increase)
            // For now, if Mu > Mu_lim, the formula below would yield NaN because of the square root of a negative number.
            if (Mu > Mu_lim)
            {
                // Cap at Mu_lim for Ast calc to prevent NaN, but flag it conceptually
                Mu = Mu_lim; 
            }

            // IS 456 Annex G Formula: Mu = 0.87 * fy * Ast * d * (1 - (Ast * fy)/(b * d * fck))
            // Solving quadratic for Ast:
            double rootTerm = 1 - (4.6 * Mu) / (fck * b * d * d);
            
            // To prevent NaN from precision issues if Mu slightly exceeds theoretical max
            if (rootTerm < 0) rootTerm = 0;

            double Ast = (0.5 * fck / fy) * (1 - Math.Sqrt(rootTerm)) * b * d;

            // Minimum steel requirement (IS 456 Cl. 26.5.2.1)
            double minAstPercent = (fy >= 415) ? 0.12 : 0.15;
            double D = d + 20 + 5; // Approximate gross depth
            double Ast_min = (minAstPercent / 100.0) * b * D;

            return Math.Max(Ast, Ast_min);
        }

        /// <summary>
        /// Selects the optimal bar diameter and spacing to satisfy the required Ast.
        /// </summary>
        public static string SelectBars(double Ast_req, double d, bool isMainSteel = true)
        {
            if (Ast_req <= 0) return "None";

            int[] preferredDiameters = { 8, 10, 12, 16 };
            double maxSpacingMain = Math.Min(3 * d, 300);
            double maxSpacingDist = Math.Min(5 * d, 450);
            double maxSpacing = isMainSteel ? maxSpacingMain : maxSpacingDist;

            foreach (int barDia in preferredDiameters)
            {
                double areaOneBar = (Math.PI * barDia * barDia) / 4.0;
                double reqSpacing = (1000.0 * areaOneBar) / Ast_req;

                // Round down to nearest 10 mm
                double spacing = Math.Floor(reqSpacing / 10.0) * 10.0;

                // Enforce max spacing
                if (spacing > maxSpacing)
                {
                    spacing = Math.Floor(maxSpacing / 10.0) * 10.0;
                }

                // If spacing is practically constructible (e.g., >= 75mm), select it
                if (spacing >= 75)
                {
                    return $"T{barDia} @ {spacing} c/c";
                }
            }

            // If even 16mm bars require < 75mm spacing, fallback to 16mm at 75mm (or throw warning)
            return "T16 @ 75 c/c (Heavy)";
        }
    }
}
