using ETABSv1;
using System;
using System.Windows.Forms;

namespace CSiNET8PluginExample1
{
    // Implementing the cPluginContract interface is not required, however
    // it is recommended to ensure that the required cPlugin methods are created correctly.
    // Do not implement the Info or Main methods explicitly,
    // i.e. their method signatures are correct as is
    public class cPlugin : cPluginContract
    {
        private static string _modality = "Non-Modal";
        private static string _versionString = "1.0";
        private int errorCode = 0; // default return code is no error

        public int Info(ref string Text)
        {
            try
            {
                // CORRECTION (cplugin.cs): replaced CSI boilerplate with an accurate
                // description of this plugin's actual purpose and capabilities.
                Text = "IS 456 Slab Design Plugin v" + _versionString + Environment.NewLine +
                       "Developed by: Advatech Structural Engineers" + Environment.NewLine +
                       Environment.NewLine +
                       "Extracts all area (slab) objects from the open ETABS model, " +
                       "performs IS 456:2000 flexural design and deflection optimisation " +
                       "(Annex C), and writes the optimised slab thickness back to " +
                       "ETABS as a new shell-section property." + Environment.NewLine +
                       Environment.NewLine +
                       "Supported slab types: One-Way, Two-Way (Table 26), " +
                       "Cantilever, Flat Slab." + Environment.NewLine +
                       "Deflection check: IS 456 Annex C (short-term + shrinkage + creep)." +
                       Environment.NewLine +
                       "Load combinations: IS 875 Part 5 (1.5 DL+LL).";
            }
            catch (Exception)
            {
            }

            return 0;
        }

        public void Main(ref cSapModel sapModel, ref cPluginCallback pluginCallback)
        {
            var aForm = new Form1();

            try
            {
                aForm.SetSapModel(ref sapModel, ref pluginCallback);

                if (string.Compare(_modality, "Non-Modal", true) == 0)
                {
                    // Non-modal form, allows graphics refresh operations in CSI program, 
                    // but Main will return to CSI program before the form is closed.
                    aForm.Show();
                }
                else
                {
                    // Modal form, will not return to CSI program until form is closed,
                    // but may cause errors when refreshing the view.
                    aForm.ShowDialog();
                }

                // It is very important to call pluginCallback.Finish(errorCode) when the form closes, !!!
                // otherwise, the CSI program will wait and be hung !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
                // This must be done inside the closing event for the form itself, not here !!!!!!!!!!!!!!

                // If you have only algorithmic code here without any forms, 
                // then call pluginCallback.Finish(errorCode) here before returning to the CSI program

                // errorCode = 0 indicates that the plugin completed successfully
                // ie pluginCallback.Finish(0)
                // errorCode = (Any non-zero integer) indicates that the plugin closed with an error
                // ie pluginCallback.Finish(1)
                // If an error occurs, the errorCode value will be displayed
                // to the plugin end-user in a message box, for debugging purposes.

                // If your code will run for more than a few seconds, you should exercise
                // the Windows messaging loop to keep the program responsive. You may 
                // also want to provide an opportunity for the user to cancel operations.

            }
            catch (Exception ex)
            {
                errorCode = 1;
                MessageBox.Show("The following error terminated the plugin:" + Environment.NewLine + ex.Message);

                // call Finish to inform the CSI program that the plugin has terminated
                try
                {
                    pluginCallback.Finish(errorCode); // error code 1 will be visible to plugin end-user for debugging purposes
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
