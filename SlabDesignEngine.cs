using System;

namespace CSiNET8PluginExample1
{
    public class SlabDesignEngine
    {
        public static void DesignSlab(SlabData slab)
        {

            double selfWeight = (slab.Thickness / 1000.0) * 25.0; // kN/m2
            double totalDL = selfWeight + slab.DeadLoad + slab.SuperimposedDeadLoad;
            double totalService = totalDL + slab.LiveLoad;

            // IS 875 Part 5 / IS 456 Cl. 18.2: ULS combination = 1.5(DL + LL)
            double wFactored = totalService * 1.5;

            // ── Bending moments ──────────────────────────────────────────────────
            double Mx_pos = 0, My_pos = 0, Mx_neg = 0, My_neg = 0;
            double ax_pos = 0, ay_pos = 0;
            
            double Lx_m = slab.Lx / 1000.0; // mm → m

            if (slab.Type == SlabType.TwoWay)
            {
                double lyLx = slab.Ly / slab.Lx;
                ax_pos = Is456Table26.GetAlphaXPos(slab.BoundaryCase, lyLx);
                double ax_neg = Is456Table26.GetAlphaXNeg(slab.BoundaryCase, lyLx);
                ay_pos = Is456Table26.GetAlphaYPos(slab.BoundaryCase);
                double ay_neg = Is456Table26.GetAlphaYNeg(slab.BoundaryCase);

                Mx_pos = ax_pos * wFactored * Lx_m * Lx_m;
                Mx_neg = ax_neg * wFactored * Lx_m * Lx_m;
                My_pos = ay_pos * wFactored * Lx_m * Lx_m;
                My_neg = ay_neg * wFactored * Lx_m * Lx_m;
            }
            else if (slab.Type == SlabType.OneWay)
            {
                // Simplified one-way coefficients (assuming continuous interior)
                ax_pos = 1.0 / 12.0; 
                double ax_neg = 1.0 / 10.0;
                Mx_pos = ax_pos * wFactored * Lx_m * Lx_m;
                Mx_neg = ax_neg * wFactored * Lx_m * Lx_m;
            }
            else if (slab.Type == SlabType.Cantilever)
            {
                // Cantilever moment wL^2 / 2
                double ax_neg = 0.5;
                Mx_neg = ax_neg * wFactored * Lx_m * Lx_m;
                ax_pos = 0.5; // for deflection service moment lookup parity
            }

            // Moments calculated above

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
