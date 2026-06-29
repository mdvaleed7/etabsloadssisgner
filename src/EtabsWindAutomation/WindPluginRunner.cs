using System.Text;
using ETABSv1;

namespace EtabsWindAutomation;

public static class WindPluginRunner
{
    public static void Run(cSapModel sapModel)
    {
        ArgumentNullException.ThrowIfNull(sapModel);

        var log = new StringBuilder();
        log.AppendLine($"ETABS wind automation started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        // 1) The model must be UNLOCKED before any definition is added or any
        //    load is applied. After an analysis ETABS locks the model and then
        //    silently rejects (non-zero return code) every mutating call -
        //    SetPresentUnits, LoadPatterns.Add, StaticLinear.SetCase,
        //    PointObj.SetLoadForce, RespCombo.Add, ... - leaving the user
        //    wondering why "nothing happened".
        EnsureUnlocked(sapModel, log);

        // 2) The IS 875 Part 3 forces are computed in kN and m, so the model
        //    must report kN_m_C while we push point loads / distributed loads.
        //    We capture the user's current units first and ALWAYS restore them
        //    afterwards (including on failure) so the automation does not leave
        //    the model silently displaying different units than the user chose.
        var originalUnits = sapModel.GetPresentUnits();
        var unitsChanged = false;
        if (originalUnits != eUnits.kN_m_C)
        {
            var unitRet = sapModel.SetPresentUnits(eUnits.kN_m_C);
            if (unitRet == 0)
            {
                unitsChanged = true;
                log.AppendLine($"  INFO  Present units temporarily switched {originalUnits} -> kN_m_C for force consistency.");
            }
            else
            {
                log.AppendLine($"  WARN  Could not switch present units to kN_m_C (ret={unitRet}); " +
                               "forces assume kN/m - verify model units before relying on results.");
            }
        }

        try
        {
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
        }
        finally
        {
            // 3) Restore the user's original units no matter what happened.
            if (unitsChanged)
            {
                var restoreRet = sapModel.SetPresentUnits(originalUnits);
                log.AppendLine(restoreRet == 0
                    ? $"  INFO  Present units restored to {originalUnits}."
                    : $"  WARN  Could not restore present units to {originalUnits} (ret={restoreRet}).");
            }

            File.WriteAllText(GetLogPath(), log.ToString());
        }
    }

    /// <summary>
    /// Unlocks the model if ETABS has locked it (which happens after an
    /// analysis). Mutating-definition calls are rejected while locked, so this
    /// must run before any load definition or assignment. The unlock is
    /// idempotent; unlocking also discards stale analysis results, which is the
    /// desired behaviour before we redefine wind loads.
    /// </summary>
    private static void EnsureUnlocked(cSapModel sapModel, StringBuilder log)
    {
        try
        {
            if (!sapModel.GetModelIsLocked())
            {
                return;
            }

            var ret = sapModel.SetModelIsLocked(false);
            log.AppendLine(ret == 0
                ? "  INFO  Model was locked (analysed) - unlocked for editing. Existing analysis results are now invalidated."
                : $"  WARN  Could not unlock model (ret={ret}). Unlock manually in ETABS before running wind automation.");
        }
        catch (Exception ex)
        {
            // Best-effort: a probe failure should not block the whole workflow.
            log.AppendLine($"  WARN  Lock-state check failed ({ex.Message}). Continuing.");
        }
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

