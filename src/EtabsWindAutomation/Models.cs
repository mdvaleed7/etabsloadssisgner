namespace EtabsWindAutomation;

public enum WindDirection
{
    X,
    Y
}

public sealed record DirectionSettings
{
    public double DragForceCoefficientCf { get; init; } = 1.2;
}

public sealed record ModelGeometrySettings
{
    public bool UseEtabsStories { get; init; } = true;
    public double FallbackWidthXM { get; init; } = 19.0;
    public double FallbackWidthYM { get; init; } = 120.0;
    public IReadOnlyList<StoryGeometry> ManualStories { get; init; } = Array.Empty<StoryGeometry>();
}

public sealed record LoadNamingSettings
{
    public string WindXPlus { get; init; } = "WX_PLUS";
    public string WindXMinus { get; init; } = "WX_MINUS";
    public string WindYPlus { get; init; } = "WY_PLUS";
    public string WindYMinus { get; init; } = "WY_MINUS";
    public string CrossWindXPlus { get; init; } = "CWX_PLUS";
    public string CrossWindXMinus { get; init; } = "CWX_MINUS";
    public string CrossWindYPlus { get; init; } = "CWY_PLUS";
    public string CrossWindYMinus { get; init; } = "CWY_MINUS";
}

public sealed record LoadApplicationSettings
{
    public bool ReplaceExistingWindPointLoads { get; init; } = true;
    public string CoordinateSystem { get; init; } = "Global";
}

public sealed record LoadCombinationRule
{
    public string Name { get; init; } = "";
    public IReadOnlyList<LoadCombinationTerm> Terms { get; init; } = Array.Empty<LoadCombinationTerm>();
}

public sealed record LoadCombinationTerm
{
    public string CaseName { get; init; } = "";
    public double ScaleFactor { get; init; } = 1.0;
}

public sealed record BeamDistributedLoadRule
{
    public string GroupName { get; init; } = "";
    public string LoadPattern { get; init; } = "";
    public int LoadType { get; init; } = 1;
    public int Direction { get; init; } = 10;
    public double RelativeStart { get; init; } = 0.0;
    public double RelativeEnd { get; init; } = 1.0;
    public double ValueStart { get; init; }
    public double ValueEnd { get; init; }
    public string CoordinateSystem { get; init; } = "Global";
    public bool ReplaceExisting { get; init; } = true;
}

public sealed record SlabUniformLoadRule
{
    public string GroupName { get; init; } = "";
    public string LoadPattern { get; init; } = "";
    public double Value { get; init; }
    public int Direction { get; init; } = 10;
    public string CoordinateSystem { get; init; } = "Global";
    public bool ReplaceExisting { get; init; } = true;
}

public sealed record ModifierRule
{
    public string Target { get; init; } = "";
    public string TargetKind { get; init; } = "Group";
    public IReadOnlyList<double> Values { get; init; } = Array.Empty<double>();
}

public sealed record WindSettings
{
    public double BasicWindSpeedMps { get; init; } = 50.0;
    public double RiskCoefficientK1 { get; init; } = 1.0;
    public double TopographyFactorK3 { get; init; } = 1.0;
    public double CyclonicFactorK4 { get; init; } = 1.0;
    public int TerrainCategory { get; init; } = 3;
    public double FirstModeFrequencyHz { get; init; } = 0.21;
    public double DampingRatioBeta { get; init; } = 0.02;
    public double ModeShapeExponentK { get; init; } = 1.0;
    public double CrossWindForceSpectrumCoefficientCfs { get; init; } = 0.0008;
    public DirectionSettings XDirection { get; init; } = new() { DragForceCoefficientCf = 1.2 };
    public DirectionSettings YDirection { get; init; } = new() { DragForceCoefficientCf = 1.4 };
    public ModelGeometrySettings ModelGeometry { get; init; } = new();
    public LoadNamingSettings LoadNaming { get; init; } = new();
    public LoadApplicationSettings LoadApplication { get; init; } = new();
    public IReadOnlyList<LoadCombinationRule> CombinationRules { get; init; } = Array.Empty<LoadCombinationRule>();
    public IReadOnlyList<BeamDistributedLoadRule> BeamDistributedLoadRules { get; init; } = Array.Empty<BeamDistributedLoadRule>();
    public IReadOnlyList<SlabUniformLoadRule> SlabUniformLoadRules { get; init; } = Array.Empty<SlabUniformLoadRule>();
    public IReadOnlyList<ModifierRule> FrameModifierRules { get; init; } = Array.Empty<ModifierRule>();
    public IReadOnlyList<ModifierRule> AreaModifierRules { get; init; } = Array.Empty<ModifierRule>();
}

public sealed record StoryGeometry
{
    public string Name { get; init; } = "";
    public double StoryHeightM { get; init; }
    public double HeightAboveGroundM { get; init; }
    public double WidthXM { get; init; }
    public double WidthYM { get; init; }
    public int TerrainCategory { get; init; } = 3;
}

public sealed record WindStoryResult
{
    public required string StoryName { get; init; }
    public required WindDirection Direction { get; init; }
    public required double HeightAboveGroundM { get; init; }
    public required double StoryHeightM { get; init; }
    public required double AlongWidthM { get; init; }
    public required double NormalWidthM { get; init; }
    public required double K2HourlyMeanWindSpeedFactor { get; init; }
    public required double DesignWindSpeedMps { get; init; }
    public required double DesignWindPressureKnPerM2 { get; init; }
    public required double TurbulenceIntensity { get; init; }
    public required double GustFactor { get; init; }
    public required double EffectiveAreaHeightM { get; init; }
    public required double AlongForceKnPerM { get; init; }
    public required double AlongStoryForceKn { get; init; }
    public required double CrossWindBaseMomentKnM { get; init; }
    public required double CrossWindStoryForceKn { get; init; }
}

public sealed record WindDirectionResult
{
    public required WindDirection Direction { get; init; }
    public required IReadOnlyList<WindStoryResult> Stories { get; init; }
}

public sealed record WindCalculationResult
{
    public required WindDirectionResult X { get; init; }
    public required WindDirectionResult Y { get; init; }
}

