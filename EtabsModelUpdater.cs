using System;
using ETABSv1;
using System.Windows.Forms;

namespace CSiNET8PluginExample1
{
    public class EtabsModelUpdater
    {
        private cSapModel _sapModel;

        public EtabsModelUpdater(cSapModel sapModel)
        {
            _sapModel = sapModel;
        }

        /// <summary>
        /// Determines the factor required to convert a thickness from millimetres
        /// into the ETABS model's present length unit.
        ///
        /// CORRECTION (EtabsModelUpdater.cs): the original code called SetSlab() with
        /// slab.Thickness directly (in mm).  If the model is in metres (the most common
        /// IS practice), this would create a property with thickness = 150 m — a slab
        /// 150 metres thick.  The corrected version queries GetPresentUnits() and
        /// converts accordingly before calling the API.
        /// </summary>
        private double MmToModelUnits()
        {
            eUnits u = _sapModel.GetPresentUnits();
            string uName = u.ToString(); // e.g. "kN_m_C", "kN_mm_C", "kip_in_C" …

            if (uName.Contains("_mm_")) return 1.0;          // mm → mm (no conversion)
            if (uName.Contains("_in_")) return 1.0 / 25.4;  // mm → inches
            if (uName.Contains("_ft_")) return 1.0 / 304.8; // mm → feet
            return 1.0 / 1000.0;                             // mm → metres (default/kN_m_C)
        }

        public void PushOptimizedThickness(SlabData slab)
        {
            try
            {
                // Build a unique property name from the rounded thickness (mm)
                string newPropName = $"SLAB_{(int)Math.Round(slab.Thickness)}mm";

                // CORRECTION: convert thickness from mm to the model's current length unit.
                double thicknessModelUnits = slab.Thickness * MmToModelUnits();

                // CORRECTION: use the material name extracted from the original section
                // property (stored on SlabData.MaterialName by EtabsDataExtractor).
                // The original code passed an empty string "", which would either fail
                // silently or assign a default material — neither is acceptable.
                string matProp = string.IsNullOrEmpty(slab.MaterialName)
                    ? "M25"          // safe fallback — warn the user if this branch is hit
                    : slab.MaterialName;

                if (string.IsNullOrEmpty(slab.MaterialName))
                {
                    MessageBox.Show(
                        $"Warning: material name for slab '{slab.Name}' was not extracted " +
                        $"from the model. Defaulting to 'M25'. Please verify the section " +
                        $"property '{newPropName}' after the push.",
                        "Material Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                // Create (or overwrite) the slab section property
                int retSet = _sapModel.PropArea.SetSlab(
                    newPropName,
                    eSlabType.Slab,
                    eShellType.ShellThin,
                    matProp,
                    thicknessModelUnits);

                if (retSet != 0)
                {
                    MessageBox.Show(
                        $"SetSlab() failed for property '{newPropName}' " +
                        $"(ret={retSet}). Check material name '{matProp}' and units.",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Assign the new property to the area object
                int retAssign = _sapModel.AreaObj.SetProperty(slab.Name, newPropName);

                if (retAssign == 0)
                {
                    MessageBox.Show(
                        $"Slab '{slab.Name}' updated to {slab.Thickness:F0} mm " +
                        $"(property: '{newPropName}', material: '{matProp}').",
                        "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(
                        $"SetProperty() failed for slab '{slab.Name}' " +
                        $"(ret={retAssign}).",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unexpected error updating ETABS model:{Environment.NewLine}{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
