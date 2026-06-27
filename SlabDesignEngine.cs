using System;

namespace CSiNET8PluginExample1
{
    public class SlabDesignEngine
    {
        public static void DesignSlab(SlabData slab)
        {
            if (slab.Type == SlabType.FlatSlab)
            {
                slab.DesignStatus = "FLAT SLAB";
                slab.Notes = "Flat Slabs require Equivalent Frame or Direct Design Method. Table 26 deflection limits do not apply directly.";
                return;
            }

            // ── Loads ──────────────────────────────────────────────────
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

            double cover = 20;
            double dx = slab.Thickness - cover - 5; // Assumed 10mm main bar
            double dy = dx - 10;                    // Y layer sits above X layer

            // Calculate Required Ast (mm2/m)
            slab.Ast_x_bot = ReinforcementDesignEngine.CalculateAst(Mx_pos, dx, fck, fy);
            slab.Ast_y_bot = ReinforcementDesignEngine.CalculateAst(My_pos, dy, fck, fy);
            slab.Ast_x_top = ReinforcementDesignEngine.CalculateAst(Mx_neg, dx, fck, fy);
            slab.Ast_y_top = ReinforcementDesignEngine.CalculateAst(My_neg, dy, fck, fy);

            // Select Bars
            slab.Bars_x_bot = ReinforcementDesignEngine.SelectBars(slab.Ast_x_bot, dx, true);
            slab.Bars_y_bot = ReinforcementDesignEngine.SelectBars(slab.Ast_y_bot, dy, true);
            slab.Bars_x_top = ReinforcementDesignEngine.SelectBars(slab.Ast_x_top, dx, true);
            slab.Bars_y_top = ReinforcementDesignEngine.SelectBars(slab.Ast_y_top, dy, true);

            // Limit state check
            double pt = 0.3; // Approx starting percentage
            double Mu_limit = 0.133 * fck * 1000 * dx * dx / 1e6; // kNm

            if (maxMu <= Mu_limit)
            {
                slab.DesignStatus = "SAFE";
                slab.Notes = $"SW={selfWeight:F2} kN/m2, wFact={wFactored:F2} kN/m2. Flexure Safe.";
            }
            else
            {
                slab.DesignStatus = "FAIL (Flexure)";
                slab.Notes = $"Mu ({maxMu:F2} kNm) > Mu_lim ({Mu_limit:F2} kNm). Increase thickness.";
            }

            // Service moment (total) and permanent moment (DL only)
            double M_service = ax_pos * totalService * Lx_m * Lx_m;
            double M_perm    = ax_pos * totalDL      * Lx_m * Lx_m;

            // ── Iterative deflection optimisation (IS 456 Annex C) ──────────────
            var deflResult = Is456DeflectionEngine.CheckAndOptimizeThickness(
                slab, M_service, M_perm, slab.Ast_x_bot, fck, fy);

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
