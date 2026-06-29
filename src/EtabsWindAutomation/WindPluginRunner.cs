using System.Text;
using ETABSv1;

namespace EtabsWindAutomation;

public static class WindPluginRunner
{
    public static void Run(cSapModel sapModel)
    {
        var log = new StringBuilder();
        log.AppendLine($"ETABS wind automation started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        sapModel.SetPresentUnits(eUnits.kN_m_C);

        var settings = SettingsLoader.LoadFromPluginFolder();
        var stories = EtabsModelExtractor.ReadStories(sapModel, settings, log);
        var wind = Is875WindCalculator.Calculate(settings, stories);

        var applier = new EtabsLoadApplier(sapModel, log);
        applier.EnsureWindPatternsAndCases(settings);
        applier.ApplyWindStoryForces(settings, wind);
        applier.ApplyConfiguredBeamAndSlabLoads(settings);
        applier.ApplyConfiguredModifiers(settings);
        applier.CreateConfiguredCombinations(settings);

        log.AppendLine("Wind calculation summary:");
        LogSummary(log, wind.X);
        LogSummary(log, wind.Y);
        log.AppendLine("ETABS wind automation completed.");

        File.WriteAllText(GetLogPath(), log.ToString());
    }

    public static void TryWriteFailureLog(Exception exception)
    {
        try
        {
            File.WriteAllText(GetLogPath(), exception.ToString());
        }
        catch
        {
            // ETABS may be closing or folder permissions may block logging.
        }
    }

    private static void LogSummary(StringBuilder log, WindDirectionResult result)
    {
        var totalAlong = result.Stories.Sum(s => s.AlongStoryForceKn);
        var totalCross = result.Stories.Sum(s => s.CrossWindStoryForceKn);
        log.AppendLine($"{result.Direction}: along total = {totalAlong:F3} kN, cross total = {totalCross:F3} kN");
    }

    private static string GetLogPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "wind-automation-log.txt");
    }
}

