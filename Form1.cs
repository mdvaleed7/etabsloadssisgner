using System;
using System.Collections.Generic;
using System.Windows.Forms;
using ETABSv1;

namespace CSiNET8PluginExample1
{
    public partial class Form1 : Form
    {

        private cSapModel _sapModel;
        private cPluginCallback _pluginCallback;
        private int errorCode = 0; 
        private List<SlabData> _currentSlabs = new List<SlabData>();

        public Form1()
        {
            InitializeComponent();
            FormClosing += Form1_FormClosing;
            SetupGrid();
        }

        private void SetupGrid()
        {
            dataGridView1.Columns.Add("Name", "Slab Name");
            dataGridView1.Columns.Add("Type", "Type");
            dataGridView1.Columns.Add("Dimensions", "Lx x Ly (mm)");
            dataGridView1.Columns.Add("Thickness", "Thickness (mm)");
            dataGridView1.Columns.Add("AstXBot", "Ast X Bot");
            dataGridView1.Columns.Add("BarsXBot", "Bars X Bot");
            dataGridView1.Columns.Add("Status", "Design Status");
            
            dataGridView1.Columns["Dimensions"].Width = 120;
            
            dataGridView1.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dataGridView1.MultiSelect = false;
            dataGridView1.ReadOnly = true;
        }

        public void SetSapModel(ref cSapModel inSapModel, ref cPluginCallback inPluginCallback)
        {
            _sapModel = inSapModel;
            _pluginCallback = inPluginCallback;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_pluginCallback != null)
            {
                _pluginCallback.Finish(errorCode);
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (_sapModel == null)
            {
                MessageBox.Show("ETABS model is not attached.");
                return;
            }

            try
            {
                dataGridView1.Rows.Clear();
                
                var extractor = new EtabsDataExtractor(_sapModel);
                _currentSlabs = extractor.ExtractSlabs();

                foreach (var slab in _currentSlabs)
                {
                    SlabDesignEngine.DesignSlab(slab);
                    
                    dataGridView1.Rows.Add(
                        slab.Name,
                        slab.Type.ToString(),
                        $"{slab.Lx:F0} x {slab.Ly:F0}",
                        slab.Thickness.ToString(),
                        $"{slab.Ast_x_bot:F0}",
                        slab.Bars_x_bot,
                        slab.DesignStatus
                    );
                }
                
                if (_currentSlabs.Count == 0)
                {
                    MessageBox.Show("No slabs found in the ETABS model.");
                }
            }
            catch (Exception ex)
            {
                errorCode = 2; 
                MessageBox.Show("The following error occurred:" + Environment.NewLine + ex.Message);
            }
        }

        private void dataGridView1_SelectionChanged(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count > 0)
            {
                int index = dataGridView1.SelectedRows[0].Index;
                if (index >= 0 && index < _currentSlabs.Count)
                {
                    var slab = _currentSlabs[index];
                    lblSlabName.Text = $"Slab: {slab.Name}";
                    lblThickness.Text = $"Req. Thickness: {slab.Thickness} mm";
                    
                    // We parse the deflection out of the notes for display, or better yet, we should add properties to SlabData.
                    // For now, we display the notes in the status/deflection labels
                    lblStatus.Text = $"Status: {slab.DesignStatus}";
                    
                    // Basic parsing of the notes string which looks like: "Required thickness for deflection: ... mm. Deflection Xmm <= Ymm. Moments: Mx+=Z kNm"
                    string notes = slab.Notes;
                    int deflStart = notes.IndexOf("Deflection");
                    int deflEnd = notes.IndexOf("Moments");
                    if (deflStart >= 0 && deflEnd > deflStart)
                    {
                        lblDeflection.Text = notes.Substring(deflStart, deflEnd - deflStart).Trim();
                    }
                    else
                    {
                        lblDeflection.Text = "Deflection: " + slab.Notes;
                    }
                    
                    // Show all bar selections in the notes string or we can just append it to lblDeflection for now
                    lblDeflection.Text += $"\nBot: X={slab.Bars_x_bot}, Y={slab.Bars_y_bot} | Top: X={slab.Bars_x_top}, Y={slab.Bars_y_top}";
                    
                    btnPushToEtabs.Enabled = true;
                    btnPushToEtabs.Tag = slab; // Store the slab object in the button for easy access
                }
            }
            else
            {
                lblSlabName.Text = "Select a Slab";
                lblThickness.Text = "Thickness: -";
                lblDeflection.Text = "Deflection: -";
                lblStatus.Text = "Status: -";
                btnPushToEtabs.Enabled = false;
            }
        }

        private void btnPushToEtabs_Click(object sender, EventArgs e)
        {
            if (btnPushToEtabs.Tag is SlabData slab)
            {
                if (_sapModel != null)
                {
                    var updater = new EtabsModelUpdater(_sapModel);
                    updater.PushOptimizedThickness(slab);
                }
                else
                {
                    MessageBox.Show("ETABS model is not attached.", "Error");
                }
            }
        }
    }
}
