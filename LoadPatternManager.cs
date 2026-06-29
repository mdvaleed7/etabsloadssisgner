using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ETABSv1;

namespace AdvatechEtabsPlugin
{
    public class PatternReport
    {
        public string Name = "";
        public eLoadPatternType Type;
        public double SelfWtMultiplier;
        public bool UsedInCombo;
        public bool UsedInLoad;       // referenced by an area/frame load (not probed exhaustively)
        public List<string> Notes = new();
    }

    /// <summary>
    /// FEATURE 8 — Automatic load-pattern management.
    ///
    /// Audits the model's load patterns and reports:
    ///   • the full pattern inventory (type + self-weight multiplier),
    ///   • duplicate / suspicious names (case-insensitive collisions),
    ///   • patterns NOT referenced by any load combination ("possibly unused"),
    ///   • incorrect self-weight multipliers (e.g. > 1 dead pattern with SW = 1,
    ///     or a SuperDead/Live pattern carrying a non-zero SW multiplier — a very
    ///     common modelling error that double-counts self weight).
    ///
    /// All operations are READ-ONLY by default; mutating helpers
    /// (FixSelfWeightMultiplier) warn and require an explicit call.
    /// </summary>
    public class LoadPatternManager
    {
        private readonly cSapModel _sapModel;

        public LoadPatternManager(cSapModel sapModel) { _sapModel = sapModel; }

        public List<PatternReport> Audit(out string log)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Load Pattern Audit ===");
            var reports = new List<PatternReport>();

            int n = 0; string[] names = null;
            if (_sapModel.LoadPatterns.GetNameList(ref n, ref names) != 0 || names == null || n == 0)
            {
                sb.AppendLine("  No load patterns defined.");
                log = sb.ToString();
                return reports;
            }

            // Names referenced by any combination (treated as "used").
            var usedInCombos = GetPatternsUsedInCombos();

            // Detect case-insensitive duplicate names.
            var dupGroups = names.GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                                 .Where(g => g.Count() > 1)
                                 .Select(g => g.Key)
                                 .ToHashSet(StringComparer.OrdinalIgnoreCase);

            int deadWithSW = 0;
            foreach (var name in names)
            {
                var r = new PatternReport { Name = name };

                eLoadPatternType t = eLoadPatternType.Other;
                if (_sapModel.LoadPatterns.GetLoadType(name, ref t) == 0) r.Type = t;

                double sw = 0;
                if (_sapModel.LoadPatterns.GetSelfWTMultiplier(name, ref sw) == 0) r.SelfWtMultiplier = sw;

                r.UsedInCombo = usedInCombos.Contains(name);

                // ── Self-weight multiplier sanity ──
                if (r.Type == eLoadPatternType.Dead && Math.Abs(r.SelfWtMultiplier - 1.0) > 1e-6)
                    r.Notes.Add($"Dead pattern has SW multiplier {r.SelfWtMultiplier:F2} (expected 1.0).");
                if (r.Type == eLoadPatternType.Dead && Math.Abs(r.SelfWtMultiplier - 1.0) < 1e-6)
                    deadWithSW++;
                if ((r.Type == eLoadPatternType.SuperDead || r.Type == eLoadPatternType.Live ||
                     r.Type == eLoadPatternType.Wind || r.Type == eLoadPatternType.Quake)
                    && r.SelfWtMultiplier > 1e-6)
                    r.Notes.Add($"{r.Type} pattern carries SW multiplier {r.SelfWtMultiplier:F2} " +
                                "— self weight is double-counted; set it to 0.");

                if (dupGroups.Contains(name))
                    r.Notes.Add("Duplicate name (case-insensitive collision).");

                if (!r.UsedInCombo)
                    r.Notes.Add("Not referenced by any load combination (possibly unused).");

                reports.Add(r);
            }

            if (deadWithSW > 1)
                sb.AppendLine($"  WARN  {deadWithSW} Dead patterns each apply self weight (SW=1.0) " +
                              "— self weight is counted multiple times. Keep SW=1.0 on ONE dead pattern only.");

            foreach (var r in reports)
            {
                sb.AppendLine($"  {r.Name,-14} {r.Type,-10} SW={r.SelfWtMultiplier:F2}  " +
                              (r.UsedInCombo ? "[in combos]" : "[unused?]"));
                foreach (var note in r.Notes) sb.AppendLine($"       • {note}");
            }

            int issues = reports.Sum(r => r.Notes.Count);
            sb.AppendLine($"  → {reports.Count} pattern(s), {issues} note(s).");
            log = sb.ToString();
            return reports;
        }

        /// <summary>Mutating fix: set a pattern's self-weight multiplier (warns the caller).</summary>
        public string FixSelfWeightMultiplier(string name, double multiplier)
        {
            EtabsModelGuard.EnsureUnlocked(_sapModel, null);
            int ret = _sapModel.LoadPatterns.SetSelfWTMultiplier(name, multiplier);
            return ret == 0
                ? $"Set self-weight multiplier of '{name}' to {multiplier:F2}."
                : $"Failed to set self-weight multiplier of '{name}' (ret={ret}).";
        }

        private HashSet<string> GetPatternsUsedInCombos()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int nC = 0; string[] combos = null;
            if (_sapModel.RespCombo.GetNameList(ref nC, ref combos) != 0 || combos == null)
                return used;

            foreach (var c in combos)
            {
                int nItems = 0;
                eCNameType[] types = null; string[] cnames = null; double[] sf = null;
                if (_sapModel.RespCombo.GetCaseList(c, ref nItems, ref types, ref cnames, ref sf) == 0
                    && cnames != null)
                {
                    foreach (var cn in cnames) used.Add(cn);
                }
            }
            return used;
        }
    }
}
