using ETABSv1;
using System;
using System.Windows.Forms;

namespace CSiNET8PluginExample1
{
    public class cPlugin : cPluginContract
    {
        private static string _version = "2.0";
        private int errorCode = 0;

        public int Info(ref string Text)
        {
            Text =
                "Advatech ETABS Automation Plugin  v" + _version + Environment.NewLine +
                "Developed by: Advatech Structural Engineers" + Environment.NewLine +
                Environment.NewLine +
                "Building Configuration" + Environment.NewLine +
                "  - Seismic zone, soil type, importance factor I, R factor" + Environment.NewLine +
                "  - Occupancy-based live load, SDL, cladding, parapet" + Environment.NewLine +
                Environment.NewLine +
                "Load Definition" + Environment.NewLine +
                "  - Create load patterns, cases, gravity assignments, and combinations" + Environment.NewLine +
                "  - IS 875 Part 5 ULS and IS 456 service combinations" + Environment.NewLine +
                Environment.NewLine +
                "Wind Automation" + Environment.NewLine +
                "  - IS 875 Part 3 wind story forces from wind-settings.json" + Environment.NewLine +
                "  - Load patterns, cases, point loads, optional rules, and combinations" + Environment.NewLine +
                Environment.NewLine +
                "Slab Designer" + Environment.NewLine +
                "  - Extracts ETABS slab panels, designs per IS 456:2000, and can push optimized thicknesses.";
            return 0;
        }

        public void Main(ref cSapModel sapModel, ref cPluginCallback pluginCallback)
        {
            var aForm = new Form1();
            try
            {
                aForm.SetSapModel(ref sapModel, ref pluginCallback);
                aForm.Show();
            }
            catch (Exception ex)
            {
                errorCode = 1;
                MessageBox.Show("Plugin startup error:\n" + ex.Message);
                try { pluginCallback.Finish(errorCode); } catch { }
            }
        }
    }
}
