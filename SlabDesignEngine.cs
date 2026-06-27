using System;

namespace CSiNET8PluginExample1
{
    public class SlabDesignEngine
    {
        public static void DesignSlab(SlabData slab)
        {
            // ── Loads ────────────────────────────────────────────────────────────
            // CORRECTION (SlabDesignEngine.cs): the previous version omitted slab
            // self-weight from wFactored.  IS 875 Part 1 / IS 456 Cl. 18.2 require
            // self-weight to be included in DL.  Self-weight = (D/1000) × 25 kN/m².
            double selfWeight  = (slab.Thickness / 1000.0) * 25.0; // kN/m²
            double totalDL     = selfWeight + slab.DeadLoad + slab.SuperimposedDeadLoad;
            double totalService = totalDL + slab.LiveLoad;

            // IS 875 Part 5 / IS 456 Cl. 18.2: ULS combination = 1.5(DL + LL)
            double wFactored = totalService * 1.5;

            // ── IS 456 Table 26 moment coefficients (simplified: fixed interior panel) ──
            // BoundaryCase 1 (all edges continuous): αx+ = 0.024, αx- = 0.032, etc.
            // For a proper implementation these should be looked up from Table 26 by
            // Ly/Lx ratio and boundary case number — see slabEngine.ts for the full table.
            double ax_pos, ay_pos, ax_neg, ay_neg;
            // Interior panel (Case 1) at Ly/Lx = 1.0
            ax_pos = 0.024; ay_pos = 0.024;
            ax_neg = 0.032; ay_neg = 0.032;

            double Lx_m = slab.Lx / 1000.0; // mm → m

            double Mx_pos = ax_pos * wFactored * Lx_m * Lx_m;
            double My_pos = ay_pos * wFactored * Lx_m * Lx_m;
            double Mx_neg = ax_neg * wFactored * Lx_m * Lx_m;
            double My_neg = ay_neg * wFactored * Lx_m * Lx_m;

            double maxMu = Math.Max(Mx_pos, Math.Max(My_pos, Math.Max(Mx_neg, My_neg)));

            double fck  = 25;   // M25
            double fy   = 500;  // Fe500

            // Preliminary Ast (approximate lever arm = 0.85d — iterative refinement
            // would use the full IS 456 Cl. 38.1 parabolic formula; acceptable here
            // as a seed value for the deflection loop).
            double d_initial = slab.Thickness - 20 - 5; // cover 20 mm + bar radius 5 mm
            double leverArm  = 0.85 * d_initial;         // ≈ 0.85d (Fe500 balanced section)
            double Ast_req   = (maxMu * 1e6) / (0.87 * fy * leverArm); // mm²

            // Service moment (total) and permanent moment (DL only)
            double M_service = ax_pos * totalService * Lx_m * Lx_m;
            double M_perm    = ax_pos * totalDL      * Lx_m * Lx_m;

            // ── Iterative deflection optimisation (IS 456 Annex C) ──────────────
            var deflResult = Is456DeflectionEngine.CheckAndOptimizeThickness(
                slab, M_service, M_perm, Ast_req, fck, fy);

            if (deflResult.Status == "SAFE")
            {
                slab.DesignStatus = "SAFE";
                slab.Thickness    = deflResult.RequiredThickness;
                slab.Notes = $"Required thickness (deflection): {deflResult.RequiredThickness:F0} mm. " +
                             $"a_total={deflResult.CalculatedDeflection:F1} mm ≤ " +
                             $"limit={deflResult.AllowableDeflection:F1} mm. " +
                             $"Moments: Mx+={Mx_pos:F2} kNm, Mx-={Mx_neg:F2} kNm. " +
                             $"SW={selfWeight:F2} kN/m², wFact={wFactored:F2} kN/m²";
            }
            else
            {
                slab.DesignStatus = "REVISE";
                slab.Notes = $"Deflection FAIL after 20 iterations (max D tried={deflResult.RequiredThickness:F0} mm). " +
                             $"a_total={deflResult.CalculatedDeflection:F1} mm > " +
                             $"limit={deflResult.AllowableDeflection:F1} mm. " +
                             $"Review span, loading, or grade.";
            }
        }
    }
}
