using System;

namespace AdvatechEtabsPlugin
{
    // ── Seismic Zone (IS 1893:2016 Table 3) ───────────────────────────────────
    public enum SeismicZone { II, III, IV, V }

    // ── Site Soil Type (IS 1893:2016 Cl. 6.4.2) ──────────────────────────────
    public enum SiteClass
    {
        Type_I_Hard,      // Rock / hard soil
        Type_II_Medium,   // Medium soil
        Type_III_Soft     // Soft soil / liquefiable
    }

    // ── Structural System → Response Reduction Factor R (Table 9) ─────────────
    public enum StructuralSystem
    {
        RC_OMRF,             // RC Ordinary Moment Resisting Frame     R = 3.0
        RC_SMRF,             // RC Special Moment Resisting Frame (IS 13920)  R = 5.0
        RC_ShearWall_OMRF,   // RC Shear Wall + Ordinary Frame         R = 4.0
        RC_ShearWall_SMRF,   // RC Shear Wall + SMRF                   R = 5.0
        Steel_OMRF,          // Steel Ordinary Moment Frame            R = 3.0
        Steel_SMRF,          // Steel Special Moment Frame             R = 5.0
        Steel_CBF,           // Steel Concentrically Braced Frame      R = 4.0
        UnreinforcedMasonry, // URM (not recommended for seismic zones III-V)  R = 1.5
    }

    // ── Building Importance Factor (IS 1893:2016 Table 8) ─────────────────────
    public enum ImportanceFactor
    {
        Cat_III_Normal,    // Residential, office, commercial  I = 1.0
        Cat_II_Important,  // Schools, halls, IT parks          I = 1.2
        Cat_I_Critical,    // Hospitals, power plants, fire stations I = 1.5
    }

    // ── Occupancy type → IS 875 Part 2 live load ──────────────────────────────
    public enum OccupancyType
    {
        Residential,       // 2.0 kN/m²
        Office_General,    // 4.0 kN/m²
        Office_Lobby,      // 4.0 kN/m²
        Commercial_Retail, // 4.0 kN/m²
        Assembly_Hall,     // 5.0 kN/m²
        Storage_Light,     // 7.5 kN/m²
        Storage_Heavy,     // 12.0 kN/m²
        Corridor,          // 4.0 kN/m²
        Stairs,            // 5.0 kN/m²
        Roof_Accessible,   // 1.5 kN/m²
        Roof_Inaccessible, // 0.75 kN/m²
        Custom,            // user-defined
    }

    /// <summary>
    /// All parameters that describe the building for automatic load generation.
    /// Covers IS 456:2000, IS 875 Parts 1–3 and Part 5, IS 1893:2016 Part 1.
    /// </summary>
    public class BuildingConfig
    {
        // ── Gravity loads ────────────────────────────────────────────────────
        public OccupancyType Occupancy { get; set; } = OccupancyType.Office_General;
        /// <summary>Floor live load (kN/m²) per IS 875 Part 2. Auto-populated from Occupancy.</summary>
        public double LiveLoad { get; set; } = 4.0;
        /// <summary>Superimposed dead load (kN/m²) — finishes, partitions, services. IS 875 Part 1.</summary>
        public double SDL { get; set; } = 1.5;
        /// <summary>Roof live load (kN/m²). Applied to area objects on the topmost story.</summary>
        public double RoofLiveLoad { get; set; } = 1.5;
        /// <summary>Perimeter cladding/façade line load (kN/m) applied to exterior beams at each floor.</summary>
        public double CladdingLoad_kNm { get; set; } = 8.0;
        /// <summary>Parapet / handrail load at roof level (kN/m).</summary>
        public double ParapetLoad_kNm { get; set; } = 2.0;
        /// <summary>Concrete unit weight (kN/m³). Default = 25 (IS 875 Part 1 Table 1).</summary>
        public double ConcreteUnitWeight { get; set; } = 25.0;

        // ── Seismic (IS 1893:2016) ───────────────────────────────────────────
        public SeismicZone Zone { get; set; } = SeismicZone.III;
        public SiteClass SoilType { get; set; } = SiteClass.Type_II_Medium;
        public ImportanceFactor Importance { get; set; } = ImportanceFactor.Cat_III_Normal;
        public StructuralSystem StructSystem { get; set; } = StructuralSystem.RC_SMRF;
        /// <summary>
        /// Response reduction factor R (IS 1893:2016 Table 9).
        /// Auto-populated from StructSystem; can be overridden.
        /// </summary>
        public double R { get; set; } = 5.0;
        /// <summary>Modal damping ratio for response spectrum. Default 5% per IS 1893:2016 Cl. 7.2.</summary>
        public double DampingRatio { get; set; } = 0.05;
        /// <summary>Number of modal load cases (Eigen modes) for the modal analysis case.</summary>
        public int NumberOfModes { get; set; } = 12;
        /// <summary>
        /// True = create Response Spectrum cases (EQX_RS, EQY_RS) — recommended.
        /// False = create Equivalent Static Force cases (requires seismic weight assignment).
        /// </summary>
        public bool UseResponseSpectrum { get; set; } = true;

        // ── Load pattern names (user can rename if model already has named patterns) ─
        public string PatternDead    { get; set; } = "DEAD";
        public string PatternSDL     { get; set; } = "SDL";
        public string PatternLive    { get; set; } = "LIVE";
        public string PatternWLX     { get; set; } = "WLX";
        public string PatternWLY     { get; set; } = "WLY";
        public string RSFunctionName { get; set; } = "IS1893_2016_Spectrum";

        // ── Response-spectrum load-case names ────────────────────────────────
        // IMPORTANT: ETABS requires every load case to have a unique name. The
        // seismic effect is delivered ENTIRELY by these Response Spectrum cases,
        // so we deliberately do NOT create static Quake-type load patterns named
        // "EQX"/"EQY" (which would collide with these case names and serve no
        // analytical purpose). Combinations reference CaseEQX / CaseEQY below.
        public string CaseEQX        { get; set; } = "EQX_RS";
        public string CaseEQY        { get; set; } = "EQY_RS";
        public string CaseModal      { get; set; } = "Modal";

        // ── Derived properties ────────────────────────────────────────────────
        public double ZoneFactor => SeismicHelper.GetZoneFactor(Zone);
        public double ImportanceFactorValue => SeismicHelper.GetImportanceFactor(Importance);
    }
}
