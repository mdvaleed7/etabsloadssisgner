using System;
using System.Text;

namespace AdvatechEtabsPlugin
{
    /// <summary>
    /// FEATURE 2 — Approximate fundamental natural period Ta per
    /// IS 1893 (Part 1):2016 Cl. 7.6.2.
    ///
    /// ───────────────────────────────────────────────────────────────────────
    /// ENGINEERING LOGIC
    /// ───────────────────────────────────────────────────────────────────────
    /// IS 1893:2016 Cl. 7.6.2 gives three empirical expressions for Ta:
    ///
    ///   (a) Bare moment-resisting frame WITHOUT brick infill panels:
    ///         RC MRF    : Ta = 0.075 · h^0.75
    ///         Steel MRF : Ta = 0.085 · h^0.75
    ///       where h = height of the building (m) measured from base.
    ///
    ///   (b) All OTHER buildings, including those with brick/masonry infill
    ///       and shear-wall buildings:
    ///         Ta = 0.09 · h / sqrt(d)
    ///       where d = base dimension (m) of the building at plinth level
    ///       in the direction of the earthquake motion considered.
    ///
    ///   (c) RC structural-wall buildings (the 2016 code also gives an
    ///       Aw-based refinement):
    ///         Ta = 0.075 · h^0.75 / sqrt(Aw)
    ///       This calculator exposes <see cref="SeismicData.InfillAreaFactor_AW"/>
    ///       for that refinement when AW > 0; otherwise the d-based formula is
    ///       used, which is the common practising-engineer default.
    ///
    /// The result is direction-dependent because d differs in X and Y; the
    /// caller passes the correct base dimension for the direction analysed.
    ///
    /// The building height h is determined automatically from the ETABS storey
    /// table (top elevation − base elevation); the engineer may override.
    /// </summary>
    public static class TimePeriodCalculator
    {
        /// <summary>
        /// Computes Ta and writes it into <paramref name="data"/>.CalculatedPeriod_s.
        /// Returns a human-readable derivation (formula, inputs, result) suitable
        /// for the log and the report.
        /// </summary>
        public static string Calculate(SeismicData data)
        {
            var sb = new StringBuilder();
            double h = data.BuildingHeight_m;

            if (h <= 0)
            {
                data.CalculatedPeriod_s = 0;
                sb.AppendLine("  Ta: building height h ≤ 0 — cannot evaluate the empirical period.");
                sb.AppendLine("      Provide the height (m) or detect it from the model first.");
                return sb.ToString();
            }

            double Ta;
            string formula;
            string inputs;

            switch (data.PeriodFrame)
            {
                case PeriodFrameType.RC_MRF:
                    // Cl. 7.6.2(a): bare RC moment frame.
                    if (data.InfillAreaFactor_AW > 0)
                    {
                        Ta = 0.075 * Math.Pow(h, 0.75) / Math.Sqrt(data.InfillAreaFactor_AW);
                        formula = "Ta = 0.075·h^0.75 / √Aw   (Cl. 7.6.2c, RC wall building)";
                        inputs  = $"h = {h:F2} m, Aw = {data.InfillAreaFactor_AW:F3}";
                    }
                    else
                    {
                        Ta = 0.075 * Math.Pow(h, 0.75);
                        formula = "Ta = 0.075·h^0.75   (Cl. 7.6.2a, bare RC MRF)";
                        inputs  = $"h = {h:F2} m";
                    }
                    break;

                case PeriodFrameType.Steel_MRF:
                    Ta = 0.085 * Math.Pow(h, 0.75);
                    formula = "Ta = 0.085·h^0.75   (Cl. 7.6.2a, bare steel MRF)";
                    inputs  = $"h = {h:F2} m";
                    break;

                case PeriodFrameType.RC_InfilledFrame:
                case PeriodFrameType.Steel_InfilledFrame:
                case PeriodFrameType.Other:
                default:
                    // Cl. 7.6.2(b)/(c): infilled / other / shear-wall → d-based.
                    double d = data.BaseDimension_m;
                    if (d <= 0)
                    {
                        data.CalculatedPeriod_s = 0;
                        sb.AppendLine("  Ta: base dimension d ≤ 0 — the infilled / 'other' building");
                        sb.AppendLine("      formula Ta = 0.09·h/√d needs the plan base dimension (m).");
                        return sb.ToString();
                    }
                    Ta = 0.09 * h / Math.Sqrt(d);
                    formula = "Ta = 0.09·h / √d   (Cl. 7.6.2b, infilled / other building)";
                    inputs  = $"h = {h:F2} m, d = {d:F2} m";
                    break;
            }

            data.CalculatedPeriod_s = Ta;

            double saG = SeismicHelper.GetSpectralAcceleration(data.SoilType, Ta);
            double ah  = (data.ZoneFactor / 2.0) * (data.ImportanceFactorValue / data.R) * saG;

            sb.AppendLine("  Fundamental period (IS 1893:2016 Cl. 7.6.2)");
            sb.AppendLine($"    Formula : {formula}");
            sb.AppendLine($"    Inputs  : {inputs}");
            sb.AppendLine($"    Result  : Ta = {Ta:F4} s");
            sb.AppendLine($"    Sa/g    : {saG:F3}  (soil {data.SoilType}, 5% damping)");
            sb.AppendLine($"    Ah      : (Z/2)(I/R)(Sa/g) = " +
                          $"({data.ZoneFactor:F2}/2)({data.ImportanceFactorValue:F2}/{data.R:F1})({saG:F3}) " +
                          $"= {ah:F4}");

            return sb.ToString();
        }

        /// <summary>
        /// Maps a <see cref="StructuralSystem"/> (used for R) to the most
        /// appropriate default <see cref="PeriodFrameType"/> for Ta.  Pure
        /// convenience for pre-selecting the UI; the engineer can override.
        /// </summary>
        public static PeriodFrameType SuggestFrameType(StructuralSystem sys)
        {
            switch (sys)
            {
                case StructuralSystem.RC_OMRF:
                case StructuralSystem.RC_SMRF:
                    return PeriodFrameType.RC_MRF;
                case StructuralSystem.Steel_OMRF:
                case StructuralSystem.Steel_SMRF:
                case StructuralSystem.Steel_CBF:
                    return PeriodFrameType.Steel_MRF;
                case StructuralSystem.RC_ShearWall_OMRF:
                case StructuralSystem.RC_ShearWall_SMRF:
                case StructuralSystem.UnreinforcedMasonry:
                default:
                    return PeriodFrameType.Other;
            }
        }
    }
}
