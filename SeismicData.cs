using System;

namespace AdvatechEtabsPlugin
{
    // ───────────────────────────────────────────────────────────────────────
    //  Seismic load definition data model (IS 1893 (Part 1):2016)
    //
    //  This file collects EVERY parameter the plugin needs to define a complete
    //  Equivalent Static (Cl. 7.6) OR Response Spectrum (Cl. 7.7) earthquake
    //  load, plus the directional / eccentricity options ETABS exposes for the
    //  IS1893:2016 auto-seismic load pattern.
    //
    //  Design references:
    //    • Zone factor Z              — Table 3 (Cl. 6.4.2)
    //    • Importance factor I        — Table 8 (Cl. 7.2.3)
    //    • Response reduction R        — Table 9 (Cl. 7.2.6)
    //    • Design base shear VB       — Cl. 7.6.1 / 7.6.2
    //    • Fundamental period Ta       — Cl. 7.6.2
    //    • Vertical distribution Qi    — Cl. 7.6.3
    //    • Accidental eccentricity     — Cl. 7.9.2 (±0.05 b each way)
    // ───────────────────────────────────────────────────────────────────────

    /// <summary>Direction / eccentricity option for an equivalent static EQ pattern.</summary>
    public enum SeismicDirection
    {
        X_Plus_Ecc,     // Global X, +eccentricity
        X_Minus_Ecc,    // Global X, -eccentricity
        X_NoEcc,        // Global X, no accidental eccentricity
        Y_Plus_Ecc,     // Global Y, +eccentricity
        Y_Minus_Ecc,    // Global Y, -eccentricity
        Y_NoEcc         // Global Y, no accidental eccentricity
    }

    /// <summary>How the fundamental natural period Ta is determined.</summary>
    public enum TimePeriodMode
    {
        /// <summary>Program calculates Ta from the empirical IS 1893 expressions (Cl. 7.6.2).</summary>
        AutoEmpirical,
        /// <summary>Engineer supplies Ta directly (e.g. from a modal analysis).</summary>
        Manual
    }

    /// <summary>
    /// Empirical lateral-load-resisting system used by IS 1893:2016 Cl. 7.6.2
    /// for the approximate fundamental natural period Ta.  This is intentionally
    /// distinct from <see cref="StructuralSystem"/> (which drives R): the period
    /// formula depends on the framing material and infill, not on the ductility
    /// detailing class.
    /// </summary>
    public enum PeriodFrameType
    {
        /// <summary>RC moment-resisting frame, bare frame.  Ta = 0.075 h^0.75 (Cl. 7.6.2 a).</summary>
        RC_MRF,
        /// <summary>Steel moment-resisting frame, bare frame.  Ta = 0.085 h^0.75 (Cl. 7.6.2 a).</summary>
        Steel_MRF,
        /// <summary>RC/Steel frame with unreinforced masonry infill panels.
        /// Ta = 0.09 h / sqrt(d) (Cl. 7.6.2 b / c).</summary>
        RC_InfilledFrame,
        Steel_InfilledFrame,
        /// <summary>All other buildings (incl. shear-wall buildings) use the
        /// infill expression with the plan base dimension d (Cl. 7.6.2 c).</summary>
        Other
    }

    /// <summary>
    /// Full equivalent-static + response-spectrum seismic definition.  Populated
    /// from the UI and consumed by the time-period calculator, the static EQ
    /// pattern builder, and the RS case builder.
    /// </summary>
    public class SeismicData
    {
        // ── Core IS 1893 parameters (all overridable) ────────────────────────
        public SeismicZone Zone { get; set; } = SeismicZone.III;

        /// <summary>Zone factor Z.  Defaults from Zone but can be overridden for
        /// site-specific studies (IS 1893 Cl. 6.4.2 permits special studies).</summary>
        public double ZoneFactorOverride { get; set; } = double.NaN;
        public double ZoneFactor =>
            double.IsNaN(ZoneFactorOverride) ? SeismicHelper.GetZoneFactor(Zone) : ZoneFactorOverride;

        public ImportanceFactor Importance { get; set; } = ImportanceFactor.Cat_III_Normal;
        public double ImportanceFactorOverride { get; set; } = double.NaN;
        public double ImportanceFactorValue =>
            double.IsNaN(ImportanceFactorOverride)
                ? SeismicHelper.GetImportanceFactor(Importance)
                : ImportanceFactorOverride;

        public StructuralSystem StructSystem { get; set; } = StructuralSystem.RC_SMRF;
        /// <summary>Response reduction factor R (Table 9).</summary>
        public double R { get; set; } = 5.0;

        public SiteClass SoilType { get; set; } = SiteClass.Type_II_Medium;

        /// <summary>Modal damping ratio (fraction).  IS 1893 Cl. 7.2.4 → 5% for RC/steel.</summary>
        public double DampingRatio { get; set; } = 0.05;

        // ── Directional / eccentricity definition (Cl. 7.9) ──────────────────
        public SeismicDirection Direction { get; set; } = SeismicDirection.X_Plus_Ecc;

        /// <summary>Accidental eccentricity ratio of plan dimension (Cl. 7.9.2 → 0.05).</summary>
        public double AccidentalEccentricityRatio { get; set; } = 0.05;

        /// <summary>Name of the rigid diaphragm the lateral force is applied to.
        /// Empty → ETABS applies to all diaphragms / auto.</summary>
        public string Diaphragm { get; set; } = "";

        // ── Storey range over which the auto lateral load is distributed ─────
        public string TopStory { get; set; } = "";
        public string BottomStory { get; set; } = "";

        // ── Time period (Cl. 7.6.2) ──────────────────────────────────────────
        public TimePeriodMode PeriodMode { get; set; } = TimePeriodMode.AutoEmpirical;
        public PeriodFrameType PeriodFrame { get; set; } = PeriodFrameType.RC_MRF;

        /// <summary>Building height above base (m).  Auto-detected from the model
        /// when 0; can be overridden by the engineer.</summary>
        public double BuildingHeight_m { get; set; } = 0.0;

        /// <summary>Plan base dimension d (m) in the direction of motion — used by
        /// the infilled-frame / "other" period formula (Cl. 7.6.2 b/c).</summary>
        public double BaseDimension_m { get; set; } = 0.0;

        /// <summary>Aspect-ratio masonry-infill area factor AW (Cl. 7.6.2 c, eq. for
        /// buildings with infill).  Optional; 0 → use the simpler d-based formula.</summary>
        public double InfillAreaFactor_AW { get; set; } = 0.0;

        /// <summary>Manually entered fundamental period Ta (s) — used when
        /// <see cref="PeriodMode"/> is Manual.</summary>
        public double ManualPeriod_s { get; set; } = 0.0;

        /// <summary>Result of the empirical calculation (s), filled by
        /// <see cref="TimePeriodCalculator"/>.</summary>
        public double CalculatedPeriod_s { get; set; } = 0.0;

        /// <summary>The period actually used in the base-shear / Sa/g lookup (s).</summary>
        public double EffectivePeriod_s =>
            PeriodMode == TimePeriodMode.Manual && ManualPeriod_s > 0
                ? ManualPeriod_s
                : (CalculatedPeriod_s > 0 ? CalculatedPeriod_s : ManualPeriod_s);

        // ── ETABS object names ───────────────────────────────────────────────
        public string PatternName { get; set; } = "EQX_STATIC";

        // ── Derived helpers ──────────────────────────────────────────────────

        /// <summary>True when the configured direction acts along global X.</summary>
        public bool IsXDirection =>
            Direction == SeismicDirection.X_Plus_Ecc ||
            Direction == SeismicDirection.X_Minus_Ecc ||
            Direction == SeismicDirection.X_NoEcc;

        /// <summary>Signed eccentricity ratio (+/-/0) for the chosen direction.</summary>
        public double SignedEccentricity =>
            Direction switch
            {
                SeismicDirection.X_Plus_Ecc or SeismicDirection.Y_Plus_Ecc => +AccidentalEccentricityRatio,
                SeismicDirection.X_Minus_Ecc or SeismicDirection.Y_Minus_Ecc => -AccidentalEccentricityRatio,
                _ => 0.0
            };

        /// <summary>
        /// Design horizontal seismic coefficient Ah (IS 1893 Cl. 6.4.2):
        ///   Ah = (Z/2) · (I/R) · (Sa/g)
        /// where Sa/g is evaluated at the EFFECTIVE period for the configured soil.
        /// Clamped to the code minimum (Cl. 7.2.2 floor handled by SpectrumValue).
        /// </summary>
        public double DesignHorizontalCoefficient()
        {
            double saG = SeismicHelper.GetSpectralAcceleration(SoilType, EffectivePeriod_s);
            double ah = (ZoneFactor / 2.0) * (ImportanceFactorValue / R) * saG;
            return ah;
        }
    }
}
