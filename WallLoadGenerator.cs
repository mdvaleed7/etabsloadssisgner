using System;
using System.Collections.Generic;
using System.Text;

namespace AdvatechEtabsPlugin
{
    /// <summary>Masonry / partition wall material with its IS 875 Part 1 unit weight.</summary>
    public enum WallMaterial
    {
        BrickMasonry,        // 19.0 kN/m³
        AAC_Block,           //  6.0 kN/m³ (autoclaved aerated concrete)
        ConcreteBlock_Solid, // 20.0 kN/m³
        ConcreteBlock_Hollow,// 14.0 kN/m³
        StoneMasonry,        // 24.0 kN/m³
        Drywall_Gypsum,      //  variable, treated as areal load
        GlassPartition,      //  areal load
        Custom
    }

    /// <summary>Inputs for one run of wall-load generation.</summary>
    public class WallLoadInput
    {
        public WallMaterial Material { get; set; } = WallMaterial.BrickMasonry;

        /// <summary>Custom masonry density (kN/m³) used when Material == Custom.</summary>
        public double CustomDensity_kNm3 { get; set; } = 19.0;

        public double Thickness_mm { get; set; } = 230;   // wall thickness (excl. plaster)
        public double StoreyHeight_mm { get; set; } = 3000;// floor-to-floor
        public double BeamDepth_mm { get; set; } = 450;    // deducted to get clear wall height

        /// <summary>Opening (door/window) percentage of the wall elevation (0–100).</summary>
        public double OpeningPercent { get; set; } = 0;

        /// <summary>Plaster thickness PER FACE (mm).  Applied to both faces.</summary>
        public double PlasterThickness_mm { get; set; } = 12;
        public double PlasterDensity_kNm3 { get; set; } = 20.0;

        /// <summary>Additional finish areal load on the wall faces (kN/m²), e.g. cladding/tiles.</summary>
        public double FinishLoad_kNm2 { get; set; } = 0;

        /// <summary>True for a parapet (height = parapet height, no beam deduction, often solid).</summary>
        public bool IsParapet { get; set; } = false;
        public double ParapetHeight_mm { get; set; } = 1000;
    }

    /// <summary>Computed wall loads for assignment.</summary>
    public class WallLoadResult
    {
        public double Density_kNm3;
        public double ClearHeight_m;
        public double MasonryLineLoad_kNm;   // wall body, after opening reduction
        public double PlasterLineLoad_kNm;
        public double FinishLineLoad_kNm;
        public double TotalLineLoad_kNm;     // → SetLoadDistributed on the beam
        public string Breakdown = "";
    }

    /// <summary>
    /// FEATURE 6 — Wall Load Generator.
    ///
    /// ───────────────────────────────────────────────────────────────────────
    /// ENGINEERING LOGIC (IS 875 Part 1)
    /// ───────────────────────────────────────────────────────────────────────
    /// A masonry wall sitting on a beam delivers a uniformly distributed line
    /// load (kN/m) equal to its self-weight per metre run:
    ///
    ///   clear height  H = storey height − beam depth         (parapet: H = parapet height)
    ///   masonry/m     w_m = t (m) × H (m) × γ (kN/m³)
    ///   plaster/m     w_p = 2 faces × t_pl (m) × H (m) × γ_pl
    ///   finish/m      w_f = finish (kN/m²) × H (m)
    ///   opening factor (1 − opening% / 100) applied to the masonry + plaster
    ///                  (openings remove wall body & plaster, not the lintel —
    ///                   a conservative engineer may set opening% = 0).
    ///
    ///   total line load = (w_m + w_p) · (1 − op) + w_f
    ///
    /// The result is applied as a downward (Gravity) distributed load on the
    /// supporting beam(s); auto beam-detection beneath the wall is handled by
    /// the assignment service, which also supports inclined beams by using the
    /// projected length implicitly (ETABS distributes per unit member length).
    /// </summary>
    public static class WallLoadGenerator
    {
        public static double Density(WallLoadInput w) => w.Material switch
        {
            WallMaterial.BrickMasonry         => 19.0,
            WallMaterial.AAC_Block            => 6.0,
            WallMaterial.ConcreteBlock_Solid  => 20.0,
            WallMaterial.ConcreteBlock_Hollow => 14.0,
            WallMaterial.StoneMasonry         => 24.0,
            WallMaterial.Drywall_Gypsum       => 0.0,   // treated as areal finish
            WallMaterial.GlassPartition       => 0.0,   // treated as areal finish
            WallMaterial.Custom               => w.CustomDensity_kNm3,
            _ => 19.0
        };

        public static WallLoadResult Compute(WallLoadInput w)
        {
            var r = new WallLoadResult();
            double gamma = Density(w);
            r.Density_kNm3 = gamma;

            double H_mm = w.IsParapet
                ? w.ParapetHeight_mm
                : Math.Max(0, w.StoreyHeight_mm - w.BeamDepth_mm);
            double H = H_mm / 1000.0;
            r.ClearHeight_m = H;

            double t  = w.Thickness_mm / 1000.0;
            double op = Math.Max(0, Math.Min(100, w.OpeningPercent)) / 100.0;
            if (w.IsParapet) op = 0; // parapets are solid

            // Masonry body
            double wm = t * H * gamma;

            // Plaster, both faces
            double wp = 2.0 * (w.PlasterThickness_mm / 1000.0) * H * w.PlasterDensity_kNm3;

            // Finish areal load over the wall elevation
            double wf = w.FinishLoad_kNm2 * H;

            r.MasonryLineLoad_kNm = wm * (1 - op);
            r.PlasterLineLoad_kNm = wp * (1 - op);
            r.FinishLineLoad_kNm  = wf;            // openings keep frame finishes conservative
            r.TotalLineLoad_kNm   = r.MasonryLineLoad_kNm + r.PlasterLineLoad_kNm + r.FinishLineLoad_kNm;

            var sb = new StringBuilder();
            sb.AppendLine($"Wall load ({w.Material}, γ = {gamma:F1} kN/m³):");
            sb.AppendLine($"  Clear height H = {(w.IsParapet ? "parapet " : "")}{H:F3} m " +
                          (w.IsParapet ? "" : $"(storey {w.StoreyHeight_mm:F0} − beam {w.BeamDepth_mm:F0} mm)"));
            sb.AppendLine($"  Masonry   : {w.Thickness_mm:F0} mm × {H:F2} m × {gamma:F1} = {wm:F2} kN/m" +
                          (op > 0 ? $"  × (1−{op:F2}) = {r.MasonryLineLoad_kNm:F2} kN/m" : ""));
            if (wp > 0)
                sb.AppendLine($"  Plaster   : 2 × {w.PlasterThickness_mm:F0} mm × {H:F2} m × {w.PlasterDensity_kNm3:F1} " +
                              $"= {wp:F2} kN/m" + (op > 0 ? $"  × (1−{op:F2}) = {r.PlasterLineLoad_kNm:F2}" : ""));
            if (wf > 0)
                sb.AppendLine($"  Finish    : {w.FinishLoad_kNm2:F2} kN/m² × {H:F2} m = {wf:F2} kN/m");
            sb.AppendLine($"  TOTAL line load = {r.TotalLineLoad_kNm:F2} kN/m");
            r.Breakdown = sb.ToString();
            return r;
        }
    }
}
