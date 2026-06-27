using System;

namespace CSiNET8PluginExample1
{
    public static class FlatSlabEngine
    {
        public static void DesignFlatSlab(SlabData slab)
        {
            double fck = 25;
            double fy = 500;
            double cover = 20;

            double L1 = slab.Lx; // Short span mm
            double L2 = slab.Ly; // Long span mm

            double c1 = slab.c1;
            double c2 = slab.c2;

            double D = slab.Thickness;
            double d_slab = D - cover - 10;
            double d_drop = slab.HasDrop ? (slab.DropDepth - cover - 10) : d_slab;

            // Clear span Ln (IS 456 Cl 31.4.2.2 - Ln not less than 0.65 L1)
            double Ln = L1 - c1;
            if (Ln < 0.65 * L1) Ln = 0.65 * L1;

            // Loads (kN/m2)
            double selfWeight = (D / 1000.0) * 25.0;
            // Ignore extra drop weight for simplicity of total panel load calculation
            double w_total = selfWeight + slab.DeadLoad + slab.SuperimposedDeadLoad + slab.LiveLoad;
            double wu = 1.5 * w_total;

            // L1, L2, Ln in metres for Moment calc
            double L1_m = L1 / 1000.0;
            double L2_m = L2 / 1000.0;
            double Ln_m = Ln / 1000.0;

            // Total Design Moment M0 (kN.m)
            double M0 = (wu * L2_m * Ln_m * Ln_m) / 8.0;

            // Distribution to negative (0.65) and positive (0.35)
            double M_neg = 0.65 * M0;
            double M_pos = 0.35 * M0;

            // Strip Widths (m)
            double colStripWidth = 0.5 * L2_m;
            double midStripWidth = L2_m - colStripWidth;

            // Strip Moments
            double M_neg_col = 0.75 * M_neg;
            double M_pos_col = 0.60 * M_pos;
            double M_neg_mid = 0.25 * M_neg;
            double M_pos_mid = 0.40 * M_pos;

            // Calculate Required Ast (convert Moment per strip to Moment per metre width)
            double Ast_neg_col = ReinforcementDesignEngine.CalculateAst(M_neg_col / colStripWidth, slab.HasDrop ? d_drop : d_slab, fck, fy);
            double Ast_pos_col = ReinforcementDesignEngine.CalculateAst(M_pos_col / colStripWidth, d_slab, fck, fy);
            double Ast_neg_mid = ReinforcementDesignEngine.CalculateAst(M_neg_mid / midStripWidth, d_slab, fck, fy);
            double Ast_pos_mid = ReinforcementDesignEngine.CalculateAst(M_pos_mid / midStripWidth, d_slab, fck, fy);

            // Assign to SlabData (mapping Column Strip to X and Middle Strip to Y for UI display)
            slab.Ast_x_top = Ast_neg_col; // Col Strip Neg
            slab.Ast_x_bot = Ast_pos_col; // Col Strip Pos
            slab.Ast_y_top = Ast_neg_mid; // Mid Strip Neg
            slab.Ast_y_bot = Ast_pos_mid; // Mid Strip Pos

            slab.Bars_x_top = ReinforcementDesignEngine.SelectBars(Ast_neg_col, slab.HasDrop ? d_drop : d_slab, true) + " (Col)";
            slab.Bars_x_bot = ReinforcementDesignEngine.SelectBars(Ast_pos_col, d_slab, true) + " (Col)";
            slab.Bars_y_top = ReinforcementDesignEngine.SelectBars(Ast_neg_mid, d_slab, true) + " (Mid)";
            slab.Bars_y_bot = ReinforcementDesignEngine.SelectBars(Ast_pos_mid, d_slab, true) + " (Mid)";

            // --- Punching Shear ---
            double d_punch = slab.HasDrop ? d_drop : d_slab; // mm
            double d_punch_m = d_punch / 1000.0;
            double c1_m = c1 / 1000.0;
            double c2_m = c2 / 1000.0;

            double crit_perimeter = 2 * ((c1_m + d_punch_m) + (c2_m + d_punch_m)); // m
            double area_inside = (c1_m + d_punch_m) * (c2_m + d_punch_m); // m2
            
            // Vu = total factored load on panel minus load inside critical perimeter
            double shear_force = wu * (L1_m * L2_m - area_inside); // kN

            // tau_v = Vu / (perim * d) in N/mm2
            double tau_v = (shear_force * 1000) / (crit_perimeter * 1000 * d_punch);

            // tau_c allowable
            double beta_c = Math.Min(c1, c2) / Math.Max(c1, c2);
            double ks = Math.Min(1.0, 0.5 + beta_c);
            double tau_c = ks * 0.25 * Math.Sqrt(fck);

            bool punchingSafe = tau_v <= tau_c;

            if (punchingSafe)
            {
                slab.PunchingShearStatus = $"SAFE (tv={tau_v:F2} <= tc={tau_c:F2})";
            }
            else
            {
                slab.PunchingShearStatus = $"FAIL (tv={tau_v:F2} > tc={tau_c:F2})";
            }

            // Deflection calculation (Using column strip bottom steel as governing)
            double w_service = selfWeight + slab.DeadLoad + slab.SuperimposedDeadLoad + slab.LiveLoad;
            double w_perm = selfWeight + slab.DeadLoad + slab.SuperimposedDeadLoad;
            
            double M0_service = (w_service * L2_m * Ln_m * Ln_m) / 8.0;
            double M0_perm = (w_perm * L2_m * Ln_m * Ln_m) / 8.0;

            double M_pos_col_service_per_m = (0.35 * 0.60 * M0_service) / colStripWidth;
            double M_pos_col_perm_per_m = (0.35 * 0.60 * M0_perm) / colStripWidth;

            var deflResult = Is456DeflectionEngine.CheckAndOptimizeThickness(
                slab, M_pos_col_service_per_m, M_pos_col_perm_per_m, Ast_pos_col, fck, fy);

            if (deflResult.Status == "SAFE" && punchingSafe)
            {
                slab.DesignStatus = "SAFE";
                slab.Notes = $"Flat Slab DDM. {slab.PunchingShearStatus}. Defl {deflResult.CalculatedDeflection:F1}mm <= {deflResult.AllowableDeflection:F1}mm";
            }
            else
            {
                slab.DesignStatus = punchingSafe ? "FAIL (Deflection)" : "FAIL (Punching)";
                slab.Notes = $"Flat Slab DDM. {slab.PunchingShearStatus}. Defl {deflResult.CalculatedDeflection:F1}mm <= {deflResult.AllowableDeflection:F1}mm";
            }
        }
    }
}
