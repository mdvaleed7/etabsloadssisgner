using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using ETABSv1;

namespace CSiNET8PluginExample1
{
    /// <summary>
    /// Main UI: extracts slabs, designs them, displays them, and pushes
    /// optimised thicknesses back to ETABS.
    ///
    /// PATCH NOTES (v2):
    ///  • Reads design inputs (Fy, cover, bar Ø, fck fallback) from the new
    ///    panelInputs controls and feeds them into EtabsDataExtractor.
    ///  • "Push ALL" now uses the silent updater and shows a single summary
    ///    popup at the end (one RefreshView call).
    ///  • Per-slab detail panel now also displays material grades.
    /// </summary>
    public partial class Form1 : Form
    {
        private cSapModel? _sapModel;
        private cPluginCallback? _pluginCallback;
        private int errorCode = 0;
        private List<SlabData> _currentSlabs = new List<SlabData>();

        // Extra controls created at run-time
        private Button btnPushAllToEtabs = null!;
        private Label  lblAstTop          = null!;
        private Label  lblAstBot          = null!;
        private Label  lblMaterials       = null!;
        private Label  lblFlatSlabGeom    = null!;
        private Label  lblPunchingShear   = null!;

        public Form1()
        {
            InitializeComponent();
            FormClosing += Form1_FormClosing;
            SetupGrid();
            SetupExtendedUI();
        }

        private void SetupExtendedUI()
        {
            btnPushAllToEtabs = new Button
            {
                Location = new System.Drawing.Point(15, 245),
                Size     = new System.Drawing.Size(265, 40),
                Text     = "Push ALL Optimized Thicknesses",
                Enabled  = false
            };
            btnPushAllToEtabs.Click += btnPushAllToEtabs_Click;
            panelProperties.Controls.Add(btnPushAllToEtabs);

            lblMaterials     = new Label { Location = new System.Drawing.Point(15, 150), Size = new System.Drawing.Size(265, 40), Text = "fck / fy: -" };
            lblAstTop        = new Label { Location = new System.Drawing.Point(15, 290), Size = new System.Drawing.Size(265, 35), Text = "Top Steel:\n-" };
            lblAstBot        = new Label { Location = new System.Drawing.Point(15, 330), Size = new System.Drawing.Size(265, 35), Text = "Bottom Steel:\n-" };
            lblFlatSlabGeom  = new Label { Location = new System.Drawing.Point(15, 370), Size = new System.Drawing.Size(265, 15), Text = "Geometry: -", Visible = false };
            lblPunchingShear = new Label { Location = new System.Drawing.Point(15, 388), Size = new System.Drawing.Size(265, 15), Text = "Punching: -", Visible = false };

            panelProperties.Controls.Add(lblMaterials);
            panelProperties.Controls.Add(lblAstTop);
            panelProperties.Controls.Add(lblAstBot);
            panelProperties.Controls.Add(lblFlatSlabGeom);
            panelProperties.Controls.Add(lblPunchingShear);
        }

        private void SetupGrid()
        {
            dataGridView1.Columns.Add("Name",      "Slab");
            dataGridView1.Columns.Add("Type",      "Type");
            dataGridView1.Columns.Add("Dim",       "Lx × Ly (mm)");
            dataGridView1.Columns.Add("Thickness", "D (mm)");
            dataGridView1.Columns.Add("AstXBot",   "Ast X (mm²/m)");
            dataGridView1.Columns.Add("BarsXBot",  "Bars X (governing)");
            dataGridView1.Columns.Add("Status",    "Status");

            dataGridView1.Columns["Dim"].Width = 120;
            dataGridView1.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dataGridView1.MultiSelect   = false;
            dataGridView1.ReadOnly      = true;
        }

        public void SetSapModel(ref cSapModel inSapModel, ref cPluginCallback inPluginCallback)
        {
            _sapModel        = inSapModel;
            _pluginCallback  = inPluginCallback;
        }

        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            _pluginCallback?.Finish(errorCode);
        }

        private void button1_Click(object? sender, EventArgs e)
        {
            if (_sapModel == null) { MessageBox.Show("ETABS model is not attached."); return; }

            try
            {
                dataGridView1.Rows.Clear();

                // PATCH: pull user inputs and pass to the extractor
                double fyVal = ParseFy(cmbFy.SelectedItem?.ToString() ?? "Fe500 (500)");
                var extractor = new EtabsDataExtractor(_sapModel)
                {
                    UserFy         = fyVal,
                    UserCover      = (double)numCover.Value,
                    UserBarDiaMain = (double)numBarMain.Value,
                    UserBarDiaDist = (double)numBarDist.Value,
                    DefaultFck     = (double)numFckOverride.Value
                };

                _currentSlabs = extractor.ExtractSlabs();

                foreach (var slab in _currentSlabs)
                {
                    SlabDesignEngine.DesignSlab(slab);

                    // PATCH v3: for a cantilever the dominant steel is at the
                    // TOP at the root — display that instead of the (zero)
                    // bottom steel so the grid is meaningful.
                    bool topGoverns = slab.Type == SlabType.Cantilever
                                   || slab.Ast_x_top > slab.Ast_x_bot;
                    double astDisp  = topGoverns ? slab.Ast_x_top : slab.Ast_x_bot;
                    string barsDisp = topGoverns ? slab.Bars_x_top : slab.Bars_x_bot;

                    dataGridView1.Rows.Add(
                        slab.Name, slab.Type.ToString(),
                        $"{slab.Lx:F0} × {slab.Ly:F0}",
                        slab.Thickness.ToString("F0"),
                        $"{astDisp:F0}",
                        barsDisp,
                        slab.DesignStatus);
                }

                if (_currentSlabs.Count == 0)
                    MessageBox.Show("No slabs found in the ETABS model.");
                else
                    btnPushAllToEtabs.Enabled = true;
            }
            catch (Exception ex)
            {
                errorCode = 2;
                MessageBox.Show("The following error occurred:" + Environment.NewLine + ex.Message);
            }
        }

        private static double ParseFy(string item)
        {
            // "Fe500 (500)" → 500
            if (item.Contains("(") && item.Contains(")"))
            {
                int a = item.IndexOf('('); int b = item.IndexOf(')');
                if (b > a && double.TryParse(item.Substring(a + 1, b - a - 1), out double v)) return v;
            }
            return 500;
        }

        private void dataGridView1_SelectionChanged(object? sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count == 0) { ClearDetails(); return; }
            int index = dataGridView1.SelectedRows[0].Index;
            if (index < 0 || index >= _currentSlabs.Count) { ClearDetails(); return; }

            var slab = _currentSlabs[index];
            lblSlabName.Text  = $"Slab: {slab.Name}";
            lblThickness.Text = $"Req. Thickness: {slab.Thickness:F0} mm";
            lblStatus.Text    = $"Status: {slab.DesignStatus}";
            lblDeflection.Text= "Notes: " + slab.Notes;

            lblMaterials.Text = $"fck = {slab.Fck:F0} N/mm² (material: '{slab.MaterialName}')\n" +
                                $"fy  = {slab.Fy:F0} N/mm²,  cover = {slab.Cover:F0} mm,  Ø = {slab.BarDiaMain:F0}/{slab.BarDiaDist:F0} mm";
            lblAstTop.Text = $"Top Steel:\nX: {slab.Bars_x_top}\nY: {slab.Bars_y_top}";
            lblAstBot.Text = $"Bot Steel:\nX: {slab.Bars_x_bot}\nY: {slab.Bars_y_bot}";

            bool isFlat = slab.Type == SlabType.FlatSlab;
            lblFlatSlabGeom.Visible = lblPunchingShear.Visible = isFlat;
            if (isFlat)
            {
                lblFlatSlabGeom.Text = $"Cols: {slab.c1:F0}×{slab.c2:F0} mm" +
                                       (slab.HasDrop ? $" | Drop: {slab.DropDepth:F0}mm" : "");
                lblPunchingShear.Text = $"Punching: {slab.PunchingShearStatus}";
            }

            btnPushToEtabs.Enabled = true;
            btnPushToEtabs.Tag = slab;
        }

        private void ClearDetails()
        {
            lblSlabName.Text   = "Select a Slab";
            lblThickness.Text  = "Thickness: -";
            lblDeflection.Text = "Notes: -";
            lblStatus.Text     = "Status: -";
            lblMaterials.Text  = "fck / fy: -";
            lblAstTop.Text     = "Top Steel:\n-";
            lblAstBot.Text     = "Bottom Steel:\n-";
            lblFlatSlabGeom.Visible = lblPunchingShear.Visible = false;
            btnPushToEtabs.Enabled = false;
        }

        private void btnPushToEtabs_Click(object? sender, EventArgs e)
        {
            if (btnPushToEtabs.Tag is SlabData slab && _sapModel != null)
            {
                var updater = new EtabsModelUpdater(_sapModel);
                updater.PushOptimizedThickness(slab);
            }
            else MessageBox.Show("ETABS model is not attached.", "Error");
        }

        private void btnPushAllToEtabs_Click(object? sender, EventArgs e)
        {
            if (_sapModel == null) { MessageBox.Show("ETABS model is not attached.", "Error"); return; }

            var updater = new EtabsModelUpdater(_sapModel);
            int ok = 0, fail = 0;
            var failures = new StringBuilder();

            foreach (var slab in _currentSlabs)
            {
                var (success, msg) = updater.PushOptimizedThicknessSilent(slab);
                if (success) ok++;
                else { fail++; failures.AppendLine(" • " + msg); }
            }

            updater.TryRefreshView();

            string summary = $"Pushed {ok} slab(s) successfully.";
            if (fail > 0) summary += $"\n{fail} slab(s) FAILED:\n{failures}";
            MessageBox.Show(summary,
                fail == 0 ? "Push Complete" : "Push Completed with Errors",
                MessageBoxButtons.OK,
                fail == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
    }
}
