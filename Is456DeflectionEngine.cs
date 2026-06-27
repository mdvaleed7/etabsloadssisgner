using System;

namespace CSiNET8PluginExample1
{
    /// <summary>
    /// IS 456:2000 Annex C deflection engine.
    ///
    /// CORRECTIONS applied vs. the original version:
    ///
    /// 1. Self-weight omission (now fixed in SlabDesignEngine — engine receives
    ///    the correct M_service and M_perm).
    ///
    /// 2. Long-term deflection formula (CRITICAL):
    ///    The original code used a crude multiplier (a_LT = ai × 1.5 → a_total = 2.5 ai).
    ///    IS 456 Annex C requires:
    ///      (a) Creep deflection: a_cc = a(perm,Ie2) − a(perm,Ie1)
    ///          where Ie1 uses Ec and Ie2 uses Ec / (1 + θ).  IS 456 Cl. 6.2.5.1:
    ///          θ = creep coefficient = 1.6 (28-day loading per Cl. 6.2.5.1, default).
    ///      (b) Shrinkage deflection: a_s = k3 × ε_cs × L² / D
    ///          IS 456 Annex C Eq. (C-5): k3 = 0.5 (cantilever), 0.125 (SS), 0.083 (cont.)
    ///          ε_cs = unit shrinkage strain = 0.0003 (IS 456 Cl. 6.2.4.1 — for w/c ≤ 0.50)
    ///    This implementation now uses proper Annex C creep and shrinkage formulations.
    ///
    /// 3. Allowable deflection (IS 456 Cl. 23.2):
    ///    Two limits must both be satisfied:
    ///      Limit A: total deflection ≤ L/250
    ///      Limit B: deflection after partition/finish ≤ L/350 or 20 mm (lesser)
    ///    The check in CheckAndOptimizeThickness now enforces both.
    ///
    /// 4. Alpha coefficient for continuous slab (BoundaryCase 1):
    ///    Short-term deflection formula: ai = α × M_s × L² / (Ec × Ieff)
    ///    For an interior panel the mid-span bending moment governs short-term
    ///    deflection; using the positive BM (M_pos = 0.024 w Lx²) and the
    ///    corresponding coefficient:
    ///      α = 5/48 (simply supported — upper bound)
    ///      α = 1/96 (both ends fully fixed — theoretically from wL⁴/384EI
    ///                expressed as M_pos × L² / (EI); M_pos = wL²/24 → α = 1/24 × 1/4
    ///                but midspan moment used as M_pos above is wL²/12 for interior
    ///                so α = 1/48 gives wL⁴/384EI ≡ (wL²/12)×L²/(48EI).)
    ///    The code below uses α = 1/48 for the continuous case (BoundaryCase 1)
    ///    which is the correct coefficient when M_service = positive service moment.
    ///    The previous value of 1/16 would have overestimated deflection by 3×.
    ///
    /// 5. fck and fy are now passed explicitly rather than hardcoded.
    /// </summary>
    public class Is456DeflectionEngine
    {
        // IS 456 Cl. 6.2.3.1: Ec = 5000 √fck (MPa)
        public static double GetEc(double fck) => 5000.0 * Math.Sqrt(fck);

        // IS 456 Cl. 6.2.5.1: creep coefficient θ for age of loading ≥ 28 days
        private const double CREEP_COEFF = 1.6;

        // IS 456 Cl. 6.2.4.1: design ultimate shrinkage strain
        private const double EPSILON_CS = 0.0003;

        // ── Alpha coefficient for mid-span deflection under UDL ────────────────
        // Expressed as: δ_mid = α × M_midspan × L² / (EI)
        // Simply supported      α = 5/48   (δ = 5wL⁴/384EI; M_ss = wL²/8)
        // One-end continuous    α ≈ 1/48   (conservative mid-range approximation)
        // Both-ends continuous  α = 1/48   (δ = wL⁴/384EI; M_mid = wL²/12 → α=1/48)
        // Cantilever            α = 1/4    (δ = wL⁴/8EI;   M_root = wL²/2  → α=1/4)
        private static double GetAlpha(SlabData slab)
        {
            if (slab.Type == SlabType.Cantilever) return 1.0 / 4.0;

            switch (slab.BoundaryCase)
            {
                case 1:
                    // CORRECTION: was 1/16 (wrong). Correct value for both-ends-continuous
                    // using positive mid-span moment M_pos = α_x × wFactored × Lx² is 1/48.
                    // (Derivation: δ = wL⁴/384EI; M_mid = wL²/12; α = δ/(M_mid·L²/EI) = 1/48.)
                    return 1.0 / 48.0;
                case 2:
                case 3:
                case 4:
                    return 1.0 / 48.0; // one or two edges discontinuous: conservative 1/48
                default:
                    return 5.0 / 48.0; // simply supported (Cases 5, 7, 8, 9)
            }
        }

        // ── k3 coefficient for shrinkage deflection (IS 456 Annex C, Eq. C-5) ──
        private static double GetK3(SlabData slab)
        {
            if (slab.Type == SlabType.Cantilever) return 0.5;
            if (slab.BoundaryCase == 1) return 0.083;  // fully continuous
            if (slab.BoundaryCase >= 5) return 0.125;  // simply supported
            return 0.083;                               // intermediate: use continuous
        }

        /// <summary>
        /// Single-pass IS 456 Annex C deflection check.
        /// Returns (Status, CalculatedDeflection, AllowableDeflection).
        /// </summary>
        public static (string Status, double CalculatedDeflection, double AllowableDeflection)
            CheckThickness(SlabData slab, double M_service_kNm, double M_perm_kNm,
                           double Ast_mm2, double fck, double fy, double L_eff)
        {
            double b   = 1000.0; // mm — 1 m unit strip
            double Ec  = GetEc(fck);
            double Es  = 200000.0; // MPa
            double m   = Es / Ec;

            // Use passed effective span
            double L = L_eff;

            // IS 456 Cl. 23.2: two separate allowable deflection limits
            double limitA   = L / 250.0;                  // total deflection
            double limitB   = Math.Min(L / 350.0, 20.0); // post-construction limit

            double D = slab.Thickness;
            double alpha  = GetAlpha(slab);
            double k3     = GetK3(slab);
            double Ec_eff = Ec / (1.0 + CREEP_COEFF); // long-term effective modulus

            // ── Section geometry ──────────────────────────────────────────
            double d = D - 20.0 - 5.0; // effective depth
            if (d <= 0) return ("FAIL", 9999, limitA);

            // ── Gross inertia ─────────────────────────────────────────────
            double Igr = b * D * D * D / 12.0; // mm⁴

            // ── Cracking moment (IS 456 Cl. 6.2.2) ───────────────────────
            double fcr = 0.7 * Math.Sqrt(fck); // N/mm²
            double yt  = D / 2.0;               
            double Mcr = (fcr * Igr / yt) / 1e6; // kN·m

            // ── Cracked neutral axis depth (IS 456 Annex C) ───────────────
            double mAst = m * Ast_mm2;
            double x    = (-mAst + Math.Sqrt(mAst * mAst + 2.0 * b * mAst * d)) / b; // mm

            // ── Cracked inertia (IS 456 Annex C) ─────────────────────────
            double Icr = b * x * x * x / 3.0 + m * Ast_mm2 * (d - x) * (d - x); // mm⁴

            // ── Effective inertia ─────────────────────────────────────────
            double Ms = Math.Abs(M_service_kNm);
            double Ieff;
            if (Ms < 0.001 || Mcr >= Ms)
            {
                Ieff = Igr;
            }
            else
            {
                double z      = d - x / 3.0;
                double denom  = 1.2 - (Mcr / Ms) * (z / d) * (1.0 - x / d);
                Ieff = (denom > 0) ? Icr / denom : Igr;
                Ieff = Math.Max(Icr, Math.Min(Igr, Ieff));
            }

            // ── Effective inertia for permanent load ──────────────────────
            double Mp = Math.Abs(M_perm_kNm);
            double Ieff_perm;
            if (Mp < 0.001 || Mcr >= Mp)
            {
                Ieff_perm = Igr;
            }
            else
            {
                double z_p      = d - x / 3.0;
                double denom_p  = 1.2 - (Mcr / Mp) * (z_p / d) * (1.0 - x / d);
                Ieff_perm = (denom_p > 0) ? Icr / denom_p : Igr;
                Ieff_perm = Math.Max(Icr, Math.Min(Igr, Ieff_perm));
            }

            // ── (a) Short-term deflection ─────────────────────────────────
            double ai = alpha * Ms * 1e6 * L * L / (Ec * Ieff);

            // ── (b) Creep deflection ──────────────────────────────────────
            double a_perm_elastic = alpha * Mp * 1e6 * L * L / (Ec       * Ieff_perm);
            double a_perm_LT      = alpha * Mp * 1e6 * L * L / (Ec_eff  * Ieff_perm);
            double a_cc = a_perm_LT - a_perm_elastic; 

            // ── (c) Shrinkage deflection ──────────────────────────────────
            double a_s = k3 * EPSILON_CS * L * L / D;

            // ── Total long-term deflection ─────────────────────────────────
            double a_total = ai + a_cc + a_s;

            // ── Limit check ────────────────────────────────────────────────
            double a_post = a_cc + a_s;
            bool passA = a_total <= limitA;
            bool passB = a_post  <= limitB;

            return (passA && passB)
                ? ("SAFE", a_total, limitA)
                : ("FAIL", a_total, limitA);
        }
    }
}
