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

            double fck  = 25;   // M25
            double fy   = 500;  // Fe500
            double cover = 20;

            bool isSafe = false;
            int iterations = 0;
            double limitA = 0;
            double finalDeflection = 0;

            while (!isSafe && iterations < 20)
            {
                double d = slab.Thickness - cover - 5; // Assumed 10mm main bar
                if (d <= 0) { slab.Thickness += 10; iterations++; continue; }

                // ── Effective Span Calculation (IS 456 Cl 22.2) ─────────────
                double Lx_eff = EffectiveSpanCalculator.CalculateEffectiveSpan(slab.Lx, slab.SupportWidthX1, slab.SupportWidthX2, slab.IsContinuousX1, slab.IsContinuousX2, d, slab.Type);
                double Ly_eff = EffectiveSpanCalculator.CalculateEffectiveSpan(slab.Ly, slab.SupportWidthY1, slab.SupportWidthY2, slab.IsContinuousY1, slab.IsContinuousY2, d, slab.Type);

                // ── Loads ──────────────────────────────────────────────────
                double selfWeight = (slab.Thickness / 1000.0) * 25.0; // kN/m2
                double totalDL = selfWeight + slab.DeadLoad + slab.SuperimposedDeadLoad;
                double totalService = totalDL + slab.LiveLoad;
                double wFactored = totalService * 1.5;

                // ── Bending moments ────────────────────────────────────────
                double Mx_pos = 0, My_pos = 0, Mx_neg = 0, My_neg = 0;
                double ax_pos = 0, ay_pos = 0;
                
                double Lx_m = Lx_eff / 1000.0; // mm → m

                if (slab.Type == SlabType.TwoWay)
                {
                    double lyLx = Ly_eff / Lx_eff;
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
                    ax_pos = 1.0 / 12.0; 
                    double ax_neg = 1.0 / 10.0;
                    Mx_pos = ax_pos * wFactored * Lx_m * Lx_m;
                    Mx_neg = ax_neg * wFactored * Lx_m * Lx_m;
                }
                else if (slab.Type == SlabType.Cantilever)
                {
                    double ax_neg = 0.5;
                    Mx_neg = ax_neg * wFactored * Lx_m * Lx_m;
                    ax_pos = 0.5; // for deflection service moment lookup parity
                }

                double dy = d - 10; // Y layer sits above X layer
                if (dy <= 0) { slab.Thickness += 10; iterations++; continue; }

                // Calculate Required Ast (mm2/m)
                slab.Ast_x_bot = ReinforcementDesignEngine.CalculateAst(Mx_pos, d, fck, fy);
                slab.Ast_y_bot = ReinforcementDesignEngine.CalculateAst(My_pos, dy, fck, fy);
                slab.Ast_x_top = ReinforcementDesignEngine.CalculateAst(Mx_neg, d, fck, fy);
                slab.Ast_y_top = ReinforcementDesignEngine.CalculateAst(My_neg, dy, fck, fy);

                slab.Bars_x_bot = ReinforcementDesignEngine.SelectBars(slab.Ast_x_bot, d, true);
                slab.Bars_y_bot = ReinforcementDesignEngine.SelectBars(slab.Ast_y_bot, dy, true);
                slab.Bars_x_top = ReinforcementDesignEngine.SelectBars(slab.Ast_x_top, d, true);
                slab.Bars_y_top = ReinforcementDesignEngine.SelectBars(slab.Ast_y_top, dy, true);

                double maxMu = Math.Max(Mx_pos, Math.Max(My_pos, Math.Max(Mx_neg, My_neg)));
                double Mu_limit = 0.133 * fck * 1000 * d * d / 1e6; // kNm

                if (maxMu > Mu_limit)
                {
                    slab.Thickness += 10.0;
                    iterations++;
                    continue; // Flexure failed, increase thickness and try again
                }

                // Service moment (total) and permanent moment (DL only)
                double M_service = ax_pos * totalService * Lx_m * Lx_m;
                double M_perm    = ax_pos * totalDL      * Lx_m * Lx_m;

                // ── Deflection check (IS 456 Annex C) ──────────────
                var deflResult = Is456DeflectionEngine.CheckThickness(slab, M_service, M_perm, slab.Ast_x_bot, fck, fy, Lx_eff);

                if (deflResult.Status == "SAFE")
                {
                    isSafe = true;
                    limitA = deflResult.AllowableDeflection;
                    finalDeflection = deflResult.CalculatedDeflection;
                    
                    slab.DesignStatus = "SAFE";
                    slab.Notes = $"Leff={Lx_eff:F0}mm. D={slab.Thickness:F0}mm. " +
                                 $"a_tot={finalDeflection:F1}mm ≤ " +
                                 $"lim={limitA:F1}mm. " +
                                 $"Mx+={Mx_pos:F2}kNm, Mx-={Mx_neg:F2}kNm. " +
                                 $"wFact={wFactored:F2}kN/m²";
                    break;
                }
                else
                {
                    slab.Thickness += 10.0;
                    iterations++;
                }
            }

            if (!isSafe)
            {
                slab.DesignStatus = "REVISE";
                slab.Notes = $"Deflection FAIL after 20 iterations (max D tried={slab.Thickness:F0} mm). " +
                             $"Review span, loading, or grade.";
            }
        }
    }
}
