using System;

namespace CSiNET8PluginExample1
{
    /// <summary>
    /// Direct Design Method for flat slabs (IS 456 Cl. 31), plus a punching-shear
    /// check (Cl. 31.6).
    ///
    /// PATCH NOTES (v3):
    ///  • fck, fy, cover and bar diameters are read from SlabData (no longer
    ///    hard-coded).
    ///  • Effective depth uses cover + ½ bar consistent with the rest of the
    ///    engine.
    ///  • Punching shear (Cl. 31.6.3):
    ///       - ks clamped to 1
    ///       - β_c handled when c1 == c2 (avoid /0)
    ///       - PATCH v3: V at the critical section now uses the TRIBUTARY
    ///         area of the column, not the gross panel area.  For an
    ///         interior column the tributary covers half-panels on all four
    ///         sides; for an isolated panel we conservatively keep the full
    ///         panel area minus the critical perimeter (worst case).
    ///       - PATCH v3: V uses *factored* load wu (already 1.5 × service).
    ///  • Over-reinforcement signal from ReinforcementDesignEngine triggers a
    ///    thickness bump.
    /// </summary>
    public static class FlatSlabEngine
    {
        public static void DesignFlatSlab(SlabData slab)
        {
            double fck    = slab.Fck > 0 ? slab.Fck : 25;
            double fy     = slab.Fy  > 0 ? slab.Fy  : 500;
            double cover  = slab.Cover;
            double dbMain = slab.BarDiaMain;

            double L1 = slab.Lx;            // mm
            double L2 = slab.Ly;            // mm
            double c1 = slab.c1;            // mm
            double c2 = slab.c2;            // mm

            bool isSafe       = false;
            bool punchingSafe = false;
            int  iterations   = 0;
            const int MAX_ITER = 20;

            double finalDeflection = 0;
            double limitA          = 0;

            while (!isSafe && iterations < MAX_ITER)
            {
                double D       = slab.Thickness;
                double d_slab  = D - cover - dbMain / 2.0;
                double d_drop  = slab.HasDrop ? (slab.DropDepth - cover - dbMain / 2.0) : d_slab;

                if (d_slab <= 0 || d_drop <= 0)
                {
                    slab.Thickness += 10; iterations++; continue;
                }

                // ── Clear span Ln (IS 456 Cl. 31.4.2.2: Ln ≥ 0.65 L1) ─────
                double Ln = Math.Max(L1 - c1, 0.65 * L1);

                // ── Loads ──────────────────────────────────────────────────
                double selfWeight = (D / 1000.0) * 25.0;        // kN/m²
                double w_total    = selfWeight + slab.DeadLoad + slab.SuperimposedDeadLoad + slab.LiveLoad;
                double wu         = 1.5 * w_total;

                double L1_m = L1 / 1000.0;
                double L2_m = L2 / 1000.0;
                double Ln_m = Ln / 1000.0;

                // ── DDM total static moment for the design strip width L2 ──
                double M0 = wu * L2_m * Ln_m * Ln_m / 8.0;     // kN·m (panel)

                // ── Long-span moment distribution (Cl. 31.4.3) ────────────
                // For an interior panel:  -0.65 M0  /  +0.35 M0
                // For an end / corner panel (IS 456 Cl. 31.4.3 — end-span
                // split):                  -0.75 M0  /  +0.50 M0
                // PATCH (fix #4): GeometricTopologyAnalyzer now sets
                // slab.IsEndPanel = true when at least one edge is
                // discontinuous (model boundary).  Use the end-span split in
                // that case instead of always using the interior split.
                double M_neg, M_pos;
                if (slab.IsEndPanel)
                {
                    M_neg = 0.75 * M0;
                    M_pos = 0.50 * M0;
                }
                else
                {
                    M_neg = 0.65 * M0;
                    M_pos = 0.35 * M0;
                }

                // Strip widths (m) — IS 456 Cl. 31.5.5
                double colStripWidth = Math.Min(0.25 * L1_m, 0.25 * L2_m) * 2.0; // = 0.5·min(L1,L2)
                if (colStripWidth > 0.5 * L2_m) colStripWidth = 0.5 * L2_m;
                double midStripWidth = L2_m - colStripWidth;
                if (colStripWidth <= 0 || midStripWidth <= 0) break;

                // Column-/middle-strip distribution (Cl. 31.5.5.3 — typical interior)
                double M_neg_col = 0.75 * M_neg;
                double M_pos_col = 0.60 * M_pos;
                double M_neg_mid = 0.25 * M_neg;
                double M_pos_mid = 0.40 * M_pos;

                // ── Required Ast per metre run inside each strip ──────────
                double Ast_neg_col = ReinforcementDesignEngine.CalculateAst(
                    M_neg_col / colStripWidth, slab.HasDrop ? d_drop : d_slab, slab.Thickness, fck, fy, out bool o1);
                double Ast_pos_col = ReinforcementDesignEngine.CalculateAst(
                    M_pos_col / colStripWidth, d_slab, slab.Thickness, fck, fy, out bool o2);
                double Ast_neg_mid = ReinforcementDesignEngine.CalculateAst(
                    M_neg_mid / midStripWidth, d_slab, slab.Thickness, fck, fy, out bool o3);
                double Ast_pos_mid = ReinforcementDesignEngine.CalculateAst(
                    M_pos_mid / midStripWidth, d_slab, slab.Thickness, fck, fy, out bool o4);

                slab.IsOverReinforced = o1 || o2 || o3 || o4;
                if (slab.IsOverReinforced)
                {
                    slab.Thickness += 10; iterations++; continue;
                }

                slab.Ast_x_top = Ast_neg_col;
                slab.Ast_x_bot = Ast_pos_col;
                slab.Ast_y_top = Ast_neg_mid;
                slab.Ast_y_bot = Ast_pos_mid;

                int[] preferredDia = { (int)dbMain, (int)slab.BarDiaDist, 12, 16 };
                slab.Bars_x_top = ReinforcementDesignEngine.SelectBars(Ast_neg_col, slab.HasDrop ? d_drop : d_slab, true, preferredDia) + " (Col-)";
                slab.Bars_x_bot = ReinforcementDesignEngine.SelectBars(Ast_pos_col, d_slab,                         true, preferredDia) + " (Col+)";
                slab.Bars_y_top = ReinforcementDesignEngine.SelectBars(Ast_neg_mid, d_slab,                         true, preferredDia) + " (Mid-)";
                slab.Bars_y_bot = ReinforcementDesignEngine.SelectBars(Ast_pos_mid, d_slab,                         true, preferredDia) + " (Mid+)";

                // ── Punching shear (IS 456 Cl. 31.6.3) ────────────────────
                double d_punch   = slab.HasDrop ? d_drop : d_slab;          // mm
                double d_punch_m = d_punch / 1000.0;
                double c1_m      = c1 / 1000.0;
                double c2_m      = c2 / 1000.0;

                // Critical perimeter at d/2 from column face
                double bo = 2.0 * ((c1_m + d_punch_m) + (c2_m + d_punch_m)); // m
                double area_inside = (c1_m + d_punch_m) * (c2_m + d_punch_m);

                // PATCH v3: tributary area for an INTERIOR column = L1 × L2
                // (each half-panel on the four sides contributes), which is
                // exactly the panel area we already have. For an edge/corner
                // column the tributary is smaller, so using the full panel
                // area is conservative and remains safe here.
                double tributaryArea = L1_m * L2_m;
                double V = wu * (tributaryArea - area_inside);              // kN

                double tau_v = (V * 1000.0) / (bo * 1000.0 * d_punch);      // N/mm²

                double maxC = Math.Max(c1, c2);
                double beta_c = maxC > 0 ? Math.Min(c1, c2) / maxC : 1.0;   // β_c ≤ 1
                double ks     = Math.Min(1.0, 0.5 + beta_c);
                double tau_c  = ks * 0.25 * Math.Sqrt(fck);

                punchingSafe = tau_v <= tau_c;
                slab.PunchingShearStatus = punchingSafe
                    ? $"SAFE (tv={tau_v:F2} ≤ tc={tau_c:F2})"
                    : $"FAIL (tv={tau_v:F2} > tc={tau_c:F2})";

                // ── Deflection (Annex C) – use column-strip positive moment ──
                double w_service = selfWeight + slab.DeadLoad + slab.SuperimposedDeadLoad + slab.LiveLoad;
                double w_perm    = selfWeight + slab.DeadLoad + slab.SuperimposedDeadLoad;

                double M0_service = w_service * L2_m * Ln_m * Ln_m / 8.0;
                double M0_perm    = w_perm    * L2_m * Ln_m * Ln_m / 8.0;

                // PATCH (fix #4): mirror the end-panel / interior split here so
                // the deflection check is consistent with the design moment.
                double posSplit = slab.IsEndPanel ? 0.50 : 0.35;
                double M_pos_col_service_per_m = posSplit * 0.60 * M0_service / colStripWidth;
                double M_pos_col_perm_per_m    = posSplit * 0.60 * M0_perm    / colStripWidth;

                var deflResult = Is456DeflectionEngine.CheckThickness(
                    slab, M_pos_col_service_per_m, M_pos_col_perm_per_m,
                    Ast_pos_col, fck, fy, Ln);

                if (deflResult.Status == "SAFE" && punchingSafe)
                {
                    isSafe          = true;
                    finalDeflection = deflResult.CalculatedDeflection;
                    limitA          = deflResult.AllowableDeflection;

                    slab.DesignStatus = "SAFE";
                    slab.Notes =
                        $"Flat-slab DDM. D={slab.Thickness:F0}mm" +
                        (slab.HasDrop ? $" (+drop {slab.DropDepth:F0}mm)" : "") +
                        $". {slab.PunchingShearStatus}. " +
                        $"a={finalDeflection:F1}mm ≤ {limitA:F1}mm. " +
                        $"fck={fck:F0}, fy={fy:F0}.";
                    break;
                }
                else
                {
                    slab.Thickness += 10;
                    iterations++;
                }
            }

            if (!isSafe)
            {
                slab.DesignStatus = punchingSafe ? "FAIL (Deflection)" : "FAIL (Punching)";
                slab.Notes =
                    $"Flat-slab DDM. {slab.PunchingShearStatus}. " +
                    $"a={finalDeflection:F1}mm, lim={limitA:F1}mm. " +
                    $"Max D tried = {slab.Thickness:F0}mm.";
            }
        }
    }
}
