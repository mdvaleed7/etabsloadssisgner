using System;
using System.Collections.Generic;

namespace AdvatechEtabsPlugin
{
    /// <summary>
    /// IS 1893 (Part 1):2016 seismic design parameter helper.
    ///
    /// Key references:
    ///   • Zone factor Z       — Table 3 (Cl. 6.4.1)
    ///   • Importance factor I — Table 8
    ///   • Response reduction R — Table 9
    ///   • Design acceleration  — Cl. 6.4.2: Ah = Z/2 × I/R × Sa/g
    ///   • Response spectrum Sa/g for 5% damping — Cl. 6.4.2, Table 8
    ///
    /// The design horizontal seismic coefficient:
    ///   Ah = (Z / 2) × (I / R) × (Sa / g)
    ///
    /// In ETABS Response Spectrum load cases, the user-defined spectrum provides
    /// the Sa/g (dimensionless) curve.  The ETABS scale factor is set to:
    ///   Scale = Z × I / (2 × R)
    /// so that when ETABS computes Ah = Scale × (Sa/g), it gives the IS 1893 value.
    /// </summary>
    public static class SeismicHelper
    {
        // ── Zone Factor Z (IS 1893:2016 Table 3) ──────────────────────────────
        public static double GetZoneFactor(SeismicZone zone)
        {
            switch (zone)
            {
                case SeismicZone.II:  return 0.10;
                case SeismicZone.III: return 0.16;
                case SeismicZone.IV:  return 0.24;
                case SeismicZone.V:   return 0.36;
                default:              return 0.16;
            }
        }

        // ── Importance Factor I (IS 1893:2016 Table 8) ────────────────────────
        public static double GetImportanceFactor(ImportanceFactor imp)
        {
            switch (imp)
            {
                case ImportanceFactor.Cat_I_Critical:    return 1.5;
                case ImportanceFactor.Cat_II_Important:  return 1.2;
                case ImportanceFactor.Cat_III_Normal:
                default:                                 return 1.0;
            }
        }

        // ── Response Reduction Factor R (IS 1893:2016 Table 9) ───────────────
        public static double GetR(StructuralSystem sys)
        {
            switch (sys)
            {
                case StructuralSystem.RC_OMRF:             return 3.0;
                case StructuralSystem.RC_SMRF:             return 5.0;
                case StructuralSystem.RC_ShearWall_OMRF:   return 4.0;
                case StructuralSystem.RC_ShearWall_SMRF:   return 5.0;
                case StructuralSystem.Steel_OMRF:          return 3.0;
                case StructuralSystem.Steel_SMRF:          return 5.0;
                case StructuralSystem.Steel_CBF:           return 4.0;
                case StructuralSystem.UnreinforcedMasonry: return 1.5;
                default:                                   return 3.0;
            }
        }

        // ── IS 875 Part 2 Live Load by Occupancy ──────────────────────────────
        public static double GetLiveLoad(OccupancyType occ)
        {
            switch (occ)
            {
                case OccupancyType.Residential:        return 2.0;
                case OccupancyType.Office_General:     return 4.0;
                case OccupancyType.Office_Lobby:       return 4.0;
                case OccupancyType.Commercial_Retail:  return 4.0;
                case OccupancyType.Assembly_Hall:      return 5.0;
                case OccupancyType.Storage_Light:      return 7.5;
                case OccupancyType.Storage_Heavy:      return 12.0;
                case OccupancyType.Corridor:           return 4.0;
                case OccupancyType.Stairs:             return 5.0;
                case OccupancyType.Roof_Accessible:    return 1.5;
                case OccupancyType.Roof_Inaccessible:  return 0.75;
                default:                               return 4.0;
            }
        }

        /// <summary>
        /// Returns discrete (T, Sa/g) pairs for a user-defined ETABS response
        /// spectrum function that matches IS 1893 (Part 1):2016 Cl. 6.4.2.
        ///
        /// The spectrum is for 5% damping (default).  For other damping ratios,
        /// IS 1893:2016 Cl. 6.4.2 gives a multiplying factor (Table 3):
        ///   2% → ×1.40,  5% → ×1.00,  10% → ×0.80,  15% → ×0.70,  20% → ×0.60
        ///
        /// The values returned are Sa/g (dimensionless).  ETABS multiplies these
        /// by the scale factor (Z × I / (2 × R)) to obtain Ah for each mode.
        /// </summary>
        public static (double[] Periods, double[] SaOverG) GetIS1893_2016_Spectrum(SiteClass soil)
        {
            // Each branch piecewise-linearly follows IS 1893:2016 Cl. 6.4.2
            // Branches: rising (0 to 0.10s), plateau, falling 1/T, floor.
            switch (soil)
            {
                case SiteClass.Type_I_Hard:
                    // Plateau 0.10 → 0.40s; 1/T branch: Sa = 1.00/T for T > 0.40s
                    return (
                        new[] { 0.00, 0.10, 0.40, 0.50, 0.67, 1.00, 1.25, 2.00, 4.00, 6.00 },
                        new[] { 1.00, 2.50, 2.50, 2.00, 1.49, 1.00, 0.80, 0.50, 0.25, 0.25 }
                    );

                case SiteClass.Type_II_Medium:
                    // Plateau 0.10 → 0.55s; 1.36/T branch for T > 0.55s
                    return (
                        new[] { 0.00, 0.10, 0.55, 0.67, 1.00, 1.36, 2.00, 4.00, 6.00 },
                        new[] { 1.00, 2.50, 2.50, 2.03, 1.36, 1.00, 0.68, 0.34, 0.34 }
                    );

                case SiteClass.Type_III_Soft:
                    // Plateau 0.10 → 0.67s; 1.67/T branch for T > 0.67s
                    return (
                        new[] { 0.00, 0.10, 0.67, 1.00, 1.67, 2.00, 4.00, 6.00 },
                        new[] { 1.00, 2.50, 2.50, 1.67, 1.00, 0.84, 0.42, 0.42 }
                    );

                default:
                    return GetIS1893_2016_Spectrum(SiteClass.Type_II_Medium);
            }
        }

        /// <summary>
        /// Closed-form design acceleration spectrum Sa/g for 5% damping
        /// (IS 1893 (Part 1):2016 Cl. 6.4.2, equations for the three soil types).
        ///
        /// For the EQUIVALENT STATIC method (Cl. 7.6) the code applies these
        /// expressions directly at the fundamental period Ta.  The piecewise
        /// branches are:
        ///
        ///   Type I  (rock / hard):   Sa/g = 1+15T (T≤0.10) ; 2.50 (0.10–0.40) ; 1.00/T (>0.40)
        ///   Type II (medium):        Sa/g = 1+15T (T≤0.10) ; 2.50 (0.10–0.55) ; 1.36/T (>0.55)
        ///   Type III(soft):          Sa/g = 1+15T (T≤0.10) ; 2.50 (0.10–0.67) ; 1.67/T (>0.67)
        ///
        /// A floor of Sa/g = 0 is NOT applied here; the long-period tail follows
        /// the 1/T branch (the code does not impose an explicit Sa/g floor for
        /// the static method — the minimum base-shear check is handled separately
        /// by the design coefficient, not the spectrum).
        /// </summary>
        public static double GetSpectralAcceleration(SiteClass soil, double T)
        {
            if (T <= 0) return 2.5;            // peak plateau for degenerate input
            if (T <= 0.10) return 1.0 + 15.0 * T;

            switch (soil)
            {
                case SiteClass.Type_I_Hard:
                    return T <= 0.40 ? 2.50 : 1.00 / T;
                case SiteClass.Type_III_Soft:
                    return T <= 0.67 ? 2.50 : 1.67 / T;
                case SiteClass.Type_II_Medium:
                default:
                    return T <= 0.55 ? 2.50 : 1.36 / T;
            }
        }

        /// <summary>
        /// Damping correction factor (IS 1893:2016 Cl. 6.4.2).
        /// Multiplies the 5% spectrum Sa/g values to obtain the value at a different damping.
        /// </summary>
        public static double GetDampingFactor(double dampingRatio)
        {
            // IS 1893:2016 Table 3 (Cl. 6.4.2):
            if (dampingRatio <= 0.02) return 1.40;
            if (dampingRatio <= 0.05) return 1.00 + (0.05 - dampingRatio) / (0.05 - 0.02) * 0.40;
            if (dampingRatio <= 0.10) return 1.00 - (dampingRatio - 0.05) / (0.10 - 0.05) * 0.20;
            if (dampingRatio <= 0.15) return 0.80 - (dampingRatio - 0.10) / (0.15 - 0.10) * 0.10;
            if (dampingRatio <= 0.20) return 0.70 - (dampingRatio - 0.15) / (0.20 - 0.15) * 0.10;
            return 0.60;
        }

        /// <summary>
        /// ETABS scale factor for the RS load case.
        /// ETABS computes: spectral acceleration = ScaleFactor × (Sa/g from function)
        /// IS 1893:2016 Cl. 6.4.2: Ah = (Z/2) × (I/R) × (Sa/g)
        /// Therefore:  ScaleFactor = Z × I / (2 × R)
        /// </summary>
        public static double GetRS_ScaleFactor(BuildingConfig cfg)
        {
            double Z = cfg.ZoneFactor;
            double I = cfg.ImportanceFactorValue;
            double R = cfg.R;
            return Z * I / (2.0 * R);
        }

        /// <summary>
        /// Returns a human-readable summary of the seismic parameters for logging.
        /// </summary>
        public static string GetSummary(BuildingConfig cfg)
        {
            double Ah_max = cfg.ZoneFactor * cfg.ImportanceFactorValue / (2.0 * cfg.R) * 2.5;
            return
                $"Zone {cfg.Zone} → Z = {cfg.ZoneFactor:F2} | " +
                $"I = {cfg.ImportanceFactorValue:F1} | R = {cfg.R:F1} | " +
                $"Soil {cfg.SoilType} | Damping = {cfg.DampingRatio * 100:F0}% | " +
                $"Ah,max = {Ah_max:F4}  (at peak Sa/g = 2.5)";
        }
    }
}
