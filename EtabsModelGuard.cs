using System;
using System.Text;
using ETABSv1;

namespace CSiNET8PluginExample1
{
    /// <summary>
    /// Centralises the ETABS model lock/unlock handling.
    ///
    /// Why this matters
    /// ----------------
    /// After a model has been analysed, ETABS *locks* it. While locked, ANY call
    /// that changes a definition (LoadPatterns.Add, LoadCases.*.SetCase,
    /// RespCombo.Add, AreaObj.SetLoadUniform, PropArea.SetSlab, …) is rejected
    /// with a non-zero return code, and the user is left wondering why "nothing
    /// happened". Unlocking also discards stale analysis results, which is exactly
    /// what we want before redefining loads.
    ///
    /// Every workflow step that mutates the model calls <see cref="EnsureUnlocked"/>
    /// before it begins. The unlock is idempotent and cheap.
    /// </summary>
    internal static class EtabsModelGuard
    {
        /// <summary>
        /// Unlocks the model if it is currently locked. Logs the action.
        /// Returns true if the model is (now) unlocked.
        /// </summary>
        public static bool EnsureUnlocked(cSapModel sapModel, StringBuilder log)
        {
            if (sapModel == null) return false;

            try
            {
                bool locked = sapModel.GetModelIsLocked();
                if (!locked) return true;

                int ret = sapModel.SetModelIsLocked(false);
                if (ret == 0)
                {
                    log?.AppendLine("  INFO  Model was locked (analysed) — unlocked for editing. " +
                                    "Existing analysis results are now invalidated.");
                    return true;
                }

                log?.AppendLine($"  WARN  Could not unlock model (ret={ret}). " +
                                "Unlock manually in ETABS before running load steps.");
                return false;
            }
            catch (Exception ex)
            {
                log?.AppendLine($"  WARN  Lock-state check failed ({ex.Message}). Continuing.");
                return true; // best-effort: do not block the workflow on a probe failure
            }
        }
    }
}
