namespace AdvatechEtabsPlugin
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        // ── Control declarations ───────────────────────────────────────────────
        // Tab container
        private System.Windows.Forms.TabControl mainTabs;
        private System.Windows.Forms.TabPage tabConfig;
        private System.Windows.Forms.TabPage tabLoads;

        // ── Tab 0: Building Configuration ─────────────────────────────────────
        private System.Windows.Forms.GroupBox grpSeismic;
        private System.Windows.Forms.Label lblZone, lblSoil, lblImp, lblSys, lblR, lblDamp, lblModes;
        private System.Windows.Forms.ComboBox cbZone, cbSoil, cbImp, cbSys;
        private System.Windows.Forms.TextBox txtR, txtDamp, txtModes;

        private System.Windows.Forms.GroupBox grpGravity;
        private System.Windows.Forms.Label lblOcc, lblLL, lblSDL, lblRoofLL, lblCladding, lblParapet;
        private System.Windows.Forms.ComboBox cbOcc;
        private System.Windows.Forms.TextBox txtLL, txtSDL, txtRoofLL, txtCladding, txtParapet;

        private System.Windows.Forms.GroupBox grpPatNames;
        private System.Windows.Forms.Label lblPDead, lblPSDL, lblPLive, lblPEQX, lblPEQY;
        private System.Windows.Forms.TextBox txtPDead, txtPSDL, txtPLive, txtPEQX, txtPEQY;

        // ── Tab 1: Load Definition ─────────────────────────────────────────────
        private System.Windows.Forms.GroupBox grpSteps;
        private System.Windows.Forms.Button btnStep1Patterns;
        private System.Windows.Forms.Button btnStep2Cases;
        private System.Windows.Forms.Button btnStep3Assign;
        private System.Windows.Forms.Button btnStep4Combos;
        private System.Windows.Forms.Button btnRunAll;
        private System.Windows.Forms.RichTextBox rtbLog;

        // ────────────────────────────────────────────────────────────────────────
        private void InitializeComponent()
        {
            int PAD = 10; int ROW = 28; int LBL_W = 160; int CTL_W = 200; int GRP_W = 440;

            // ── Main form ─────────────────────────────────────────────────────
            this.Text = "IS 456 / IS 875 / IS 1893:2016 — ETABS Automation Plugin";
            this.Size = new System.Drawing.Size(1050, 720);
            this.MinimumSize = new System.Drawing.Size(900, 600);
            this.Font = new System.Drawing.Font("Segoe UI", 9f);

            // ── TabControl ────────────────────────────────────────────────────
            mainTabs = new System.Windows.Forms.TabControl();
            mainTabs.Dock = System.Windows.Forms.DockStyle.Fill;
            mainTabs.Font = new System.Drawing.Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Regular);

            tabConfig = new System.Windows.Forms.TabPage("🏗  Building Configuration");
            tabLoads  = new System.Windows.Forms.TabPage("⚡  Load Definition");
            mainTabs.TabPages.AddRange(new[] { tabConfig, tabLoads });

            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            // TAB 0 — Building Configuration
            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            tabConfig.AutoScroll = true;
            int cx = PAD, cy = PAD;

            // ── Seismic group ─────────────────────────────────────────────────
            grpSeismic = MakeGroup("Seismic Parameters  (IS 1893:2016)", cx, cy, GRP_W, 240);
            tabConfig.Controls.Add(grpSeismic);
            int gx = PAD, gy = 22;

            lblZone = MakeLbl("Zone:", gx, gy);
            cbZone = MakeCmb(new[]{"II","III","IV","V"}, gx+LBL_W, gy, 100);
            cbZone.SelectedIndex = 1; // default Zone III
            cbZone.SelectedIndexChanged += (s,e) => OnZoneChanged();
            grpSeismic.Controls.AddRange(new System.Windows.Forms.Control[]{ lblZone, cbZone });

            gy += ROW;
            lblSoil = MakeLbl("Site Soil Type:", gx, gy);
            cbSoil = MakeCmb(new[]{"I — Hard / Rock","II — Medium","III — Soft"}, gx+LBL_W, gy, CTL_W);
            cbSoil.SelectedIndex = 1;
            grpSeismic.Controls.AddRange(new System.Windows.Forms.Control[]{ lblSoil, cbSoil });

            gy += ROW;
            lblImp = MakeLbl("Importance Factor I:", gx, gy);
            cbImp = MakeCmb(new[]{"1.0 — Normal (residential/office)","1.2 — Important (schools/halls)","1.5 — Critical (hospitals/fire)"}, gx+LBL_W, gy, 240);
            cbImp.SelectedIndex = 0;
            grpSeismic.Controls.AddRange(new System.Windows.Forms.Control[]{ lblImp, cbImp });

            gy += ROW;
            lblSys = MakeLbl("Structural System:", gx, gy);
            cbSys = MakeCmb(new[]{
                "RC OMRF (R=3.0)","RC SMRF IS 13920 (R=5.0)",
                "RC Shear Wall + OMRF (R=4.0)","RC Shear Wall + SMRF (R=5.0)",
                "Steel OMRF (R=3.0)","Steel SMRF (R=5.0)",
                "Steel CBF (R=4.0)","Unreinforced Masonry (R=1.5)"
            }, gx+LBL_W, gy, 240);
            cbSys.SelectedIndex = 1; // default RC SMRF
            cbSys.SelectedIndexChanged += (s,e) => OnSysChanged();
            grpSeismic.Controls.AddRange(new System.Windows.Forms.Control[]{ lblSys, cbSys });

            gy += ROW;
            lblR = MakeLbl("R factor (override):", gx, gy);
            txtR = MakeTxt("5.0", gx+LBL_W, gy, 80);
            var lblRNote = MakeLbl("← Edit if non-standard", gx+LBL_W+86, gy);
            lblRNote.ForeColor = System.Drawing.Color.Gray;
            grpSeismic.Controls.AddRange(new System.Windows.Forms.Control[]{ lblR, txtR, lblRNote });

            gy += ROW;
            lblDamp = MakeLbl("Damping ratio (%):", gx, gy);
            txtDamp = MakeTxt("5", gx+LBL_W, gy, 60);
            var lblDampNote = MakeLbl("(IS 1893:2016 Cl. 7.2 — default 5%)", gx+LBL_W+66, gy);
            lblDampNote.ForeColor = System.Drawing.Color.Gray;
            grpSeismic.Controls.AddRange(new System.Windows.Forms.Control[]{ lblDamp, txtDamp, lblDampNote });

            gy += ROW;
            lblModes = MakeLbl("No. of modes:", gx, gy);
            txtModes = MakeTxt("12", gx+LBL_W, gy, 60);
            var lblModNote = MakeLbl("(≥12 recommended; IS 1893:2016 Cl. 7.7.5a)", gx+LBL_W+66, gy);
            lblModNote.ForeColor = System.Drawing.Color.Gray;
            grpSeismic.Controls.AddRange(new System.Windows.Forms.Control[]{ lblModes, txtModes, lblModNote });

            // ── Gravity group ─────────────────────────────────────────────────
            cy += 250;
            grpGravity = MakeGroup("Gravity Loads  (IS 875 Parts 1 & 2)", cx, cy, GRP_W, 200);
            tabConfig.Controls.Add(grpGravity);
            gy = 22;

            lblOcc = MakeLbl("Occupancy:", gx, gy);
            cbOcc = MakeCmb(new[]{
                "Residential (2.0 kN/m²)","Office General (4.0 kN/m²)",
                "Office Lobby (4.0 kN/m²)","Commercial/Retail (4.0 kN/m²)",
                "Assembly Hall (5.0 kN/m²)","Storage Light (7.5 kN/m²)",
                "Storage Heavy (12.0 kN/m²)","Corridor (4.0 kN/m²)",
                "Custom"
            }, gx+LBL_W, gy, 220);
            cbOcc.SelectedIndex = 1;
            cbOcc.SelectedIndexChanged += (s,e) => OnOccChanged();
            grpGravity.Controls.AddRange(new System.Windows.Forms.Control[]{ lblOcc, cbOcc });

            gy += ROW;
            lblLL = MakeLbl("Floor Live Load (kN/m²):", gx, gy);
            txtLL = MakeTxt("4.0", gx+LBL_W, gy, 80);
            grpGravity.Controls.AddRange(new System.Windows.Forms.Control[]{ lblLL, txtLL });

            gy += ROW;
            lblSDL = MakeLbl("SDL (kN/m²):", gx, gy);
            txtSDL = MakeTxt("1.5", gx+LBL_W, gy, 80);
            var lblSDLNote = MakeLbl("← finishes + partitions + MEP services", gx+LBL_W+86, gy);
            lblSDLNote.ForeColor = System.Drawing.Color.Gray;
            grpGravity.Controls.AddRange(new System.Windows.Forms.Control[]{ lblSDL, txtSDL, lblSDLNote });

            gy += ROW;
            lblRoofLL = MakeLbl("Roof Live Load (kN/m²):", gx, gy);
            txtRoofLL = MakeTxt("1.5", gx+LBL_W, gy, 80);
            var lblRoofNote = MakeLbl("← accessible; use 0.75 if inaccessible", gx+LBL_W+86, gy);
            lblRoofNote.ForeColor = System.Drawing.Color.Gray;
            grpGravity.Controls.AddRange(new System.Windows.Forms.Control[]{ lblRoofLL, txtRoofLL, lblRoofNote });

            gy += ROW;
            lblCladding = MakeLbl("Cladding Load (kN/m):", gx, gy);
            txtCladding = MakeTxt("8.0", gx+LBL_W, gy, 80);
            var lblClNote = MakeLbl("← exterior beam UDL (glass, ACP, brick)", gx+LBL_W+86, gy);
            lblClNote.ForeColor = System.Drawing.Color.Gray;
            grpGravity.Controls.AddRange(new System.Windows.Forms.Control[]{ lblCladding, txtCladding, lblClNote });

            gy += ROW;
            lblParapet = MakeLbl("Parapet Load (kN/m):", gx, gy);
            txtParapet = MakeTxt("2.0", gx+LBL_W, gy, 80);
            grpGravity.Controls.AddRange(new System.Windows.Forms.Control[]{ lblParapet, txtParapet });

            // ── Pattern names group ───────────────────────────────────────────
            cy += 210;
            grpPatNames = MakeGroup("Load Pattern / Case Names (edit if model already uses different names)", cx, cy, GRP_W+320, 100);
            tabConfig.Controls.Add(grpPatNames);
            gy = 22;
            int pw = 80;
            lblPDead = MakeLbl("Dead:", gx, gy); txtPDead = MakeTxt("DEAD", gx+50, gy, pw);
            lblPSDL  = MakeLbl("SDL:", gx+145, gy); txtPSDL  = MakeTxt("SDL", gx+195, gy, pw);
            lblPLive = MakeLbl("Live:", gx+285, gy); txtPLive = MakeTxt("LIVE", gx+325, gy, pw);
            lblPEQX  = MakeLbl("EQX case:", gx+400, gy); txtPEQX  = MakeTxt("EQX_RS", gx+470, gy, pw);
            lblPEQY  = MakeLbl("EQY case:", gx+560, gy); txtPEQY  = MakeTxt("EQY_RS", gx+630, gy, pw);
            grpPatNames.Controls.AddRange(new System.Windows.Forms.Control[]{
                lblPDead, txtPDead, lblPSDL, txtPSDL, lblPLive, txtPLive,
                lblPEQX, txtPEQX, lblPEQY, txtPEQY });

            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            // TAB 1 — Load Definition
            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            grpSteps = MakeGroup("Run load generation steps in order — or use 'Run All'", PAD, PAD, 350, 215);
            tabLoads.Controls.Add(grpSteps);

            int bx = PAD, by = 22, bw = 310, bh = 36;
            btnStep1Patterns = MakeBtn("① Create Load Patterns (IS 875 Parts 1–3 + IS 1893)", bx, by, bw, bh);
            btnStep1Patterns.Click += (s, e) => RunStep1();
            by += bh + 4;
            btnStep2Cases = MakeBtn("② Create Load Cases  (Modal + RS EQX/EQY)", bx, by, bw, bh);
            btnStep2Cases.Click += (s, e) => RunStep2();
            by += bh + 4;
            btnStep3Assign = MakeBtn("③ Assign Loads  (SDL/LL to slabs, cladding to beams)", bx, by, bw, bh);
            btnStep3Assign.Click += (s, e) => RunStep3();
            by += bh + 4;
            btnStep4Combos = MakeBtn("④ Create Combinations  (IS 456 + IS 875 Part 5)", bx, by, bw, bh);
            btnStep4Combos.Click += (s, e) => RunStep4();
            by += bh + 8;
            btnRunAll = MakeBtn("▶  Run All 4 Steps", bx, by, bw, 40);
            btnRunAll.Font = new System.Drawing.Font("Segoe UI", 10f, System.Drawing.FontStyle.Bold);
            btnRunAll.BackColor = System.Drawing.Color.FromArgb(0, 120, 212);
            btnRunAll.ForeColor = System.Drawing.Color.White;
            btnRunAll.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnRunAll.Click += (s, e) => RunAllSteps();
            grpSteps.Controls.AddRange(new System.Windows.Forms.Control[]{
                btnStep1Patterns, btnStep2Cases, btnStep3Assign, btnStep4Combos, btnRunAll });

            rtbLog = new System.Windows.Forms.RichTextBox();
            rtbLog.Location = new System.Drawing.Point(PAD, grpSteps.Bottom + PAD);
            rtbLog.Size = new System.Drawing.Size(980, 380);
            rtbLog.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left |
                            System.Windows.Forms.AnchorStyles.Right | System.Windows.Forms.AnchorStyles.Bottom;
            rtbLog.Font = new System.Drawing.Font("Consolas", 9f);
            rtbLog.ReadOnly = true;
            rtbLog.BackColor = System.Drawing.Color.FromArgb(22, 27, 34);
            rtbLog.ForeColor = System.Drawing.Color.FromArgb(201, 209, 217);
            rtbLog.Text = "Ready. Configure parameters in 'Building Configuration' then run steps above.\r\n";
            tabLoads.Controls.AddRange(new System.Windows.Forms.Control[]{ grpSteps, rtbLog });

            // ── Assemble form ─────────────────────────────────────────────────
            this.Controls.Add(mainTabs);
        }

        // ── Small helper factories ────────────────────────────────────────────
        private System.Windows.Forms.GroupBox MakeGroup(string text, int x, int y, int w, int h)
        {
            var g = new System.Windows.Forms.GroupBox { Text=text, Location=new System.Drawing.Point(x,y), Size=new System.Drawing.Size(w,h) };
            return g;
        }
        private System.Windows.Forms.Label MakeLbl(string text, int x, int y, bool bold=false)
        {
            return new System.Windows.Forms.Label {
                Text=text, Location=new System.Drawing.Point(x,y+2),
                AutoSize=true,
                Font = bold ? new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold) : null
            };
        }
        private System.Windows.Forms.ComboBox MakeCmb(string[] items, int x, int y, int w)
        {
            var c = new System.Windows.Forms.ComboBox {
                Location=new System.Drawing.Point(x,y), Width=w,
                DropDownStyle=System.Windows.Forms.ComboBoxStyle.DropDownList };
            c.Items.AddRange(items);
            return c;
        }
        private System.Windows.Forms.TextBox MakeTxt(string text, int x, int y, int w)
        {
            return new System.Windows.Forms.TextBox {
                Text=text, Location=new System.Drawing.Point(x,y), Width=w };
        }
        private System.Windows.Forms.Button MakeBtn(string text, int x, int y, int w, int h)
        {
            return new System.Windows.Forms.Button {
                Text=text, Location=new System.Drawing.Point(x,y),
                Size=new System.Drawing.Size(w,h), TextAlign=System.Drawing.ContentAlignment.MiddleLeft,
                Padding=new System.Windows.Forms.Padding(6,0,0,0) };
        }
    }
}
