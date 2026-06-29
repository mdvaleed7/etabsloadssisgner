using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ETABSv1;
using EtabsWindAutomation;

namespace CSiNET8PluginExample1
{
    public partial class Form1 : Form
    {
        private cSapModel _sapModel;
        private cPluginCallback _pluginCallback;

        // ── Creators / helpers (lazily instantiated after _sapModel is set) ───
        private LoadPatternCreator    _patCreator;
        private LoadCaseCreator       _caseCreator;
        private LoadAssigner          _assigner;
        private LoadCombinationCreator _comboCreator;

        private TabPage _tabWind;
        private RichTextBox _rtbWindLog;

        private TabPage _tabSlabs;
        private DataGridView _dgvSlabs;
        private ComboBox _cmbSlabFy;
        private NumericUpDown _numSlabCover;
        private ComboBox _cmbSlabBarMain;
        private ComboBox _cmbSlabBarDist;
        private NumericUpDown _numSlabFckOverride;
        private Button _btnPushSelectedSlab;
        private Button _btnPushAllSlabs;
        private Label _lblSlabName;
        private Label _lblSlabThickness;
        private Label _lblSlabStatus;
        private Label _lblSlabMaterials;
        private Label _lblSlabTopSteel;
        private Label _lblSlabBottomSteel;
        private Label _lblSlabNotes;
        private readonly List<SlabData> _currentSlabs = new List<SlabData>();

        public Form1()
        {
            InitializeComponent();
            NormalizeExistingUiText();
            SetupIntegratedTabs();
            this.FormClosing += Form1_FormClosing;
        }

        public void SetSapModel(ref cSapModel sapModel, ref cPluginCallback callback)
        {
            _sapModel      = sapModel;
            _pluginCallback = callback;

            _patCreator   = new LoadPatternCreator(_sapModel);
            _caseCreator  = new LoadCaseCreator(_sapModel);
            _assigner     = new LoadAssigner(_sapModel);
            _comboCreator = new LoadCombinationCreator(_sapModel);



            Log("Plugin connected to ETABS model.");
            Log($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        // ── Config tab: zone changed → update R hint ─────────────────────────
        private void OnZoneChanged()
        {
            // Log seismic summary preview in the log tab
            try
            {
                var cfg = BuildConfig();
                Log($"Zone updated → {SeismicHelper.GetSummary(cfg)}");
            }
            catch { }
        }

        private void OnSysChanged()
        {
            // Auto-fill the R text box when system changes
            double r = cbSys.SelectedIndex switch
            {
                0 => 3.0,   // RC OMRF
                1 => 5.0,   // RC SMRF
                2 => 4.0,   // RC SW + OMRF
                3 => 5.0,   // RC SW + SMRF
                4 => 3.0,   // Steel OMRF
                5 => 5.0,   // Steel SMRF
                6 => 4.0,   // Steel CBF
                7 => 1.5,   // URM
                _ => 3.0
            };
            txtR.Text = r.ToString("F1");
        }

        private void OnOccChanged()
        {
            double[] llLookup = { 2.0, 4.0, 4.0, 4.0, 5.0, 7.5, 12.0, 4.0, 4.0 };
            if (cbOcc.SelectedIndex < llLookup.Length)
                txtLL.Text = llLookup[cbOcc.SelectedIndex].ToString("F1");
        }

        // ── Build BuildingConfig from form values ─────────────────────────────
        private BuildingConfig BuildConfig()
        {
            // Parse R factor
            if (!double.TryParse(txtR.Text, out double rVal)) rVal = 5.0;
            if (!double.TryParse(txtDamp.Text, out double dampPct)) dampPct = 5.0;
            if (!int.TryParse(txtModes.Text, out int nModes)) nModes = 12;
            if (!double.TryParse(txtLL.Text, out double ll)) ll = 4.0;
            if (!double.TryParse(txtSDL.Text, out double sdl)) sdl = 1.5;
            if (!double.TryParse(txtRoofLL.Text, out double roofLL)) roofLL = 1.5;
            if (!double.TryParse(txtCladding.Text, out double clad)) clad = 8.0;
            if (!double.TryParse(txtParapet.Text, out double par)) par = 2.0;

            SeismicZone zone = cbZone.SelectedIndex switch
            {
                0 => SeismicZone.II, 1 => SeismicZone.III,
                2 => SeismicZone.IV, 3 => SeismicZone.V,
                _ => SeismicZone.III
            };
            SiteClass soil = cbSoil.SelectedIndex switch
            {
                0 => SiteClass.Type_I_Hard, 1 => SiteClass.Type_II_Medium,
                2 => SiteClass.Type_III_Soft, _ => SiteClass.Type_II_Medium
            };
            ImportanceFactor imp = cbImp.SelectedIndex switch
            {
                0 => ImportanceFactor.Cat_III_Normal, 1 => ImportanceFactor.Cat_II_Important,
                2 => ImportanceFactor.Cat_I_Critical, _ => ImportanceFactor.Cat_III_Normal
            };
            StructuralSystem sys = cbSys.SelectedIndex switch
            {
                0 => StructuralSystem.RC_OMRF,           1 => StructuralSystem.RC_SMRF,
                2 => StructuralSystem.RC_ShearWall_OMRF, 3 => StructuralSystem.RC_ShearWall_SMRF,
                4 => StructuralSystem.Steel_OMRF,        5 => StructuralSystem.Steel_SMRF,
                6 => StructuralSystem.Steel_CBF,         7 => StructuralSystem.UnreinforcedMasonry,
                _ => StructuralSystem.RC_SMRF
            };

            return new BuildingConfig
            {
                Zone           = zone,
                SoilType       = soil,
                Importance     = imp,
                StructSystem   = sys,
                R              = rVal,
                DampingRatio   = dampPct / 100.0,
                NumberOfModes  = Math.Max(6, nModes),
                LiveLoad       = ll,
                SDL            = sdl,
                RoofLiveLoad   = roofLL,
                CladdingLoad_kNm = clad,
                ParapetLoad_kNm  = par,
                // Pattern names from the bottom row of the Config tab
                PatternDead    = string.IsNullOrWhiteSpace(txtPDead.Text) ? "DEAD" : txtPDead.Text.Trim(),
                PatternSDL     = string.IsNullOrWhiteSpace(txtPSDL.Text)  ? "SDL"  : txtPSDL.Text.Trim(),
                PatternLive    = string.IsNullOrWhiteSpace(txtPLive.Text) ? "LIVE" : txtPLive.Text.Trim(),
                PatternEQX     = string.IsNullOrWhiteSpace(txtPEQX.Text)  ? "EQX"  : txtPEQX.Text.Trim(),
                PatternEQY     = string.IsNullOrWhiteSpace(txtPEQY.Text)  ? "EQY"  : txtPEQY.Text.Trim(),
                CaseEQX        = string.IsNullOrWhiteSpace(txtPEQX.Text)  ? "EQX"  : txtPEQX.Text.Trim(),
                CaseEQY        = string.IsNullOrWhiteSpace(txtPEQY.Text)  ? "EQY"  : txtPEQY.Text.Trim(),
            };
        }

        // ── Load Setup steps ──────────────────────────────────────────────────
        private void RunStep1()
        {
            if (_sapModel == null) { LogError("Not connected to ETABS"); return; }
            mainTabs.SelectedTab = tabLoads;
            try
            {
                var cfg = BuildConfig();
                Log("\n" + _patCreator.CreateAllPatterns(cfg));
            }
            catch (Exception ex) { LogError($"Step 1 failed: {ex.Message}"); }
        }

        private void RunStep2()
        {
            if (_sapModel == null) { LogError("Not connected to ETABS"); return; }
            mainTabs.SelectedTab = tabLoads;
            try
            {
                var cfg = BuildConfig();
                Log("\n" + _caseCreator.CreateAllCases(cfg));
            }
            catch (Exception ex) { LogError($"Step 2 failed: {ex.Message}"); }
        }

        private void RunStep3()
        {
            if (_sapModel == null) { LogError("Not connected to ETABS"); return; }
            mainTabs.SelectedTab = tabLoads;
            try
            {
                var cfg = BuildConfig();
                Log("\n" + _assigner.AssignAllLoads(cfg));
            }
            catch (Exception ex) { LogError($"Step 3 failed: {ex.Message}"); }
        }

        private void RunStep4()
        {
            if (_sapModel == null) { LogError("Not connected to ETABS"); return; }
            mainTabs.SelectedTab = tabLoads;
            try
            {
                var cfg = BuildConfig();
                Log("\n" + _comboCreator.CreateAllCombinations(cfg));
            }
            catch (Exception ex) { LogError($"Step 4 failed: {ex.Message}"); }
        }

        private void RunAllSteps()
        {
            if (_sapModel == null) { LogError("Not connected to ETABS"); return; }
            mainTabs.SelectedTab = tabLoads;
            Log("\n══════════════════════════════════════════");
            Log($"  IS 875 + IS 1893:2016 Load Automation");
            Log($"  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Log("══════════════════════════════════════════");
            try
            {
                var cfg = BuildConfig();
                Log("\n" + _patCreator.CreateAllPatterns(cfg));
                Log("\n" + _caseCreator.CreateAllCases(cfg));
                Log("\n" + _assigner.AssignAllLoads(cfg));
                Log("\n" + _comboCreator.CreateAllCombinations(cfg));
                Log("\n✔  All steps complete. Refresh ETABS view and verify.");
                Log("   IMPORTANT: Set Mass Source (Define > Mass Source) for seismic weight.");
                Log("   Apply IS 875 Part 3 wind pressures to WLX/WLY cases manually.");
            }
            catch (Exception ex)
            {
                LogError($"Run failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void NormalizeExistingUiText()
        {
            Text = "Advatech ETABS Automation Plugin";
            tabConfig.Text = "Building Configuration";
            tabLoads.Text = "Load Definition";

            cbSoil.Items.Clear();
            cbSoil.Items.AddRange(new[] { "I - Hard / Rock", "II - Medium", "III - Soft" });
            cbSoil.SelectedIndex = 1;

            cbImp.Items.Clear();
            cbImp.Items.AddRange(new[] { "1.0 - Normal", "1.2 - Important", "1.5 - Critical" });
            cbImp.SelectedIndex = 0;

            cbSys.Items.Clear();
            cbSys.Items.AddRange(new[]
            {
                "RC OMRF (R=3.0)", "RC SMRF IS 13920 (R=5.0)",
                "RC Shear Wall + OMRF (R=4.0)", "RC Shear Wall + SMRF (R=5.0)",
                "Steel OMRF (R=3.0)", "Steel SMRF (R=5.0)",
                "Steel CBF (R=4.0)", "Unreinforced Masonry (R=1.5)"
            });
            cbSys.SelectedIndex = 1;

            cbOcc.Items.Clear();
            cbOcc.Items.AddRange(new[]
            {
                "Residential (2.0 kN/sq.m)", "Office General (4.0 kN/sq.m)",
                "Office Lobby (4.0 kN/sq.m)", "Commercial/Retail (4.0 kN/sq.m)",
                "Assembly Hall (5.0 kN/sq.m)", "Storage Light (7.5 kN/sq.m)",
                "Storage Heavy (12.0 kN/sq.m)", "Corridor (4.0 kN/sq.m)",
                "Custom"
            });
            cbOcc.SelectedIndex = 1;

            btnStep1Patterns.Text = "1. Create Load Patterns";
            btnStep2Cases.Text = "2. Create Load Cases";
            btnStep3Assign.Text = "3. Assign Gravity Loads";
            btnStep4Combos.Text = "4. Create Load Combinations";
            btnRunAll.Text = "Run All Load Steps";
            rtbLog.Text = "Ready. Configure parameters, then run the required workflow.\r\n";
        }

        private void SetupIntegratedTabs()
        {
            SetupWindTab();
            SetupSlabTab();
            mainTabs.TabPages.Add(_tabWind);
            mainTabs.TabPages.Add(_tabSlabs);
        }

        private void SetupWindTab()
        {
            _tabWind = new TabPage("Wind Automation");
            _tabWind.Padding = new Padding(10);

            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 92
            };

            var btnRunWind = new Button
            {
                Text = "Run IS 875 Wind Automation",
                Location = new Point(10, 12),
                Size = new Size(260, 40)
            };
            btnRunWind.Click += (s, e) => RunWindAutomation();

            var lblSettings = new Label
            {
                Text = "Settings file copied beside DLL: wind-settings.json",
                Location = new Point(10, 60),
                AutoSize = true
            };

            topPanel.Controls.Add(btnRunWind);
            topPanel.Controls.Add(lblSettings);

            _rtbWindLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9f),
                ReadOnly = true,
                BackColor = Color.FromArgb(22, 27, 34),
                ForeColor = Color.FromArgb(201, 209, 217),
                Text = "Ready to run wind automation.\r\n"
            };

            _tabWind.Controls.Add(_rtbWindLog);
            _tabWind.Controls.Add(topPanel);
        }

        private void SetupSlabTab()
        {
            _tabSlabs = new TabPage("Slab Designer");
            _tabSlabs.Padding = new Padding(10);

            var rightPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 370,
                Padding = new Padding(8)
            };

            var leftPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 0, 10, 0)
            };

            _dgvSlabs = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            _dgvSlabs.Columns.Add("Name", "Slab");
            _dgvSlabs.Columns.Add("Type", "Type");
            _dgvSlabs.Columns.Add("Dim", "Lx x Ly (mm)");
            _dgvSlabs.Columns.Add("Thickness", "D (mm)");
            _dgvSlabs.Columns.Add("Ast", "Ast (mm2/m)");
            _dgvSlabs.Columns.Add("Bars", "Bars");
            _dgvSlabs.Columns.Add("Status", "Status");
            _dgvSlabs.SelectionChanged += (s, e) => SlabSelectionChanged();

            var btnExtractSlabs = new Button
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                Text = "Extract and Design Slabs"
            };
            btnExtractSlabs.Click += (s, e) => ExtractAndDesignSlabs();

            leftPanel.Controls.Add(_dgvSlabs);
            leftPanel.Controls.Add(btnExtractSlabs);

            BuildSlabSidePanel(rightPanel);

            _tabSlabs.Controls.Add(leftPanel);
            _tabSlabs.Controls.Add(rightPanel);
        }

        private void BuildSlabSidePanel(Panel panel)
        {
            var inputGroup = new GroupBox
            {
                Text = "Design Inputs",
                Dock = DockStyle.Top,
                Height = 190
            };

            _cmbSlabFy = MakeSideCombo(inputGroup, "Steel grade fy:", 24,
                new[] { "Fe250 (250)", "Fe415 (415)", "Fe500 (500)", "Fe550 (550)" }, 2);
            _numSlabCover = MakeSideNumber(inputGroup, "Clear cover (mm):", 54, 15, 75, 20);
            _cmbSlabBarMain = MakeSideCombo(inputGroup, "Main bar dia:", 84,
                new[] { "8", "10", "12", "16", "20", "25" }, 1);
            _cmbSlabBarDist = MakeSideCombo(inputGroup, "Dist. bar dia:", 114,
                new[] { "8", "10", "12", "16", "20", "25" }, 0);
            _numSlabFckOverride = MakeSideNumber(inputGroup, "fck fallback:", 144, 15, 60, 25);

            var detailGroup = new GroupBox
            {
                Text = "Selected Slab",
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };

            _lblSlabName = MakeDetailLabel("Select a slab", 24, true);
            _lblSlabThickness = MakeDetailLabel("Thickness: -", 58);
            _lblSlabStatus = MakeDetailLabel("Status: -", 86);
            _lblSlabMaterials = MakeDetailLabel("Materials: -", 114);
            _lblSlabTopSteel = MakeDetailLabel("Top steel: -", 156);
            _lblSlabBottomSteel = MakeDetailLabel("Bottom steel: -", 204);
            _lblSlabNotes = MakeDetailLabel("Notes: -", 252);
            _lblSlabNotes.Size = new Size(330, 100);

            _btnPushSelectedSlab = new Button
            {
                Text = "Push Selected Thickness",
                Location = new Point(14, 365),
                Size = new Size(330, 34),
                Enabled = false
            };
            _btnPushSelectedSlab.Click += (s, e) => PushSelectedSlab();

            _btnPushAllSlabs = new Button
            {
                Text = "Push All Optimized Thicknesses",
                Location = new Point(14, 406),
                Size = new Size(330, 34),
                Enabled = false
            };
            _btnPushAllSlabs.Click += (s, e) => PushAllSlabs();

            detailGroup.Controls.AddRange(new Control[]
            {
                _lblSlabName, _lblSlabThickness, _lblSlabStatus, _lblSlabMaterials,
                _lblSlabTopSteel, _lblSlabBottomSteel, _lblSlabNotes,
                _btnPushSelectedSlab, _btnPushAllSlabs
            });

            panel.Controls.Add(detailGroup);
            panel.Controls.Add(inputGroup);
        }

        private ComboBox MakeSideCombo(GroupBox group, string text, int y, string[] items, int selectedIndex)
        {
            var label = new Label { Text = text, Location = new Point(12, y + 3), Size = new Size(135, 22) };
            var combo = new ComboBox
            {
                Location = new Point(155, y),
                Size = new Size(175, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            combo.Items.AddRange(items);
            combo.SelectedIndex = selectedIndex;
            group.Controls.Add(label);
            group.Controls.Add(combo);
            return combo;
        }

        private NumericUpDown MakeSideNumber(GroupBox group, string text, int y, decimal min, decimal max, decimal value)
        {
            var label = new Label { Text = text, Location = new Point(12, y + 3), Size = new Size(135, 22) };
            var number = new NumericUpDown
            {
                Location = new Point(155, y),
                Size = new Size(175, 24),
                Minimum = min,
                Maximum = max,
                Value = value,
                DecimalPlaces = 0
            };
            group.Controls.Add(label);
            group.Controls.Add(number);
            return number;
        }

        private Label MakeDetailLabel(string text, int y, bool bold = false)
        {
            return new Label
            {
                Text = text,
                Location = new Point(14, y),
                Size = new Size(330, 42),
                Font = bold ? new Font("Segoe UI", 10f, FontStyle.Bold) : new Font("Segoe UI", 9f),
                AutoEllipsis = true
            };
        }

        private void RunWindAutomation()
        {
            if (_sapModel == null)
            {
                AppendWindLog("Not connected to ETABS.");
                return;
            }

            mainTabs.SelectedTab = _tabWind;
            try
            {
                AppendWindLog($"Running wind automation at {DateTime.Now:yyyy-MM-dd HH:mm:ss}...");
                WindPluginRunner.Run(_sapModel);

                var logPath = Path.Combine(AppContext.BaseDirectory, "wind-automation-log.txt");
                var logText = File.Exists(logPath)
                    ? File.ReadAllText(logPath)
                    : "Wind automation completed, but no log file was found.";

                _rtbWindLog.Text = logText;
                _rtbWindLog.ScrollToCaret();
                Log("\n" + logText);
            }
            catch (Exception ex)
            {
                WindPluginRunner.TryWriteFailureLog(ex);
                AppendWindLog("Wind automation failed: " + ex.Message);
                LogError("Wind automation failed: " + ex.Message);
            }
        }

        private void AppendWindLog(string message)
        {
            _rtbWindLog.AppendText(message + "\r\n");
            _rtbWindLog.ScrollToCaret();
        }

        private void ExtractAndDesignSlabs()
        {
            if (_sapModel == null)
            {
                MessageBox.Show("ETABS model is not attached.", "Slab Designer");
                return;
            }

            mainTabs.SelectedTab = _tabSlabs;
            try
            {
                _dgvSlabs.Rows.Clear();
                _currentSlabs.Clear();
                ClearSlabDetails();

                var extractor = new EtabsDataExtractor(_sapModel)
                {
                    UserFy = ParseFy(_cmbSlabFy.SelectedItem?.ToString() ?? "Fe500 (500)"),
                    UserCover = (double)_numSlabCover.Value,
                    UserBarDiaMain = ParseSelectedInt(_cmbSlabBarMain, 10),
                    UserBarDiaDist = ParseSelectedInt(_cmbSlabBarDist, 8),
                    DefaultFck = (double)_numSlabFckOverride.Value
                };

                _currentSlabs.AddRange(extractor.ExtractSlabs());

                foreach (var slab in _currentSlabs)
                {
                    SlabDesignEngine.DesignSlab(slab);

                    bool topGoverns = slab.Type == SlabType.Cantilever || slab.Ast_x_top > slab.Ast_x_bot;
                    double astDisplay = topGoverns ? slab.Ast_x_top : slab.Ast_x_bot;
                    string barsDisplay = topGoverns ? slab.Bars_x_top : slab.Bars_x_bot;

                    int rowIndex = _dgvSlabs.Rows.Add(
                        slab.Name,
                        slab.Type.ToString(),
                        $"{slab.Lx:F0} x {slab.Ly:F0}",
                        slab.Thickness.ToString("F0"),
                        astDisplay.ToString("F0"),
                        barsDisplay,
                        slab.DesignStatus);
                    _dgvSlabs.Rows[rowIndex].Tag = slab;
                }

                _btnPushAllSlabs.Enabled = _currentSlabs.Count > 0;

                if (_currentSlabs.Count == 0)
                {
                    MessageBox.Show("No slabs found in the ETABS model.", "Slab Designer");
                }
                else
                {
                    Log($"Slab designer completed: {_currentSlabs.Count} slab(s) extracted and designed.");
                }
            }
            catch (Exception ex)
            {
                LogError("Slab design failed: " + ex.Message);
                MessageBox.Show("Slab design failed:" + Environment.NewLine + ex.Message, "Slab Designer");
            }
        }

        private static double ParseFy(string item)
        {
            int open = item.IndexOf('(');
            int close = item.IndexOf(')');
            if (open >= 0 && close > open &&
                double.TryParse(item.Substring(open + 1, close - open - 1), out double value))
            {
                return value;
            }

            return 500.0;
        }

        private static int ParseSelectedInt(ComboBox combo, int fallback)
        {
            return int.TryParse(combo.SelectedItem?.ToString(), out int value) ? value : fallback;
        }

        private void SlabSelectionChanged()
        {
            if (_dgvSlabs.SelectedRows.Count == 0 ||
                _dgvSlabs.SelectedRows[0].Tag is not SlabData slab)
            {
                ClearSlabDetails();
                return;
            }

            _lblSlabName.Text = "Slab: " + slab.Name;
            _lblSlabThickness.Text = $"Thickness: {slab.Thickness:F0} mm";
            _lblSlabStatus.Text = "Status: " + slab.DesignStatus;
            _lblSlabMaterials.Text =
                $"fck={slab.Fck:F0} MPa, fy={slab.Fy:F0} MPa, cover={slab.Cover:F0} mm";
            _lblSlabTopSteel.Text = $"Top steel: X {slab.Bars_x_top}; Y {slab.Bars_y_top}";
            _lblSlabBottomSteel.Text = $"Bottom steel: X {slab.Bars_x_bot}; Y {slab.Bars_y_bot}";
            _lblSlabNotes.Text = "Notes: " + slab.Notes;
            _btnPushSelectedSlab.Enabled = true;
        }

        private void ClearSlabDetails()
        {
            _lblSlabName.Text = "Select a slab";
            _lblSlabThickness.Text = "Thickness: -";
            _lblSlabStatus.Text = "Status: -";
            _lblSlabMaterials.Text = "Materials: -";
            _lblSlabTopSteel.Text = "Top steel: -";
            _lblSlabBottomSteel.Text = "Bottom steel: -";
            _lblSlabNotes.Text = "Notes: -";
            _btnPushSelectedSlab.Enabled = false;
        }

        private void PushSelectedSlab()
        {
            if (_sapModel == null)
            {
                MessageBox.Show("ETABS model is not attached.", "Slab Designer");
                return;
            }

            if (_dgvSlabs.SelectedRows.Count == 0 ||
                _dgvSlabs.SelectedRows[0].Tag is not SlabData slab)
            {
                MessageBox.Show("Select a slab first.", "Slab Designer");
                return;
            }

            var updater = new EtabsModelUpdater(_sapModel);
            updater.PushOptimizedThickness(slab);
        }

        private void PushAllSlabs()
        {
            if (_sapModel == null)
            {
                MessageBox.Show("ETABS model is not attached.", "Slab Designer");
                return;
            }

            var updater = new EtabsModelUpdater(_sapModel);
            int ok = 0;
            int fail = 0;
            var failures = new System.Text.StringBuilder();

            foreach (var slab in _currentSlabs)
            {
                var (success, message) = updater.PushOptimizedThicknessSilent(slab);
                if (success)
                {
                    ok++;
                }
                else
                {
                    fail++;
                    failures.AppendLine(message);
                }
            }

            updater.TryRefreshView();

            string summary = $"Pushed {ok} slab(s) successfully.";
            if (fail > 0)
            {
                summary += Environment.NewLine + $"{fail} slab(s) failed:" +
                           Environment.NewLine + failures;
            }

            MessageBox.Show(summary,
                fail == 0 ? "Push Complete" : "Push Completed with Errors",
                MessageBoxButtons.OK,
                fail == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }



        // ── Logging helpers ───────────────────────────────────────────────────
        private void Log(string msg)
        {
            if (rtbLog.InvokeRequired)
                rtbLog.Invoke(new Action(() => Log(msg)));
            else
            {
                rtbLog.AppendText(msg + "\r\n");
                rtbLog.ScrollToCaret();
            }
        }

        private void LogError(string msg)
        {
            if (rtbLog.InvokeRequired)
                rtbLog.Invoke(new Action(() => LogError(msg)));
            else
            {
                int start = rtbLog.TextLength;
                rtbLog.AppendText("  ✘  " + msg + "\r\n");
                rtbLog.Select(start, msg.Length + 6);
                rtbLog.SelectionColor = Color.FromArgb(255, 100, 100);
                rtbLog.SelectionLength = 0;
                rtbLog.ScrollToCaret();
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            try { _pluginCallback?.Finish(0); }
            catch { }
        }
    }
}
