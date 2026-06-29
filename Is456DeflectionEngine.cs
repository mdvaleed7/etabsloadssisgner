using System;

namespace AdvatechEtabsPlugin
{
    /// <summary>
    /// IS 456:2000 Annex C deflection engine.
    ///
    /// PATCH NOTES (v2):
    ///  1. d is now derived from slab.Cover and slab.BarDiaMain instead of
    ///     hard-coded 20 + 5.  Keeps it in sync with the design engine.
    ///  2. Shrinkage deflection now uses the proper IS 456 Annex C Eq. C-3:
    ///         a_cs = k3 · k4 · ε_cs · L² / D
    ///     where k4 depends on (pt − pc):
    ///         k4 = 0.72 (pt − pc)/√pt        for 0.25 ≤ pt − pc < 1.0
    ///         k4 = 0.65 (pt − pc)/√pt        for pt − pc ≥ 1.0
    ///         k4 ≤ 1.0
    ///     The previous version folded k4 into k3 by constant, which over-
    ///     estimated shrinkage deflection for lightly reinforced sections.
    ///  3. Creep deflection a_cc = a(perm,Ec_eff) − a(perm,Ec) is unchanged
    ///     (Annex C Eq. C-2 with θ = 1.6).
    ///  4. Two allowable limits (Cl. 23.2) — L/250 total and min(L/350, 20 mm)
    ///     post-construction — both must be satisfied.
    /// </summary>
    public class Is456DeflectionEngine
    {
        /// <summary>IS 456 Cl. 6.2.3.1: Ec = 5000 √fck (MPa).</summary>
        public static double GetEc(double fck) => 5000.0 * Math.Sqrt(fck);

        /// <summary>IS 456 Cl. 6.2.5.1: creep coefficient for ≥28-day loading.</summary>
        private const double CREEP_COEFF = 1.6;

        /// <summary>IS 456 Cl. 6.2.4.1: design ultimate shrinkage strain (w/c ≤ 0.5).</summary>
        private const double EPSILON_CS = 0.0003;

        // Mid-span deflection coefficient α s.t.  δ_mid = α · M_mid · L²/(E·I).
        // Derivation in the original file is correct.
        private static double GetAlpha(SlabData slab)
        {
            if (slab.Type == SlabType.Cantilever) return 1.0 / 4.0;
            switch (slab.BoundaryCase)
            {
                case 1:
                case 2:
                case 3:
                case 4: return 1.0 / 48.0;   // continuous / partially continuous
                default: return 5.0 / 48.0;  // simply supported (Cases 5–9)
            }
        }

        // k3 coefficient for shrinkage deflection — IS 456 Annex C Eq. C-3
        private static double GetK3(SlabData slab)
        {
            if (slab.Type == SlabType.Cantilever) return 0.5;
            if (slab.BoundaryCase == 1)           return 0.083; // fully continuous
            if (slab.BoundaryCase >= 5)           return 0.125; // simply supported
            return 0.086;                                        // one end continuous
        }

        // PATCH: separate k4 from k3.  pt and pc are tension and compression
        // reinforcement percentages (Ast / (b·d) × 100 etc.).
        private static double GetK4(double pt, double pc)
        {
            double diff = pt - pc;
            if (diff <= 0 || pt <= 0) return 0;     // no tension steel → no shrinkage curvature
            double k4;
            if (diff < 0.25)      k4 = 0.72 * diff / Math.Sqrt(pt);  // extrapolation safety
            else if (diff < 1.0)  k4 = 0.72 * diff / Math.Sqrt(pt);
            else                  k4 = 0.65 * diff / Math.Sqrt(pt);
            return Math.Min(1.0, k4);
        }

        /// <summary>
        /// Single-pass IS 456 Annex C deflection check.
        /// </summary>
        /// <param name="slab">Slab geometry & user-set cover / bar Ø / Fy.</param>
        /// <param name="M_service_kNm">Service mid-span moment per metre (kN·m/m).</param>
        /// <param name="M_perm_kNm">Permanent (DL only) mid-span moment per metre (kN·m/m).</param>
        /// <param name="Ast_mm2">Tension steel area per metre (mm²/m).</param>
        /// <param name="fck">Concrete grade (N/mm²).</param>
        /// <param name="fy">Steel grade — currently unused inside (kept for API parity).</param>
        /// <param name="L_eff">Effective span (mm).</param>
        public static (string Status, double CalculatedDeflection, double AllowableDeflection)
            CheckThickness(SlabData slab, double M_service_kNm, double M_perm_kNm,
                           double Ast_mm2, double fck, double fy, double L_eff)
        {
            double b  = 1000.0;            // mm — 1 m strip
            double Ec = GetEc(fck);
            double Es = 200000.0;          // MPa
            double m  = Es / Ec;
            double L  = L_eff;

            // Allowables (IS 456 Cl. 23.2)
            double limitA = L / 250.0;
            double limitB = Math.Min(L / 350.0, 20.0);

            double D     = slab.Thickness;
            double alpha = GetAlpha(slab);
            double k3    = GetK3(slab);

            // PATCH: effective depth uses slab.Cover and slab.BarDiaMain
            double d = D - slab.Cover - slab.BarDiaMain / 2.0;
            if (d <= 0) return ("FAIL", 9999, limitA);

            double Ec_eff = Ec / (1.0 + CREEP_COEFF);

            // ── Gross & cracked section properties ────────────────────────
            double Igr  = b * D * D * D / 12.0;                       // mm⁴
            double fcr  = 0.7 * Math.Sqrt(fck);                       // N/mm²
            double yt   = D / 2.0;
            double Mcr  = (fcr * Igr / yt) / 1e6;                     // kN·m

            double mAst = m * Ast_mm2;
            double x    = (-mAst + Math.Sqrt(mAst * mAst + 2.0 * b * mAst * d)) / b;
            double Icr  = b * x * x * x / 3.0 + m * Ast_mm2 * (d - x) * (d - x);

            // ── Effective inertia under service moment ───────────────────
            double Ms   = Math.Abs(M_service_kNm);
            double Ieff = (Ms < 0.001 || Mcr >= Ms) ? Igr : EffectiveI(Igr, Icr, Mcr, Ms, x, d);

            // ── Effective inertia under permanent moment ─────────────────
            double Mp        = Math.Abs(M_perm_kNm);
            double Ieff_perm = (Mp < 0.001 || Mcr >= Mp) ? Igr : EffectiveI(Igr, Icr, Mcr, Mp, x, d);

            // ── (a) Short-term elastic deflection under total service load ──
            double ai = alpha * Ms * 1e6 * L * L / (Ec * Ieff);

            // ── (b) Creep deflection — Annex C Eq. C-2 ───────────────────
            double a_perm_elastic = alpha * Mp * 1e6 * L * L / (Ec     * Ieff_perm);
            double a_perm_LT      = alpha * Mp * 1e6 * L * L / (Ec_eff * Ieff_perm);
            double a_cc           = a_perm_LT - a_perm_elastic;

            // ── (c) Shrinkage deflection — Annex C Eq. C-3 with k4 ───────
            double pt = 100.0 * Ast_mm2 / (b * d);   // %
            double pc = 0.0;                         // no compression steel modelled here
            double k4 = GetK4(pt, pc);
            double a_s = k3 * k4 * EPSILON_CS * L * L / D;

            // ── Total long-term deflection ───────────────────────────────
            double a_total = ai + a_cc + a_s;
            double a_post  = a_cc + a_s;

            bool passA = a_total <= limitA;
            bool passB = a_post  <= limitB;

            return (passA && passB)
                ? ("SAFE", a_total, limitA)
                : ("FAIL", a_total, limitA);
        }

        private static double EffectiveI(double Igr, double Icr, double Mcr, double M,
                                         double x, double d)
        {
            double z     = d - x / 3.0;
            double denom = 1.2 - (Mcr / M) * (z / d) * (1.0 - x / d);
            double Ieff  = (denom > 0) ? Icr / denom : Igr;
            return Math.Max(Icr, Math.Min(Igr, Ieff));
        }
    }
}
