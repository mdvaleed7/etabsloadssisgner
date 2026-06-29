using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvatechEtabsPlugin
{
    /// <summary>One occupancy category and its IS 875 (Part 2):1987 imposed load.</summary>
    public class OccupancyLoad
    {
        public string Category { get; }
        public string Occupancy { get; }
        /// <summary>Uniformly distributed imposed load (kN/m²) — IS 875 Part 2 Table 1.</summary>
        public double UDL_kNm2 { get; }
        /// <summary>Concentrated load (kN) — for punching / local checks (informational).</summary>
        public double Concentrated_kN { get; }
        public string Clause { get; }

        public OccupancyLoad(string category, string occupancy, double udl, double conc, string clause)
        {
            Category = category; Occupancy = occupancy;
            UDL_kNm2 = udl; Concentrated_kN = conc; Clause = clause;
        }

        public override string ToString() => $"{Occupancy}  ({UDL_kNm2:F1} kN/m²)";
    }

    /// <summary>
    /// FEATURE 4 — IS 875 (Part 2):1987 imposed (live) load database.
    ///
    /// Engineering basis: Table 1 of IS 875 (Part 2) tabulates uniformly
    /// distributed and concentrated imposed loads by occupancy.  The values
    /// below reproduce that table (and the widely-used roof clauses of
    /// Cl. 4.1) so the engineer selects an OCCUPANCY rather than typing a
    /// number — eliminating the most common source of live-load error.
    ///
    /// The list is intentionally exhaustive over the categories named in the
    /// project specification plus the rest of Table 1.  Custom values are
    /// supported by the caller (Category == "Custom").
    /// </summary>
    public static class Is875LiveLoad
    {
        // NOTE: UDL values are the IS 875 Part 2 Table 1 design imposed loads.
        // Where the table gives a range, the value commonly adopted in Indian
        // practice for that occupancy is used; the engineer can always override.
        private static readonly List<OccupancyLoad> _db = new List<OccupancyLoad>
        {
            // ── Residential (Table 1, item 1) ───────────────────────────────
            new("Residential", "Residential Room / Bedroom",            2.0, 1.8, "Part 2 Tbl 1 (1)"),
            new("Residential", "Living Room",                           2.0, 1.8, "Part 2 Tbl 1 (1)"),
            new("Residential", "Kitchen (domestic)",                    3.0, 4.5, "Part 2 Tbl 1 (1)"),
            new("Residential", "Bathroom / Toilet",                     2.0, 0.0, "Part 2 Tbl 1 (1)"),
            new("Residential", "Balcony",                               3.0, 1.5, "Part 2 Tbl 1 (1)"),
            new("Residential", "Corridor / Passage / Staircase (res.)", 3.0, 4.5, "Part 2 Tbl 1 (1)"),

            // ── Office / Institutional (Table 1, items 2 & 5) ───────────────
            new("Office", "Office (general, above ground)",             2.5, 2.7, "Part 2 Tbl 1 (2)"),
            new("Office", "Office (with separate store)",               4.0, 4.5, "Part 2 Tbl 1 (2)"),
            new("Office", "Conference Room",                            4.0, 2.7, "Part 2 Tbl 1 (2)"),
            new("Office", "Corridor / Lobby / Staircase (office)",      4.0, 4.5, "Part 2 Tbl 1 (2)"),
            new("Office", "Library Reading Room",                       4.0, 4.5, "Part 2 Tbl 1 (2)"),
            new("Office", "Library Stack Room",                         6.0, 4.5, "Part 2 Tbl 1 (2)"),

            // ── Education (Table 1, item 3) ─────────────────────────────────
            new("Education", "Classroom",                               3.0, 2.7, "Part 2 Tbl 1 (3)"),
            new("Education", "Laboratory",                              3.0, 4.5, "Part 2 Tbl 1 (3)"),
            new("Education", "Assembly / Auditorium (fixed seats)",     4.0, 3.6, "Part 2 Tbl 1 (3)"),
            new("Education", "Assembly (no fixed seats) / Dance Hall",  5.0, 3.6, "Part 2 Tbl 1 (3)"),

            // ── Hospital (Table 1, item 4) ──────────────────────────────────
            new("Hospital", "Hospital Ward / Bedroom",                  2.0, 1.8, "Part 2 Tbl 1 (4)"),
            new("Hospital", "Operation Theatre / X-ray Room",           3.0, 4.5, "Part 2 Tbl 1 (4)"),
            new("Hospital", "Hospital Corridor / Kitchen / Laundry",    4.0, 4.5, "Part 2 Tbl 1 (4)"),

            // ── Hotel / Restaurant (Table 1, item 6) ────────────────────────
            new("Hospitality", "Hotel Room / Lodging Bedroom",          2.0, 1.8, "Part 2 Tbl 1 (6)"),
            new("Hospitality", "Restaurant / Dining Room",              4.0, 2.7, "Part 2 Tbl 1 (6)"),
            new("Hospitality", "Bar / Cafeteria",                       4.0, 2.7, "Part 2 Tbl 1 (6)"),
            new("Hospitality", "Kitchen (commercial) / Laundry",        5.0, 4.5, "Part 2 Tbl 1 (6)"),

            // ── Retail / Mercantile (Table 1, item 7) ───────────────────────
            new("Retail", "Retail Shop / Shopping Mall floor",          4.0, 3.6, "Part 2 Tbl 1 (7)"),
            new("Retail", "Showroom (heavy display)",                   5.0, 4.5, "Part 2 Tbl 1 (7)"),

            // ── Storage / Warehouse (Table 1, item 8) ───────────────────────
            new("Storage", "Storage Room (light, ≤2.4 m stack)",        5.0, 4.5, "Part 2 Tbl 1 (8)"),
            new("Storage", "Storage Room (general)",                    7.5, 4.5, "Part 2 Tbl 1 (8)"),
            new("Storage", "Warehouse (heavy) / Cold Storage",         10.0, 7.0, "Part 2 Tbl 1 (8)"),
            new("Storage", "Book / Paper Storage (per m height)",       4.0, 4.5, "Part 2 Tbl 1 (8)"),

            // ── Parking / Vehicular (Table 1, item 9) ───────────────────────
            new("Parking", "Parking (cars ≤ 2.5 t)",                    4.0, 9.0, "Part 2 Tbl 1 (9)"),
            new("Parking", "Parking (light commercial / driveway)",     5.0, 9.0, "Part 2 Tbl 1 (9)"),

            // ── Plant / Services rooms ──────────────────────────────────────
            new("Services", "Mechanical / Plant Room",                  5.0, 4.5, "Part 2 Tbl 1 (8)"),
            new("Services", "Machine Room / Lift Machine Room",         7.5, 4.5, "Part 2 Tbl 1 (8)"),
            new("Services", "Generator Room",                           7.5, 4.5, "Part 2 Tbl 1 (8)"),
            new("Services", "Electrical / Switchgear Room",             5.0, 4.5, "Part 2 Tbl 1 (8)"),
            new("Services", "Water Tank Room (excl. water load)",       5.0, 4.5, "Part 2 Tbl 1 (8)"),

            // ── Roofs (Cl. 4.1) ─────────────────────────────────────────────
            new("Roof", "Roof — Accessible (terrace, flat)",           1.5, 1.8, "Part 2 Cl. 4.1"),
            new("Roof", "Roof — Inaccessible (sloping ≤ 10°)",         0.75, 1.9, "Part 2 Cl. 4.1"),
            new("Roof", "Terrace (assembly / garden)",                  4.0, 1.8, "Part 2 Cl. 4.1"),
        };

        public static IReadOnlyList<OccupancyLoad> All => _db;

        public static IEnumerable<string> Categories =>
            _db.Select(o => o.Category).Distinct();

        public static IEnumerable<OccupancyLoad> ByCategory(string category) =>
            _db.Where(o => string.Equals(o.Category, category, StringComparison.OrdinalIgnoreCase));

        public static OccupancyLoad Find(string occupancy) =>
            _db.FirstOrDefault(o => string.Equals(o.Occupancy, occupancy, StringComparison.OrdinalIgnoreCase));
    }
}
