using System.Text;
using ETABSv1;

namespace EtabsWindAutomation;

public sealed class EtabsLoadApplier
{
    private readonly cSapModel _sapModel;
    private readonly StringBuilder _log;

    public EtabsLoadApplier(cSapModel sapModel, StringBuilder log)
    {
        _sapModel = sapModel;
        _log = log;
    }

    public void EnsureWindPatternsAndCases(WindSettings settings)
    {
        foreach (var name in WindPatternNames(settings.LoadNaming))
        {
            EnsureLoadPattern(name, eLoadPatternType.Wind);
            EnsureStaticLinearCase(name, name);
        }
    }

    public void ApplyWindStoryForces(WindSettings settings, WindCalculationResult result)
    {
        ApplyDirection(result.X.Stories, settings.LoadNaming.WindXPlus, globalXSign: 1.0, globalYSign: 0.0, settings);
        ApplyDirection(result.X.Stories, settings.LoadNaming.WindXMinus, globalXSign: -1.0, globalYSign: 0.0, settings);
        ApplyCrossDirection(result.X.Stories, settings.LoadNaming.CrossWindXPlus, globalXSign: 0.0, globalYSign: 1.0, settings);
        ApplyCrossDirection(result.X.Stories, settings.LoadNaming.CrossWindXMinus, globalXSign: 0.0, globalYSign: -1.0, settings);

        ApplyDirection(result.Y.Stories, settings.LoadNaming.WindYPlus, globalXSign: 0.0, globalYSign: 1.0, settings);
        ApplyDirection(result.Y.Stories, settings.LoadNaming.WindYMinus, globalXSign: 0.0, globalYSign: -1.0, settings);
        ApplyCrossDirection(result.Y.Stories, settings.LoadNaming.CrossWindYPlus, globalXSign: 1.0, globalYSign: 0.0, settings);
        ApplyCrossDirection(result.Y.Stories, settings.LoadNaming.CrossWindYMinus, globalXSign: -1.0, globalYSign: 0.0, settings);
    }

    public void ApplyConfiguredBeamAndSlabLoads(WindSettings settings)
    {
        foreach (var rule in settings.BeamDistributedLoadRules.Where(r => !string.IsNullOrWhiteSpace(r.GroupName)))
        {
            EnsureLoadPattern(rule.LoadPattern, eLoadPatternType.Other);
            var ret = _sapModel.FrameObj.SetLoadDistributed(
                rule.GroupName,
                rule.LoadPattern,
                rule.LoadType,
                rule.Direction,
                rule.RelativeStart,
                rule.RelativeEnd,
                rule.ValueStart,
                rule.ValueEnd,
                rule.CoordinateSystem,
                RelDist: true,
                Replace: rule.ReplaceExisting,
                ItemType: eItemType.Group);
            EtabsApi.WarnOnFailure(_log, ret, $"FrameObj.SetLoadDistributed group={rule.GroupName} pattern={rule.LoadPattern}");
        }

        foreach (var rule in settings.SlabUniformLoadRules.Where(r => !string.IsNullOrWhiteSpace(r.GroupName)))
        {
            EnsureLoadPattern(rule.LoadPattern, eLoadPatternType.Other);
            var ret = _sapModel.AreaObj.SetLoadUniform(
                rule.GroupName,
                rule.LoadPattern,
                rule.Value,
                rule.Direction,
                rule.ReplaceExisting,
                rule.CoordinateSystem,
                eItemType.Group);
            EtabsApi.WarnOnFailure(_log, ret, $"AreaObj.SetLoadUniform group={rule.GroupName} pattern={rule.LoadPattern}");
        }
    }

    public void ApplyConfiguredModifiers(WindSettings settings)
    {
        foreach (var rule in settings.FrameModifierRules.Where(r => !string.IsNullOrWhiteSpace(r.Target)))
        {
            var values = rule.Values.ToArray();
            var ret = rule.TargetKind.Equals("Property", StringComparison.OrdinalIgnoreCase)
                ? _sapModel.PropFrame.SetModifiers(rule.Target, ref values)
                : _sapModel.FrameObj.SetModifiers(rule.Target, ref values, ToItemType(rule.TargetKind));
            EtabsApi.WarnOnFailure(_log, ret, $"Frame modifier target={rule.Target} kind={rule.TargetKind}");
        }

        foreach (var rule in settings.AreaModifierRules.Where(r => !string.IsNullOrWhiteSpace(r.Target)))
        {
            var values = rule.Values.ToArray();
            var ret = rule.TargetKind.Equals("Property", StringComparison.OrdinalIgnoreCase)
                ? _sapModel.PropArea.SetModifiers(rule.Target, ref values)
                : _sapModel.AreaObj.SetModifiers(rule.Target, ref values, ToItemType(rule.TargetKind));
            EtabsApi.WarnOnFailure(_log, ret, $"Area modifier target={rule.Target} kind={rule.TargetKind}");
        }
    }

    public void CreateConfiguredCombinations(WindSettings settings)
    {
        foreach (var combo in settings.CombinationRules.Where(c => !string.IsNullOrWhiteSpace(c.Name)))
        {
            _sapModel.RespCombo.Delete(combo.Name);
            var ret = _sapModel.RespCombo.Add(combo.Name, ComboType: 0);
            EtabsApi.WarnOnFailure(_log, ret, $"RespCombo.Add {combo.Name}");

            foreach (var term in combo.Terms)
            {
                var nameType = eCNameType.LoadCase;
                ret = _sapModel.RespCombo.SetCaseList(combo.Name, ref nameType, term.CaseName, term.ScaleFactor);
                EtabsApi.WarnOnFailure(_log, ret, $"RespCombo.SetCaseList combo={combo.Name} case={term.CaseName}");
            }
        }
    }

    private static IEnumerable<string> WindPatternNames(LoadNamingSettings names)
    {
        yield return names.WindXPlus;
        yield return names.WindXMinus;
        yield return names.WindYPlus;
        yield return names.WindYMinus;
        yield return names.CrossWindXPlus;
        yield return names.CrossWindXMinus;
        yield return names.CrossWindYPlus;
        yield return names.CrossWindYMinus;
    }

    private void ApplyDirection(
        IReadOnlyList<WindStoryResult> rows,
        string loadPattern,
        double globalXSign,
        double globalYSign,
        WindSettings settings)
    {
        ApplyPointLoadsByStory(rows, loadPattern, row => row.AlongStoryForceKn, globalXSign, globalYSign, settings);
    }

    private void ApplyCrossDirection(
        IReadOnlyList<WindStoryResult> rows,
        string loadPattern,
        double globalXSign,
        double globalYSign,
        WindSettings settings)
    {
        ApplyPointLoadsByStory(rows, loadPattern, row => row.CrossWindStoryForceKn, globalXSign, globalYSign, settings);
    }

    private void ApplyPointLoadsByStory(
        IReadOnlyList<WindStoryResult> rows,
        string loadPattern,
        Func<WindStoryResult, double> forceSelector,
        double globalXSign,
        double globalYSign,
        WindSettings settings)
    {
        foreach (var row in rows)
        {
            var pointCount = 0;
            string[] pointNames = Array.Empty<string>();
            var ret = _sapModel.PointObj.GetNameListOnStory(row.StoryName, ref pointCount, ref pointNames);
            if (ret != 0 || pointCount == 0)
            {
                _log.AppendLine($"WARN: No point objects found on story {row.StoryName}; skipped {loadPattern}.");
                continue;
            }

            var forcePerPoint = forceSelector(row) / pointCount;
            foreach (var pointName in pointNames)
            {
                if (settings.LoadApplication.ReplaceExistingWindPointLoads)
                {
                    _sapModel.PointObj.DeleteLoadForce(pointName, loadPattern, eItemType.Objects);
                }

                var values = new[]
                {
                    globalXSign * forcePerPoint,
                    globalYSign * forcePerPoint,
                    0.0,
                    0.0,
                    0.0,
                    0.0
                };

                ret = _sapModel.PointObj.SetLoadForce(
                    pointName,
                    loadPattern,
                    ref values,
                    Replace: false,
                    CSys: settings.LoadApplication.CoordinateSystem,
                    ItemType: eItemType.Objects);
                EtabsApi.WarnOnFailure(_log, ret, $"PointObj.SetLoadForce point={pointName} pattern={loadPattern}");
            }
        }
    }

    private void EnsureLoadPattern(string name, eLoadPatternType type)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var count = 0;
        string[] names = Array.Empty<string>();
        _sapModel.LoadPatterns.GetNameList(ref count, ref names);
        if (names.Any(existing => existing.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        EtabsApi.Check(_sapModel.LoadPatterns.Add(name, type, SelfWTMultiplier: 0.0, AddAnalysisCase: false), $"LoadPatterns.Add {name}");
        _log.AppendLine($"Created load pattern {name}.");
    }

    private void EnsureStaticLinearCase(string caseName, string loadPatternName)
    {
        EtabsApi.Check(_sapModel.LoadCases.StaticLinear.SetCase(caseName), $"StaticLinear.SetCase {caseName}");
        string[] loadTypes = { "Load" };
        string[] loadNames = { loadPatternName };
        double[] scaleFactors = { 1.0 };
        EtabsApi.Check(
            _sapModel.LoadCases.StaticLinear.SetLoads(caseName, 1, ref loadTypes, ref loadNames, ref scaleFactors),
            $"StaticLinear.SetLoads {caseName}");
    }

    private static eItemType ToItemType(string targetKind)
    {
        return targetKind.Equals("SelectedObjects", StringComparison.OrdinalIgnoreCase)
            ? eItemType.SelectedObjects
            : targetKind.Equals("Object", StringComparison.OrdinalIgnoreCase)
                ? eItemType.Objects
                : eItemType.Group;
    }
}

