using System;
using System.Collections.Generic;
using ETABSv1;

namespace CSiNET8PluginExample1
{
    /// <summary>
    /// Creates IS 456:2000 + IS 875 Part 5 Ultimate Limit State (ULS)
    /// and Serviceability Limit State (SLS) load combinations in ETABS.
    ///
    /// Combination table — IS 875 Part 5 / IS 456 Cl. 18.2
    /// ═══════════════════════════════════════════════════════════
    /// GRAVITY (ULS)
    ///   G1:  1.5(DL + SDL + LL)                  — max gravity
    ///   G2:  1.5(DL + SDL)                        — LL absent (uplift check)
    ///
    /// SEISMIC (ULS) — EQX and EQY, both ± directions
    ///   S1:  1.2(DL + SDL + LL + EQX)
    ///   S2:  1.2(DL + SDL + LL − EQX)
    ///   S3:  1.2(DL + SDL + LL + EQY)
    ///   S4:  1.2(DL + SDL + LL − EQY)
    ///   S5:  0.9×DL + 1.5×EQX           — upward seismic / overturning
    ///   S6:  0.9×DL − 1.5×EQX
    ///   S7:  0.9×DL + 1.5×EQY
    ///   S8:  0.9×DL − 1.5×EQY
    ///
    /// WIND (ULS)
    ///   W1:  1.2(DL + SDL + LL + WLX)
    ///   W2:  1.2(DL + SDL + LL − WLX)
    ///   W3:  1.2(DL + SDL + LL + WLY)
    ///   W4:  1.2(DL + SDL + LL − WLY)
    ///   W5:  0.9×DL + 1.5×WLX
    ///   W6:  0.9×DL − 1.5×WLX
    ///   W7:  0.9×DL + 1.5×WLY
    ///   W8:  0.9×DL − 1.5×WLY
    ///
    /// SERVICEABILITY (SLS) — IS 456 Cl. 23.2 (deflection/crack width)
    ///   SLS1: 1.0(DL + SDL + LL)         — total service load (deflection check)
    ///   SLS2: 1.0(DL + SDL)              — permanent load (long-term deflection)
    ///   SLS3: 1.0(DL + SDL + LL + EQX)  — quasi-permanent seismic service
    ///   SLS4: 1.0(DL + SDL + LL + EQY)
    ///
    /// ENVELOPE combinations (for quick design review)
    ///   ENV_ULS: envelope of all ULS combinations (Linear Envelope type in ETABS)
    ///   ENV_SLS: envelope of all SLS combinations
    ///
    /// Note on EQX sign convention in ETABS:
    ///   ETABS Response Spectrum cases produce absolute (unsigned) results.
    ///   To get ±EQ in combinations, set scale factor to +1.0 and −1.0 separately.
    ///   This is handled by using the RS case with ±1.0 factors below.
    ///
    /// For wind: WLX and WLY are static load cases; ±WL uses +1.0 and −1.0 factors.
    /// </summary>
    public class LoadCombinationCreator
    {
        private readonly cSapModel _sapModel;

        public LoadCombinationCreator(cSapModel sapModel)
        {
            _sapModel = sapModel;
        }

        // Cached snapshot of existing combination names for this run, so we make
        // one GetNameList call instead of one per ComboExists() check.
        private HashSet<string> _existingCombos;

        public string CreateAllCombinations(BuildingConfig cfg)
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine("=== Creating Load Combinations ===");

            // Combinations are definitions — the model must be unlocked first.
            EtabsModelGuard.EnsureUnlocked(_sapModel, log);

            _existingCombos = GetExistingCombos();
            int nBefore = _existingCombos.Count;

            // ── ULS: Gravity ─────────────────────────────────────────────────
            AddLinear("IS875_G1_ULS", log, cfg,
                (cfg.PatternDead, 1.5), (cfg.PatternSDL, 1.5), (cfg.PatternLive, 1.5));

            AddLinear("IS875_G2_ULS_NoLL", log, cfg,
                (cfg.PatternDead, 1.5), (cfg.PatternSDL, 1.5));

            // ── ULS: Seismic (IS 1893:2016 Cl. 6.3.4) ───────────────────────
            // Factors: 1.2 for DL+LL+EQ; 0.9 DL for overturning check.
            // IS 1893:2016 does not reduce LL for seismic combination here;
            // IS 875 Part 5 / IS 456 Table 18 use 1.2 on ALL loads including LL.
            AddLinear("IS875_S1_EQX+", log, cfg,
                (cfg.PatternDead, 1.2), (cfg.PatternSDL, 1.2),
                (cfg.PatternLive, 1.2), (cfg.CaseEQX,   +1.2));

            AddLinear("IS875_S2_EQX-", log, cfg,
                (cfg.PatternDead, 1.2), (cfg.PatternSDL, 1.2),
                (cfg.PatternLive, 1.2), (cfg.CaseEQX,   -1.2));

            AddLinear("IS875_S3_EQY+", log, cfg,
                (cfg.PatternDead, 1.2), (cfg.PatternSDL, 1.2),
                (cfg.PatternLive, 1.2), (cfg.CaseEQY,   +1.2));

            AddLinear("IS875_S4_EQY-", log, cfg,
                (cfg.PatternDead, 1.2), (cfg.PatternSDL, 1.2),
                (cfg.PatternLive, 1.2), (cfg.CaseEQY,   -1.2));

            // IS 1893:2016 Cl. 6.3.4.2(b): 1.5(DL + EQ) — seismic with no live load.
            // SDL is permanent dead load and is included at the same 1.5 factor.
            AddLinear("IS1893_S1b_15DLEQX+", log, cfg,
                (cfg.PatternDead, 1.5), (cfg.PatternSDL, 1.5), (cfg.CaseEQX, +1.5));
            AddLinear("IS1893_S1b_15DLEQX-", log, cfg,
                (cfg.PatternDead, 1.5), (cfg.PatternSDL, 1.5), (cfg.CaseEQX, -1.5));
            AddLinear("IS1893_S1b_15DLEQY+", log, cfg,
                (cfg.PatternDead, 1.5), (cfg.PatternSDL, 1.5), (cfg.CaseEQY, +1.5));
            AddLinear("IS1893_S1b_15DLEQY-", log, cfg,
                (cfg.PatternDead, 1.5), (cfg.PatternSDL, 1.5), (cfg.CaseEQY, -1.5));

            // IS 1893:2016 Cl. 6.3.4.2(c): 0.9 DL ± 1.5 EQ — minimum gravity /
            // overturning / uplift check. SDL is permanent dead and factored 0.9.
            AddLinear("IS1893_S5_09DL+EQX", log, cfg,
                (cfg.PatternDead, 0.9), (cfg.PatternSDL, 0.9), (cfg.CaseEQX, +1.5));
            AddLinear("IS1893_S6_09DL-EQX", log, cfg,
                (cfg.PatternDead, 0.9), (cfg.PatternSDL, 0.9), (cfg.CaseEQX, -1.5));
            AddLinear("IS1893_S7_09DL+EQY", log, cfg,
                (cfg.PatternDead, 0.9), (cfg.PatternSDL, 0.9), (cfg.CaseEQY, +1.5));
            AddLinear("IS1893_S8_09DL-EQY", log, cfg,
                (cfg.PatternDead, 0.9), (cfg.PatternSDL, 0.9), (cfg.CaseEQY, -1.5));

            // ── ULS: Wind ─────────────────────────────────────────────────────
            AddLinear("IS875_W1_WLX+", log, cfg,
                (cfg.PatternDead, 1.2), (cfg.PatternSDL, 1.2),
                (cfg.PatternLive, 1.2), (cfg.PatternWLX, +1.2));
            AddLinear("IS875_W2_WLX-", log, cfg,
                (cfg.PatternDead, 1.2), (cfg.PatternSDL, 1.2),
                (cfg.PatternLive, 1.2), (cfg.PatternWLX, -1.2));
            AddLinear("IS875_W3_WLY+", log, cfg,
                (cfg.PatternDead, 1.2), (cfg.PatternSDL, 1.2),
                (cfg.PatternLive, 1.2), (cfg.PatternWLY, +1.2));
            AddLinear("IS875_W4_WLY-", log, cfg,
                (cfg.PatternDead, 1.2), (cfg.PatternSDL, 1.2),
                (cfg.PatternLive, 1.2), (cfg.PatternWLY, -1.2));

            AddLinear("IS875_W5_09DL+WLX", log, cfg,
                (cfg.PatternDead, 0.9), (cfg.PatternSDL, 0.9), (cfg.PatternWLX, +1.5));
            AddLinear("IS875_W6_09DL-WLX", log, cfg,
                (cfg.PatternDead, 0.9), (cfg.PatternSDL, 0.9), (cfg.PatternWLX, -1.5));
            AddLinear("IS875_W7_09DL+WLY", log, cfg,
                (cfg.PatternDead, 0.9), (cfg.PatternSDL, 0.9), (cfg.PatternWLY, +1.5));
            AddLinear("IS875_W8_09DL-WLY", log, cfg,
                (cfg.PatternDead, 0.9), (cfg.PatternSDL, 0.9), (cfg.PatternWLY, -1.5));

            // ── SLS: Serviceability (IS 456 Cl. 23.2) ────────────────────────
            // Unfactored combinations for deflection and crack width checks.
            AddLinear("IS456_SLS1_Total", log, cfg,
                (cfg.PatternDead, 1.0), (cfg.PatternSDL, 1.0), (cfg.PatternLive, 1.0));
            AddLinear("IS456_SLS2_Perm", log, cfg,
                (cfg.PatternDead, 1.0), (cfg.PatternSDL, 1.0));
            AddLinear("IS456_SLS3_EQX", log, cfg,
                (cfg.PatternDead, 1.0), (cfg.PatternSDL, 1.0),
                (cfg.PatternLive, 1.0), (cfg.CaseEQX, 1.0));
            AddLinear("IS456_SLS4_EQY", log, cfg,
                (cfg.PatternDead, 1.0), (cfg.PatternSDL, 1.0),
                (cfg.PatternLive, 1.0), (cfg.CaseEQY, 1.0));

            // ── Envelope combinations ─────────────────────────────────────────
            CreateEnvelope("ENV_ULS", log,
                "IS875_G1_ULS", "IS875_G2_ULS_NoLL",
                "IS875_S1_EQX+", "IS875_S2_EQX-", "IS875_S3_EQY+", "IS875_S4_EQY-",
                "IS1893_S1b_15DLEQX+", "IS1893_S1b_15DLEQX-", "IS1893_S1b_15DLEQY+", "IS1893_S1b_15DLEQY-",
                "IS1893_S5_09DL+EQX", "IS1893_S6_09DL-EQX", "IS1893_S7_09DL+EQY", "IS1893_S8_09DL-EQY",
                "IS875_W1_WLX+", "IS875_W2_WLX-", "IS875_W3_WLY+", "IS875_W4_WLY-",
                "IS875_W5_09DL+WLX", "IS875_W6_09DL-WLX", "IS875_W7_09DL+WLY", "IS875_W8_09DL-WLY");

            CreateEnvelope("ENV_SLS", log,
                "IS456_SLS1_Total", "IS456_SLS2_Perm",
                "IS456_SLS3_EQX", "IS456_SLS4_EQY");

            int nAfter = CountCombos();
            log.AppendLine($"  Total: {nAfter - nBefore} new combinations created  " +
                           $"({nAfter} total in model)");
            return log.ToString();
        }

        private HashSet<string> GetExistingCombos()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int n = 0; string[] names = null;
            if (_sapModel.RespCombo.GetNameList(ref n, ref names) == 0 && names != null)
                foreach (var c in names) set.Add(c);
            return set;
        }

        // ── Helper: create one Linear Add combination ─────────────────────────
        // Cases can be either load patterns (eCNameType.LoadCase for auto-generated
        // static cases) or RS load cases (same type). ETABS identifies them by name.
        private void AddLinear(string comboName, System.Text.StringBuilder log,
                               BuildingConfig cfg, params (string name, double sf)[] cases)
        {
            // Skip if already exists (idempotent re-runs).
            if (ComboExists(comboName)) { log.AppendLine($"  SKIP  {comboName}"); return; }

            int ret = _sapModel.RespCombo.Add(comboName, 0); // 0 = Linear Add
            if (ret != 0) { log.AppendLine($"  FAIL  {comboName} Add (ret={ret})"); return; }

            bool allOk = true;
            foreach (var (name, sf) in cases)
            {
                eCNameType ct = eCNameType.LoadCase;
                int r2 = _sapModel.RespCombo.SetCaseList(comboName, ref ct, name, sf);
                if (r2 != 0)
                {
                    allOk = false;
                    log.AppendLine($"  WARN  {comboName}: case '{name}' not added (ret={r2}) — " +
                                   "is the case/pattern defined? (run the relevant step first)");
                }
            }

            if (allOk)
            {
                _existingCombos.Add(comboName);
                log.AppendLine($"  OK    {comboName}  [{string.Join("  ", Array.ConvertAll(cases, c => $"{(c.sf >= 0 ? "+" : "")}{c.sf:F1}×{c.name}"))}]");
            }
            else
            {
                // Roll back a partially-populated combo so the model is not left
                // with a meaningless empty/partial combination.
                _sapModel.RespCombo.Delete(comboName);
                log.AppendLine($"  FAIL  {comboName} removed (one or more referenced cases missing)");
            }
        }

        // ── Helper: create an Envelope combination ────────────────────────────
        private void CreateEnvelope(string comboName, System.Text.StringBuilder log,
                                    params string[] subCombos)
        {
            if (ComboExists(comboName)) { log.AppendLine($"  SKIP  {comboName}"); return; }

            // Only include sub-combinations that were actually created. Referencing a
            // missing combo would fail and leave a degenerate envelope.
            var present = Array.FindAll(subCombos, ComboExists);
            if (present.Length == 0)
            {
                log.AppendLine($"  SKIP  {comboName} (no sub-combinations available to envelope)");
                return;
            }

            int ret = _sapModel.RespCombo.Add(comboName, 1); // 1 = Envelope
            if (ret != 0) { log.AppendLine($"  FAIL  {comboName} Envelope (ret={ret})"); return; }

            int added = 0;
            foreach (string sub in present)
            {
                eCNameType ct = eCNameType.LoadCombo;
                if (_sapModel.RespCombo.SetCaseList(comboName, ref ct, sub, 1.0) == 0) added++;
            }
            _existingCombos.Add(comboName);
            log.AppendLine($"  OK    {comboName} (envelope of {added} combo(s))");
        }

        private bool ComboExists(string name)
        {
            if (_existingCombos != null) return _existingCombos.Contains(name);
            int n = 0; string[] names = null;
            _sapModel.RespCombo.GetNameList(ref n, ref names);
            return names != null &&
                   Array.Exists(names, c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
        }

        private int CountCombos()
        {
            int n = 0; string[] names = null;
            _sapModel.RespCombo.GetNameList(ref n, ref names);
            return n;
        }
    }
}
