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

    public class SlabData
    {
        public string Name { get; set; }
        public string StoryName { get; set; }

        // Dimensions
        public double Lx { get; set; }          // Short span (mm)
        public double Ly { get; set; }          // Long span (mm)
        public double Thickness { get; set; }   // mm

        // Loads (kN/m²)
        // NOTE: DeadLoad here represents superimposed/user-applied dead load only.
        //       Self-weight is computed at design time from Thickness × 25 kN/m³.
        public double DeadLoad { get; set; }
        public double LiveLoad { get; set; }
        public double SuperimposedDeadLoad { get; set; }

        // Classification
        public SlabType Type { get; set; }
        public int BoundaryCase { get; set; }           // 1 to 9 (for TwoWay, IS 456 Table 26)
        public SupportCondition Support { get; set; }  // for OneWay

        // CORRECTION (SlabData.cs): added MaterialName so that EtabsModelUpdater
        // can re-use the original material when creating the resized property,
        // instead of passing an empty string to SetSlab().
        public string MaterialName { get; set; } = "";

        // Design outputs
        public double DeflectionRatio { get; set; } = 0;
        public string DesignStatus { get; set; } = "";
        public string Notes { get; set; } = "";

        // Reinforcement outputs (Ast in mm2/m)
        public double Ast_x_bot { get; set; } = 0;
        public double Ast_y_bot { get; set; } = 0;
        public double Ast_x_top { get; set; } = 0;
        public double Ast_y_top { get; set; } = 0;

        // Final Bar Selection Strings (e.g., "T10 @ 150 c/c")
        public string Bars_x_bot { get; set; } = "";
        public string Bars_y_bot { get; set; } = "";
        public string Bars_x_top { get; set; } = "";
        public string Bars_y_top { get; set; } = "";
    }
}
