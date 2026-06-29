using EtabsWindAutomation;

var settings = new WindSettings
{
    BasicWindSpeedMps = 50.0,
    RiskCoefficientK1 = 1.0,
    TopographyFactorK3 = 1.0,
    CyclonicFactorK4 = 1.0,
    TerrainCategory = 3,
    FirstModeFrequencyHz = 0.21,
    DampingRatioBeta = 0.02,
    ModeShapeExponentK = 1.0,
    CrossWindForceSpectrumCoefficientCfs = 0.0008,
    XDirection = new DirectionSettings { DragForceCoefficientCf = 1.2 },
    YDirection = new DirectionSettings { DragForceCoefficientCf = 1.4 }
};

var stories = WorkbookStories();
var result = Is875WindCalculator.Calculate(settings, stories);

AssertClose("X terrace along force", 1098.884, result.X.Stories[0].AlongStoryForceKn, 0.75);
AssertClose("X Story 4 along force", 779.119, result.X.Stories.Single(s => s.StoryName == "STORY  4").AlongStoryForceKn, 0.75);
AssertClose("X terrace cross force", 654.307, result.X.Stories[0].CrossWindStoryForceKn, 0.75);
AssertClose("Y terrace along force", 254.922, result.Y.Stories[0].AlongStoryForceKn, 0.75);
AssertClose("Y terrace cross force", 103.599, result.Y.Stories[0].CrossWindStoryForceKn, 0.75);

Console.WriteLine("Smoke test passed against representative workbook values.");

static void AssertClose(string name, double expected, double actual, double tolerance)
{
    if (Math.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException($"{name}: expected {expected:F3}, actual {actual:F3}, tolerance {tolerance:F3}");
    }

    Console.WriteLine($"{name}: {actual:F3} OK");
}

static IReadOnlyList<StoryGeometry> WorkbookStories()
{
    (string Name, double Height, double Elevation)[] rows =
    {
        ("TERRACE", 6.0, 101.4),
        ("STORY  21", 6.0, 95.4),
        ("STORY  20", 6.0, 89.4),
        ("STORY  19", 4.2, 83.4),
        ("STORY  18", 4.2, 79.2),
        ("STORY  17", 4.2, 75.0),
        ("STORY  16", 4.2, 70.8),
        ("STORY  15", 4.2, 66.6),
        ("STORY  14", 4.2, 62.4),
        ("STORY  13", 4.2, 58.2),
        ("STORY  12", 4.2, 54.0),
        ("STORY  11", 4.2, 49.8),
        ("STORY  10", 4.2, 45.6),
        ("STORY  9", 4.2, 41.4),
        ("STORY  8", 4.2, 37.2),
        ("STORY  7", 4.2, 33.0),
        ("STORY  6", 4.2, 28.8),
        ("STORY  5", 4.2, 24.6),
        ("STORY  4", 4.2, 20.4),
        ("STORY  3", 4.2, 16.2),
        ("STORY  2", 6.0, 12.0),
        ("STORY  1", 6.0, 6.0)
    };

    return rows
        .Select(row => new StoryGeometry
        {
            Name = row.Name,
            StoryHeightM = row.Height,
            HeightAboveGroundM = row.Elevation,
            WidthXM = 19.0,
            WidthYM = 120.0,
            TerrainCategory = 3
        })
        .ToArray();
}

