using System.Text;
using ETABSv1;

namespace EtabsWindAutomation;

public static class EtabsModelExtractor
{
    public static IReadOnlyList<StoryGeometry> ReadStories(cSapModel sapModel, WindSettings settings, StringBuilder log)
    {
        if (!settings.ModelGeometry.UseEtabsStories)
        {
            return settings.ModelGeometry.ManualStories
                .OrderByDescending(s => s.HeightAboveGroundM)
                .ToArray();
        }

        var storyCount = 0;
        string[] storyNames = Array.Empty<string>();
        double[] elevations = Array.Empty<double>();
        double[] heights = Array.Empty<double>();
        bool[] isMaster = Array.Empty<bool>();
        string[] similarTo = Array.Empty<string>();
        bool[] spliceAbove = Array.Empty<bool>();
        double[] spliceHeight = Array.Empty<double>();

        EtabsApi.Check(
            sapModel.Story.GetStories(
                ref storyCount,
                ref storyNames,
                ref elevations,
                ref heights,
                ref isMaster,
                ref similarTo,
                ref spliceAbove,
                ref spliceHeight),
            "Story.GetStories");

        var stories = new List<StoryGeometry>();
        for (var i = 0; i < storyCount; i++)
        {
            if (elevations[i] <= 0.0 || heights[i] <= 0.0)
            {
                continue;
            }

            var (widthX, widthY) = TryGetStoryPlanDimensions(
                sapModel,
                storyNames[i],
                settings.ModelGeometry.FallbackWidthXM,
                settings.ModelGeometry.FallbackWidthYM,
                log);

            stories.Add(new StoryGeometry
            {
                Name = storyNames[i],
                StoryHeightM = heights[i],
                HeightAboveGroundM = elevations[i],
                WidthXM = widthX,
                WidthYM = widthY,
                TerrainCategory = settings.TerrainCategory
            });
        }

        var ordered = stories.OrderByDescending(s => s.HeightAboveGroundM).ToArray();
        log.AppendLine($"Read {ordered.Length} positive-elevation stories from ETABS.");
        return ordered;
    }

    private static (double WidthX, double WidthY) TryGetStoryPlanDimensions(
        cSapModel sapModel,
        string storyName,
        double fallbackWidthX,
        double fallbackWidthY,
        StringBuilder log)
    {
        var pointCount = 0;
        string[] pointNames = Array.Empty<string>();
        var ret = sapModel.PointObj.GetNameListOnStory(storyName, ref pointCount, ref pointNames);
        if (ret != 0 || pointCount == 0)
        {
            log.AppendLine($"WARN: Could not read point list for story {storyName}; using fallback plan dimensions.");
            return (fallbackWidthX, fallbackWidthY);
        }

        var xs = new List<double>(pointCount);
        var ys = new List<double>(pointCount);
        for (var i = 0; i < pointCount; i++)
        {
            var x = 0.0;
            var y = 0.0;
            var z = 0.0;
            ret = sapModel.PointObj.GetCoordCartesian(pointNames[i], ref x, ref y, ref z, "Global");
            if (ret == 0)
            {
                xs.Add(x);
                ys.Add(y);
            }
        }

        if (xs.Count < 2 || ys.Count < 2)
        {
            log.AppendLine($"WARN: Too few point coordinates for story {storyName}; using fallback plan dimensions.");
            return (fallbackWidthX, fallbackWidthY);
        }

        var widthX = xs.Max() - xs.Min();
        var widthY = ys.Max() - ys.Min();
        if (widthX <= 0.0 || widthY <= 0.0)
        {
            log.AppendLine($"WARN: Invalid plan dimensions for story {storyName}; using fallback plan dimensions.");
            return (fallbackWidthX, fallbackWidthY);
        }

        return (widthX, widthY);
    }
}

