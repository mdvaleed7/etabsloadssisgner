using System;
using System.Collections.Generic;

namespace CSiNET8PluginExample1
{
    public enum SlabType
    {
        OneWay,
        TwoWay,
        Cantilever,
        FlatSlab,
        Unknown
    }

    public enum SupportCondition
    {
        SimplySupported,
        Continuous,
        Cantilever,
        OneEndContinuous
    }

    /// <summary>
    /// Holds geometry, loads, material grades and design outputs for one slab panel.
    ///
    /// PATCH NOTES (v2):
    ///  • Added per-slab fck (concrete grade) extracted from the assigned section's
    ///    concrete material in ETABS.
    ///  • Added Fy (steel grade) and BarDiaMain / BarDiaDist / Cover — all of which
    ///    are user-configurable from the UI (the user's specific request:
    ///    "steel rebar can be anything as per user").
    ///  • Added IsOverReinforced flag so the design engine can react when the
    ///    section needs compression steel or extra depth rather than silently
    ///    capping Mu at Mu_lim.
    /// </summary>
    public class SlabData
    {
        public string Name      { get; set; } = "";
        public string StoryName { get; set; } = "";

        // ── Dimensions (mm) ────────────────────────────────────────────────
        public double Lx { get; set; }          // Short span
        public double Ly { get; set; }          // Long span
        public double Thickness { get; set; }   // Slab thickness D (mm)

        // ── Loads (kN/m²) ──────────────────────────────────────────────────
        // DeadLoad here represents the user-applied dead pressure only.
        // Self-weight (D × 25 kN/m³) is added by the design engine — never
        // double-counted with ETABS "Self Weight Multiplier".
        public double DeadLoad { get; set; }
        public double LiveLoad { get; set; }
        public double SuperimposedDeadLoad { get; set; }

        // ── Classification ────────────────────────────────────────────────
        public SlabType Type { get; set; }
        public int BoundaryCase { get; set; }            // 1–9 (IS 456 Table 26)
        public SupportCondition Support { get; set; }    // for OneWay

        // ── Materials & cover (PATCH: now driven from ETABS + UI) ─────────
        // MaterialName  : exact concrete material string used in PropArea.SetSlab
        //                 round-trip (avoids creating a property with empty material).
        // Fck (N/mm²)   : extracted from the concrete material assigned to the slab.
        //                 If extraction fails, falls back to UI value (default 25).
        // Fy  (N/mm²)   : steel yield strength — chosen by the user (250/415/500/550).
        // Cover (mm)    : clear cover to main reinforcement (user input, default 20).
        // BarDiaMain    : main-bar diameter in mm (user input, default 10).
        // BarDiaDist    : distribution-bar diameter in mm (user input, default 8).
        public string MaterialName { get; set; } = "";
        public double Fck          { get; set; } = 25;
        public double Fy           { get; set; } = 500;
        public double Cover        { get; set; } = 20;
        public double BarDiaMain   { get; set; } = 10;
        public double BarDiaDist   { get; set; } = 8;

        // ── Design outputs ────────────────────────────────────────────────
        public double DeflectionRatio { get; set; } = 0;
        public string DesignStatus    { get; set; } = "";
        public string Notes           { get; set; } = "";
        public bool   IsOverReinforced { get; set; } = false;   // PATCH

        // ── Reinforcement outputs (Ast in mm²/m) ──────────────────────────
        public double Ast_x_bot { get; set; } = 0;
        public double Ast_y_bot { get; set; } = 0;
        public double Ast_x_top { get; set; } = 0;
        public double Ast_y_top { get; set; } = 0;

        // ── Final bar selection strings (e.g. "T10 @ 150 c/c") ────────────
        public string Bars_x_bot { get; set; } = "";
        public string Bars_y_bot { get; set; } = "";
        public string Bars_x_top { get; set; } = "";
        public string Bars_y_top { get; set; } = "";

        // ── Effective-span edge properties (mm) ───────────────────────────
        public double SupportWidthX1 { get; set; } = 0;
        public double SupportWidthX2 { get; set; } = 0;
        public double SupportWidthY1 { get; set; } = 0;
        public double SupportWidthY2 { get; set; } = 0;

        public bool IsContinuousX1 { get; set; } = false;
        public bool IsContinuousX2 { get; set; } = false;
        public bool IsContinuousY1 { get; set; } = false;
        public bool IsContinuousY2 { get; set; } = false;

        // ── Flat-slab specific (mm) ───────────────────────────────────────
        public double c1 { get; set; } = 400;       // column dim along L1
        public double c2 { get; set; } = 400;       // column dim along L2
        public bool   HasDrop  { get; set; } = false;
        public double DropL1   { get; set; } = 0;   // drop dimension along L1
        public double DropL2   { get; set; } = 0;   // drop dimension along L2
        public double DropDepth { get; set; } = 0;  // total thickness inside drop
        public string PunchingShearStatus { get; set; } = "";

        // PATCH (fix #4): TRUE when the panel is an end / edge / corner panel
        // along the L1 (long) direction — i.e. at least one of the two
        // long-direction edges is discontinuous (a free / model-boundary edge,
        // not shared with another slab).  Used by FlatSlabEngine to switch the
        // DDM long-span split from the interior (-0.65 / +0.35) to the end
        // span (-0.75 / +0.50, IS 456 Cl. 31.4.3).
        public bool IsEndPanel { get; set; } = false;
    }
}
