using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AdvatechEtabsPlugin
{
    /// <summary>
    /// FEATURE 5 — Dead Load (superimposed) assistant.
    ///
    /// Engineering basis: IS 875 (Part 1):1987 Table 1 unit weights of building
    /// materials.  The engineer assembles a finish build-up from individual
    /// layers (thickness × density) plus lumped allowances (false ceiling,
    /// services, partitions); the assistant returns the total superimposed dead
    /// pressure in kN/m² and an itemised breakdown for the report.
    ///
    ///   layer pressure  w_i = t_i (m) × γ_i (kN/m³)          [kN/m²]
    ///   total SDL       = Σ w_i + Σ lumped allowances        [kN/m²]
    /// </summary>
    public static class DeadLoadAssistant
    {
        /// <summary>Unit weights γ (kN/m³) — IS 875 Part 1 Table 1.</summary>
        public static readonly Dictionary<string, double> MaterialDensity_kNm3 =
            new(StringComparer.OrdinalIgnoreCase)
        {
            { "Reinforced Cement Concrete", 25.0 },
            { "Plain Cement Concrete",      24.0 },
            { "Cement Screed / Mortar",     20.0 },
            { "Cement Plaster",             20.0 },
            { "Lime Concrete",              19.0 },
            { "Ceramic / Vitrified Tile",   22.0 },
            { "Marble",                     26.5 },
            { "Granite",                    26.5 },
            { "Kota / Natural Stone",       24.0 },
            { "Terrazzo",                   23.0 },
            { "Brick Masonry",              19.0 },
            { "Waterproofing (bitumen)",    14.0 },
            { "Mud Phuska",                 16.0 },
            { "Sand Fill",                  16.0 },
            { "Glass",                      25.0 },
            { "Steel",                      78.5 },
        };

        /// <summary>A single finish layer (thickness in mm, density in kN/m³).</summary>
        public class Layer
        {
            public string Name = "";
            public double Thickness_mm;
            public double Density_kNm3;
            public double Pressure_kNm2 => (Thickness_mm / 1000.0) * Density_kNm3;
        }

        /// <summary>A lumped allowance applied directly as a pressure (kN/m²).</summary>
        public class Allowance
        {
            public string Name = "";
            public double Pressure_kNm2;
        }

        /// <summary>Common lumped allowances (IS 875 Part 1 / standard practice).</summary>
        public static readonly Dictionary<string, double> TypicalAllowance_kNm2 =
            new(StringComparer.OrdinalIgnoreCase)
        {
            { "False Ceiling",                 0.30 },
            { "MEP / Services",                0.50 },
            { "Light Partition Allowance",     1.00 },  // IS 875 Pt 1 Cl. min. partition
            { "Medium Partition Allowance",    1.50 },
            { "Heavy Partition Allowance",     2.00 },
            { "Raised Access Floor",           0.50 },
            { "Solar / Equipment (roof)",      0.50 },
        };

        public class Result
        {
            public List<Layer> Layers = new();
            public List<Allowance> Allowances = new();
            public double Total_kNm2 =>
                Layers.Sum(l => l.Pressure_kNm2) + Allowances.Sum(a => a.Pressure_kNm2);

            public string Breakdown()
            {
                var sb = new StringBuilder();
                sb.AppendLine("Superimposed Dead Load build-up (IS 875 Part 1):");
                foreach (var l in Layers)
                    sb.AppendLine($"  {l.Name,-28} {l.Thickness_mm,6:F0} mm × {l.Density_kNm3,5:F1} kN/m³ " +
                                  $"= {l.Pressure_kNm2,6:F3} kN/m²");
                foreach (var a in Allowances)
                    sb.AppendLine($"  {a.Name,-28} (lumped)                 = {a.Pressure_kNm2,6:F3} kN/m²");
                sb.AppendLine($"  {"TOTAL SDL",-28}                          = {Total_kNm2,6:F3} kN/m²");
                return sb.ToString();
            }
        }

        public static Result Build(IEnumerable<Layer> layers, IEnumerable<Allowance> allowances)
        {
            var r = new Result();
            if (layers != null) r.Layers.AddRange(layers);
            if (allowances != null) r.Allowances.AddRange(allowances);
            return r;
        }
    }
}
