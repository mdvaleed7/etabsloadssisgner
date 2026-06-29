using System;
using System.Collections.Generic;
using ETABSv1;

namespace AdvatechEtabsPlugin
{
    /// <summary>
    /// Creates the IS 875 gravity + IS 875 Part 3 wind load patterns in the model.
    ///
    /// Pattern naming convention:
    ///   DEAD   — Self-weight (SW multiplier = 1.0)  IS 875 Part 1
    ///   SDL    — Superimposed dead (finishes, partitions, MEP)  IS 875 Part 1
    ///   LIVE   — Floor live load  IS 875 Part 2
    ///   WLX    — Wind X-direction  IS 875 Part 3 (pressures applied by Wind tab)
    ///   WLY    — Wind Y-direction  IS 875 Part 3
    ///
    /// Seismic (IS 1893:2016) is delivered by Response Spectrum load CASES
    /// (see LoadCaseCreator), NOT by static Quake load patterns, so no EQX/EQY
    /// patterns are created here (doing so would collide with the RS case names).
    ///
    /// Existing patterns are never overwritten — they are skipped so user work is
    /// preserved and the step is safe to re-run (idempotent).
    /// </summary>
    public class LoadPatternCreator
    {
        private readonly cSapModel _sapModel;

        public LoadPatternCreator(cSapModel sapModel)
        {
            _sapModel = sapModel;
        }

        /// <summary>
        /// Creates all required load patterns. Returns a log of actions taken.
        /// </summary>
        public string CreateAllPatterns(BuildingConfig cfg)
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine("=== Creating Load Patterns ===");

            // The model must be unlocked before any definition can be added.
            EtabsModelGuard.EnsureUnlocked(_sapModel, log);

            // Cache the existing pattern name list ONCE (avoids an API round-trip
            // per pattern and keeps behaviour deterministic within this call).
            var existing = GetExistingPatternNames();

            // DEAD: self-weight (SW multiplier = 1.0). ETABS applies SW automatically.
            AddPattern(cfg.PatternDead, eLoadPatternType.Dead, 1.0, true, existing, log);

            // SDL: superimposed dead (finishes, partitions, services). SW mult = 0.0.
            AddPattern(cfg.PatternSDL, eLoadPatternType.SuperDead, 0.0, true, existing, log);

            // LIVE: floor live load (IS 875 Part 2).
            AddPattern(cfg.PatternLive, eLoadPatternType.Live, 0.0, true, existing, log);

            // WLX / WLY: wind (IS 875 Part 3) — pressures applied separately.
            AddPattern(cfg.PatternWLX, eLoadPatternType.Wind, 0.0, true, existing, log);
            AddPattern(cfg.PatternWLY, eLoadPatternType.Wind, 0.0, true, existing, log);

            log.AppendLine($"Load patterns complete. LL={cfg.LiveLoad} kN/m², SDL={cfg.SDL} kN/m²");
            return log.ToString();
        }

        private HashSet<string> GetExistingPatternNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int numPat = 0;
            string[] names = null;
            if (_sapModel.LoadPatterns.GetNameList(ref numPat, ref names) == 0 && names != null)
                foreach (var n in names) set.Add(n);
            return set;
        }

        private void AddPattern(string name, eLoadPatternType type, double swMult,
                                bool addCase, HashSet<string> existing,
                                System.Text.StringBuilder log)
        {
            // Validate the name before touching the API. ETABS rejects empty names
            // and names containing reserved characters; fail fast with a clear log.
            if (!IsValidName(name))
            {
                log.AppendLine($"  FAIL  '{name}' is not a valid load pattern name (empty or reserved chars)");
                return;
            }

            if (existing.Contains(name))
            {
                log.AppendLine($"  SKIP  {name,-12} (already exists)");
                return;
            }

            int ret = _sapModel.LoadPatterns.Add(name, type, swMult, addCase);
            if (ret == 0)
            {
                existing.Add(name); // keep the cache consistent for the rest of this call
                log.AppendLine($"  OK    {name,-12} Type={type,-12} SW={swMult:F1}" +
                               (addCase ? "  + auto-case" : ""));
            }
            else
            {
                log.AppendLine($"  FAIL  {name,-12} (API return code {ret})");
            }
        }

        // ETABS object names must be non-empty and free of these reserved characters.
        private static readonly char[] ReservedChars = { '"', '\'', '\\', '/', '\t', '\n', '\r' };

        private static bool IsValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return name.IndexOfAny(ReservedChars) < 0;
        }
    }
}
