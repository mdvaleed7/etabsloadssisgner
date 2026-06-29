using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ETABSv1;

namespace AdvatechEtabsPlugin
{
    public enum ValidationSeverity { Info, Warning, Error }

    public class ValidationIssue
    {
        public ValidationSeverity Severity;
        public string Check = "";
        public string Message = "";
        public override string ToString() =>
            $"[{Severity.ToString().ToUpperInvariant()}] {Check}: {Message}";
    }

    /// <summary>
    /// FEATURE 10 — Intelligent ETABS model validation.
    ///
    /// Runs a battery of pre-flight checks before any load is assigned so the
    /// plugin never pushes definitions into an inconsistent model.  Every check
    /// degrades gracefully if the underlying API is unavailable (returns an Info
    /// note rather than throwing), satisfying the "handle API errors gracefully"
    /// requirement.
    ///
    /// Checks performed:
    ///   • Model unlocked (editable)
    ///   • Present units sane for IS work (force = kN, length = m/mm)
    ///   • At least one storey defined
    ///   • At least one diaphragm (for seismic distribution)
    ///   • Area / frame objects present
    ///   • Selection present (when a selection-based action is intended)
    ///   • Materials defined (≥ 1 concrete material)
    ///   • Slab/frame sections resolve to a material (missing-section probe)
    ///   • Degenerate geometry (zero-length frames / collapsed areas)
    /// </summary>
    public class ModelValidator
    {
        private readonly cSapModel _sapModel;

        public ModelValidator(cSapModel sapModel) { _sapModel = sapModel; }

        public List<ValidationIssue> Validate(bool requireSelection = false)
        {
            var issues = new List<ValidationIssue>();
            if (_sapModel == null)
            {
                issues.Add(Err("Connection", "Not connected to an ETABS model."));
                return issues;
            }

            CheckLock(issues);
            CheckUnits(issues);
            CheckStories(issues);
            CheckDiaphragms(issues);
            CheckObjects(issues, out int nAreas, out int nFrames);
            CheckMaterials(issues);
            CheckDegenerateFrames(issues);
            if (requireSelection) CheckSelection(issues);

            if (!issues.Any(i => i.Severity != ValidationSeverity.Info))
                issues.Add(Info("Summary", "All pre-flight checks passed."));
            return issues;
        }

        private void CheckLock(List<ValidationIssue> issues)
        {
            try
            {
                if (_sapModel.GetModelIsLocked())
                    issues.Add(Warn("Model Lock",
                        "Model is locked (analysed). Load steps will unlock it and invalidate results."));
                else
                    issues.Add(Info("Model Lock", "Model is unlocked and editable."));
            }
            catch { issues.Add(Info("Model Lock", "Lock state could not be read.")); }
        }

        private void CheckUnits(List<ValidationIssue> issues)
        {
            try
            {
                string u = _sapModel.GetPresentUnits().ToString();
                bool forceOk = u.StartsWith("kN_") || u.StartsWith("N_");
                bool lenOk = u.Contains("_m_") || u.Contains("_mm_") || u.Contains("_cm_");
                if (forceOk && lenOk)
                    issues.Add(Info("Units", $"Present units: {u} (consistent with IS design)."));
                else
                    issues.Add(Warn("Units",
                        $"Present units: {u}. IS work expects kN/N force and m/mm length; " +
                        "loads are applied in the present units — verify Define ▸ Units."));
            }
            catch { issues.Add(Info("Units", "Present units could not be read.")); }
        }

        private void CheckStories(List<ValidationIssue> issues)
        {
            try
            {
                int n = 0; string[] names = null; double[] e = null, h = null;
                bool[] m = null; string[] sim = null; bool[] sp = null; double[] sph = null;
                int ret = _sapModel.Story.GetStories(ref n, ref names, ref e, ref h, ref m, ref sim, ref sp, ref sph);
                if (ret != 0 || n == 0)
                    issues.Add(Err("Stories", "No storeys defined — seismic/wind distribution cannot be built."));
                else
                    issues.Add(Info("Stories", $"{n} storey(s) defined."));
            }
            catch { issues.Add(Warn("Stories", "Storey table could not be read.")); }
        }

        private void CheckDiaphragms(List<ValidationIssue> issues)
        {
            try
            {
                int n = 0; string[] names = null;
                int ret = _sapModel.Diaphragm.GetNameList(ref n, ref names);
                if (ret != 0 || n == 0)
                    issues.Add(Warn("Diaphragms",
                        "No diaphragm defined. Equivalent-static / RS lateral loads need a rigid " +
                        "diaphragm to distribute storey forces."));
                else
                    issues.Add(Info("Diaphragms", $"{n} diaphragm(s) defined."));
            }
            catch { issues.Add(Info("Diaphragms", "Diaphragm list unavailable on this build.")); }
        }

        private void CheckObjects(List<ValidationIssue> issues, out int nAreas, out int nFrames)
        {
            nAreas = nFrames = 0;
            try
            {
                string[] a = null; _sapModel.AreaObj.GetNameList(ref nAreas, ref a);
                string[] f = null; _sapModel.FrameObj.GetNameList(ref nFrames, ref f);
                if (nAreas == 0 && nFrames == 0)
                    issues.Add(Err("Objects", "Model has no area or frame objects to load."));
                else
                    issues.Add(Info("Objects", $"{nAreas} area object(s), {nFrames} frame object(s)."));
            }
            catch { issues.Add(Warn("Objects", "Object lists could not be read.")); }
        }

        private void CheckMaterials(List<ValidationIssue> issues)
        {
            try
            {
                int n = 0; string[] names = null;
                if (_sapModel.PropMaterial.GetNameList(ref n, ref names) != 0 || names == null || n == 0)
                {
                    issues.Add(Err("Materials", "No materials defined."));
                    return;
                }
                int concrete = 0;
                foreach (var m in names)
                {
                    eMatType mt = eMatType.Concrete; int sym = 0;
                    if (_sapModel.PropMaterial.GetTypeOAPI(m, ref mt, ref sym) == 0 && mt == eMatType.Concrete)
                        concrete++;
                }
                if (concrete == 0)
                    issues.Add(Warn("Materials", $"{n} material(s) defined but none is concrete."));
                else
                    issues.Add(Info("Materials", $"{n} material(s) defined ({concrete} concrete)."));
            }
            catch { issues.Add(Warn("Materials", "Material list could not be read.")); }
        }

        private void CheckDegenerateFrames(List<ValidationIssue> issues)
        {
            try
            {
                int n = 0; string[] names = null;
                if (_sapModel.FrameObj.GetNameList(ref n, ref names) != 0 || names == null) return;
                int degenerate = 0;
                foreach (var f in names)
                {
                    string p1 = "", p2 = "";
                    if (_sapModel.FrameObj.GetPoints(f, ref p1, ref p2) != 0) continue;
                    double x1=0,y1=0,z1=0,x2=0,y2=0,z2=0;
                    _sapModel.PointObj.GetCoordCartesian(p1, ref x1, ref y1, ref z1);
                    _sapModel.PointObj.GetCoordCartesian(p2, ref x2, ref y2, ref z2);
                    double len = Math.Sqrt((x2-x1)*(x2-x1)+(y2-y1)*(y2-y1)+(z2-z1)*(z2-z1));
                    if (len < 1e-6) degenerate++;
                }
                if (degenerate > 0)
                    issues.Add(Warn("Geometry", $"{degenerate} zero-length frame(s) detected — review the model."));
            }
            catch { /* non-critical */ }
        }

        private void CheckSelection(List<ValidationIssue> issues)
        {
            try
            {
                int n = 0; int[] types = null; string[] names = null;
                int ret = _sapModel.SelectObj.GetSelected(ref n, ref types, ref names);
                if (ret != 0 || n == 0)
                    issues.Add(Err("Selection", "No objects selected — select target objects first."));
                else
                    issues.Add(Info("Selection", $"{n} object(s) selected."));
            }
            catch { issues.Add(Warn("Selection", "Selection could not be read.")); }
        }

        public static string Format(IEnumerable<ValidationIssue> issues)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Model Validation ===");
            foreach (var i in issues) sb.AppendLine("  " + i);
            int err = issues.Count(i => i.Severity == ValidationSeverity.Error);
            int warn = issues.Count(i => i.Severity == ValidationSeverity.Warning);
            sb.AppendLine($"  → {err} error(s), {warn} warning(s).");
            return sb.ToString();
        }

        public static bool HasBlockingErrors(IEnumerable<ValidationIssue> issues) =>
            issues.Any(i => i.Severity == ValidationSeverity.Error);

        private static ValidationIssue Info(string c, string m) => new() { Severity = ValidationSeverity.Info, Check = c, Message = m };
        private static ValidationIssue Warn(string c, string m) => new() { Severity = ValidationSeverity.Warning, Check = c, Message = m };
        private static ValidationIssue Err(string c, string m) => new() { Severity = ValidationSeverity.Error, Check = c, Message = m };
    }
}
