namespace EtabsWindAutomation;

public static class Is875WindCalculator
{
    private const double Terrain1Zo = 0.002;
    private const double Terrain2Zo = 0.02;
    private const double Terrain3Zo = 0.2;
    private const double Terrain4Zo = 2.0;

    public static WindCalculationResult Calculate(WindSettings settings, IReadOnlyList<StoryGeometry> stories)
    {
        if (stories.Count == 0)
        {
            throw new ArgumentException("At least one story is required.", nameof(stories));
        }

        var orderedStories = stories
            .Where(s => s.HeightAboveGroundM > 0.0 && s.StoryHeightM > 0.0)
            .OrderByDescending(s => s.HeightAboveGroundM)
            .ToArray();

        if (orderedStories.Length == 0)
        {
            throw new ArgumentException("No positive story elevations were found.", nameof(stories));
        }

        return new WindCalculationResult
        {
            X = CalculateDirection(settings, orderedStories, WindDirection.X, settings.XDirection),
            Y = CalculateDirection(settings, orderedStories, WindDirection.Y, settings.YDirection)
        };
    }

    private static WindDirectionResult CalculateDirection(
        WindSettings settings,
        IReadOnlyList<StoryGeometry> stories,
        WindDirection direction,
        DirectionSettings directionSettings)
    {
        var topHeight = stories[0].HeightAboveGroundM;
        var topTerrain = stories[0].TerrainCategory;
        var topNormalWidth = GetNormalWidth(stories[0], direction);
        var topRoughness = RoughnessHeight(topTerrain);
        var topK2 = HourlyMeanWindSpeedFactor(topHeight, topRoughness);
        var topDesignWindSpeed = settings.BasicWindSpeedMps
            * settings.RiskCoefficientK1
            * topK2
            * settings.TopographyFactorK3
            * settings.CyclonicFactorK4;
        var topTurbulence = TurbulenceIntensity(topHeight, topTerrain);
        var roughnessFactor = 2.0 * topTurbulence;
        var effectiveTurbulenceLength = EffectiveTurbulenceLength(topHeight, topTerrain);
        var averageNormalWidth = stories.Average(s => GetNormalWidth(s, direction));
        var sizeReductionFactor = 1.0
            / ((1.0 + (3.5 * settings.FirstModeFrequencyHz * topHeight / topDesignWindSpeed))
            * (1.0 + (4.0 * settings.FirstModeFrequencyHz * averageNormalWidth / topDesignWindSpeed)));
        var reducedFrequency = settings.FirstModeFrequencyHz * effectiveTurbulenceLength / topDesignWindSpeed;
        var turbulenceSpectrum = Math.PI * reducedFrequency / Math.Pow(1.0 + 70.8 * reducedFrequency * reducedFrequency, 5.0 / 6.0);
        var resonantPeakFactor = Math.Sqrt(2.0 * Math.Log(3600.0 * settings.FirstModeFrequencyHz));

        var rows = new List<WindStoryResult>(stories.Count);
        for (var i = 0; i < stories.Count; i++)
        {
            var story = stories[i];
            var z = story.HeightAboveGroundM;
            var terrain = story.TerrainCategory;
            var normalWidth = GetNormalWidth(story, direction);
            var alongWidth = GetAlongWidth(story, direction);
            var z0 = RoughnessHeight(terrain);
            var k2 = HourlyMeanWindSpeedFactor(z, z0);
            var vd = settings.BasicWindSpeedMps
                * settings.RiskCoefficientK1
                * k2
                * settings.TopographyFactorK3
                * settings.CyclonicFactorK4;
            var pd = 0.6 * vd * vd / 1000.0;
            var turbulence = TurbulenceIntensity(z, terrain);
            var peakFactorUpwind = terrain < 3 ? 3.0 : 4.0;
            var averageBreadthToStory = stories.Take(i + 1).Average(s => GetNormalWidth(s, direction));
            var heightFactor = 1.0 + Math.Pow(z / topHeight, 2.0);
            var backgroundFactor = 1.0 / (1.0
                + Math.Sqrt(0.26 * Math.Pow(topHeight - z, 2.0) + 0.46 * averageBreadthToStory * averageBreadthToStory)
                / effectiveTurbulenceLength);
            var secondOrderTurbulence = peakFactorUpwind * topTurbulence * Math.Sqrt(backgroundFactor) / 2.0;
            var gustFactor = 1.0 + roughnessFactor * Math.Sqrt(
                peakFactorUpwind * peakFactorUpwind * backgroundFactor * Math.Pow(1.0 + secondOrderTurbulence, 2.0)
                + (heightFactor * resonantPeakFactor * resonantPeakFactor * sizeReductionFactor * turbulenceSpectrum
                   / settings.DampingRatioBeta));

            var effectiveAreaHeight = i == 0
                ? story.StoryHeightM / 2.0
                : (story.StoryHeightM + stories[i - 1].StoryHeightM) / 2.0;

            var alongForceKnPerM = directionSettings.DragForceCoefficientCf * effectiveAreaHeight * pd * gustFactor;
            var alongStoryForceKn = alongForceKnPerM * normalWidth;
            var crossBaseMoment = 0.5
                * Math.Sqrt(2.0 * Math.Log(3600.0 * settings.FirstModeFrequencyHz))
                * pd
                * normalWidth
                * z
                * z
                * (1.06 - 0.06 * settings.ModeShapeExponentK)
                * Math.Sqrt(Math.PI * settings.CrossWindForceSpectrumCoefficientCfs / settings.DampingRatioBeta);
            var crossStoryForce = (3.0 * crossBaseMoment * z / Math.Pow(topHeight, 3.0)) * story.StoryHeightM / 2.0;

            rows.Add(new WindStoryResult
            {
                StoryName = story.Name,
                Direction = direction,
                HeightAboveGroundM = z,
                StoryHeightM = story.StoryHeightM,
                AlongWidthM = alongWidth,
                NormalWidthM = normalWidth,
                K2HourlyMeanWindSpeedFactor = k2,
                DesignWindSpeedMps = vd,
                DesignWindPressureKnPerM2 = pd,
                TurbulenceIntensity = turbulence,
                GustFactor = gustFactor,
                EffectiveAreaHeightM = effectiveAreaHeight,
                AlongForceKnPerM = alongForceKnPerM,
                AlongStoryForceKn = alongStoryForceKn,
                CrossWindBaseMomentKnM = crossBaseMoment,
                CrossWindStoryForceKn = crossStoryForce
            });
        }

        return new WindDirectionResult
        {
            Direction = direction,
            Stories = rows
        };
    }

    private static double GetAlongWidth(StoryGeometry story, WindDirection direction)
    {
        return direction == WindDirection.X ? story.WidthXM : story.WidthYM;
    }

    private static double GetNormalWidth(StoryGeometry story, WindDirection direction)
    {
        return direction == WindDirection.X ? story.WidthYM : story.WidthXM;
    }

    private static double RoughnessHeight(int terrainCategory)
    {
        return terrainCategory switch
        {
            1 => Terrain1Zo,
            2 => Terrain2Zo,
            3 => Terrain3Zo,
            4 => Terrain4Zo,
            _ => throw new ArgumentOutOfRangeException(nameof(terrainCategory), "Terrain category must be 1, 2, 3, or 4.")
        };
    }

    private static double EffectiveTurbulenceLength(double topHeightM, int terrainCategory)
    {
        var coefficient = terrainCategory < 4 ? 85.0 : 70.0;
        return coefficient * Math.Pow(topHeightM / 10.0, 0.25);
    }

    private static double HourlyMeanWindSpeedFactor(double heightM, double roughnessHeightM)
    {
        return 0.1423 * Math.Log(heightM / roughnessHeightM) * Math.Pow(roughnessHeightM, 0.0706);
    }

    private static double TurbulenceIntensity(double heightM, int terrainCategory)
    {
        var terrain1 = 0.3507 - 0.0535 * Math.Log10(heightM / Terrain1Zo);
        var terrain4 = 0.466 - 0.1358 * Math.Log10(heightM / Terrain4Zo);
        return terrainCategory switch
        {
            1 => terrain1,
            2 => terrain1 + (terrain4 - terrain1) / 7.0,
            3 => terrain1 + 3.0 * (terrain4 - terrain1) / 7.0,
            4 => terrain4,
            _ => throw new ArgumentOutOfRangeException(nameof(terrainCategory), "Terrain category must be 1, 2, 3, or 4.")
        };
    }
}

