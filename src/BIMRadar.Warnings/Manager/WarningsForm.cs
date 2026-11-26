using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using BIMRadar.Warnings.Rwis;
using BIMRadar.Warnings.Rwis.Pure;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace BIMRadar.Warnings.Manager
{
    /// <summary>
    /// The modeless "BIMRadar Warnings Manager" window. It runs on Revit's UI thread, but never touches the Revit API itself:
    /// every Revit action is requested through the ExternalEvent (WarningsHandler), which hands the result back through Finish.
    /// </summary>
    internal sealed class WarningsForm : WF.Form
    {
        public static WarningsForm Instance;

        private readonly WarningsHandler H;
        private readonly ExternalEvent Ev;

        private WF.Label lblModel;
        private WF.ComboBox cmbSeverity;
        private WF.TextBox txtContains;
        private WF.CheckBox chkUnpin, chkNoAsk;
        private WF.Button btnRefresh, btnSolve, btnNext, btnSolveAll, btnReport;
        private WF.ListView list;
        private WF.RichTextBox details;
        private WF.TextBox txtStatus;
        private WF.SplitContainer split;
        private readonly WF.Timer openTimer = new WF.Timer();
        private SD.Font fontBold, fontHead;

        private List<RwisGroupInfo> all = new List<RwisGroupInfo>();     // every warning type of the last scan
        private List<RwisGroupInfo> shown = new List<RwisGroupInfo>();   // after the severity / text filters
        private bool loading;   // list is being rebuilt: selection changes are not user actions
        private bool busy;      // a request is with the ExternalEvent

        /// <summary>Title of the Revit document the list was scanned from ("" before the first scan).</summary>
        public string ListDocTitle = "";

        public WarningsForm(WarningsHandler h, ExternalEvent ev)
        {
            H = h;
            Ev = ev;
            Text = "BIMRadar Warnings Manager";
            Font = new SD.Font("Segoe UI", 9f);
            fontBold = new SD.Font(Font, SD.FontStyle.Bold);
            fontHead = new SD.Font(Font.FontFamily, 10.5f, SD.FontStyle.Bold);
            StartPosition = WF.FormStartPosition.CenterParent;
            Size = new SD.Size(1100, 740);
            MinimumSize = new SD.Size(820, 560);
            ShowInTaskbar = true;
            MinimizeBox = true;
            MaximizeBox = true;

            var tip = new WF.ToolTip();

            var root = new WF.TableLayoutPanel();
            root.Dock = WF.DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.Padding = new WF.Padding(8);
            root.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 100f));
            root.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            root.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            root.RowStyles.Add(new WF.RowStyle(WF.SizeType.Percent, 100f));
            root.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            root.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 62f));
            Controls.Add(root);

            // ---- row 0: model / counts
            lblModel = new WF.Label();
            lblModel.AutoSize = true;
            lblModel.Dock = WF.DockStyle.Fill;
            lblModel.Margin = new WF.Padding(0, 0, 0, 6);
            lblModel.Text = "Reading the warnings ...";
            root.Controls.Add(lblModel, 0, 0);

            // ---- row 1: filters
            var filters = new WF.FlowLayoutPanel();
            filters.Dock = WF.DockStyle.Fill;
            filters.AutoSize = true;
            filters.AutoSizeMode = WF.AutoSizeMode.GrowAndShrink;
            filters.WrapContents = true;
            filters.Margin = new WF.Padding(0, 0, 0, 6);
            root.Controls.Add(filters, 0, 1);

            filters.Controls.Add(MakeLabel("Severity"));
            cmbSeverity = new WF.ComboBox();
            cmbSeverity.DropDownStyle = WF.ComboBoxStyle.DropDownList;
            cmbSeverity.Width = 150;
            foreach (string c in RwisSeverity.Choices) cmbSeverity.Items.Add(c);
            cmbSeverity.SelectedIndex = 0;
            cmbSeverity.Margin = new WF.Padding(0, 2, 14, 0);
            filters.Controls.Add(cmbSeverity);

            filters.Controls.Add(MakeLabel("Contains"));
            txtContains = new WF.TextBox();
            txtContains.Width = 230;
            txtContains.Margin = new WF.Padding(0, 2, 14, 0);
            filters.Controls.Add(txtContains);
            tip.SetToolTip(txtContains, "Only warning types whose text, class name, T-code, severity or class ID matches (as in the v7 'Only warnings containing' option).");

            chkUnpin = new WF.CheckBox();
            chkUnpin.Text = "Unpin to fix";
            chkUnpin.AutoSize = true;
            chkUnpin.Margin = new WF.Padding(0, 4, 14, 0);
            filters.Controls.Add(chkUnpin);
            tip.SetToolTip(chkUnpin, "Pinned elements are unpinned, fixed and pinned again. Without it pinned elements are left for a manual fix.");

            chkNoAsk = new WF.CheckBox();
            chkNoAsk.Text = "Don't ask before fixing";
            chkNoAsk.AutoSize = true;
            chkNoAsk.Margin = new WF.Padding(0, 4, 14, 0);
            filters.Controls.Add(chkNoAsk);
            tip.SetToolTip(chkNoAsk, "No confirmation dialogs: 3D elements that a fix deletes are deleted without asking. One Ctrl+Z still undoes the run.");

            btnRefresh = MakeButton("Refresh");
            btnRefresh.Margin = new WF.Padding(0, 0, 0, 0);
            filters.Controls.Add(btnRefresh);
            tip.SetToolTip(btnRefresh, "Read the warnings of the active model again (changes nothing in the model).");

            // ---- row 2: list (top) and details (bottom)
            split = new WF.SplitContainer();
            split.Dock = WF.DockStyle.Fill;
            split.Orientation = WF.Orientation.Horizontal;
            root.Controls.Add(split, 0, 2);

            list = new WF.ListView();
            list.Dock = WF.DockStyle.Fill;
            list.View = WF.View.Details;
            list.FullRowSelect = true;
            list.MultiSelect = false;
            list.HideSelection = false;
            list.GridLines = true;
            list.Columns.Add("Severity", 85);
            list.Columns.Add("Count", 55, WF.HorizontalAlignment.Right);
            list.Columns.Add("Class", 190);
            list.Columns.Add("Warning text", 430);
            list.Columns.Add("Planned fix", 330);
            split.Panel1.Controls.Add(list);

            details = new WF.RichTextBox();
            details.Dock = WF.DockStyle.Fill;
            details.ReadOnly = true;
            details.BackColor = SD.Color.White;
            details.BorderStyle = WF.BorderStyle.FixedSingle;
            details.DetectUrls = false;
            split.Panel2.Controls.Add(details);

            // ---- row 3: buttons
            var buttons = new WF.FlowLayoutPanel();
            buttons.Dock = WF.DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.AutoSizeMode = WF.AutoSizeMode.GrowAndShrink;
            buttons.WrapContents = true;
            buttons.Margin = new WF.Padding(0, 6, 0, 0);
            root.Controls.Add(buttons, 0, 3);

            btnSolve = MakeButton("Solve this type");
            btnNext = MakeButton("Next type");
            btnSolveAll = MakeButton("Solve all...");
            btnReport = MakeButton("Open report");
            buttons.Controls.Add(btnSolve);
            buttons.Controls.Add(btnNext);
            buttons.Controls.Add(btnSolveAll);
            buttons.Controls.Add(btnReport);
            tip.SetToolTip(btnSolve, "Fix the selected warning type (v7 'Solve warning type'), with its confirmations. One Ctrl+Z undoes it.");
            tip.SetToolTip(btnNext, "Select the next warning type and open its WRN 3D view.");
            tip.SetToolTip(btnSolveAll, "Fix every warning type in the list (v7 'Solve ALL'). One Ctrl+Z undoes everything.");
            tip.SetToolTip(btnReport, "Open Desktop\\RWIS Reports\\<model>_RWIS_Warnings_Report.xlsx");

            // ---- row 4: status line
            txtStatus = new WF.TextBox();
            txtStatus.Dock = WF.DockStyle.Fill;
            txtStatus.Multiline = true;
            txtStatus.ReadOnly = true;
            txtStatus.ScrollBars = WF.ScrollBars.Vertical;
            txtStatus.BackColor = SD.SystemColors.Control;
            txtStatus.BorderStyle = WF.BorderStyle.None;
            txtStatus.Margin = new WF.Padding(0, 6, 0, 0);
            root.Controls.Add(txtStatus, 0, 4);

            // ---- events
            cmbSeverity.SelectedIndexChanged += delegate { ApplyFilters(); };
            txtContains.TextChanged += delegate { ApplyFilters(); };
            btnRefresh.Click += delegate { RaiseRequest(WarningsHandler.ActRefresh, null); };
            list.SelectedIndexChanged += delegate { OnSelectionChanged(); };
            openTimer.Interval = 400;
            openTimer.Tick += delegate { OpenTimerTick(); };
            btnSolve.Click += delegate { OnSolve(); };
            btnNext.Click += delegate { OnNext(); };
            btnSolveAll.Click += delegate { OnSolveAll(); };
            btnReport.Click += delegate { OnOpenReport(); };

            Shown += delegate
            {
                try { split.Panel1MinSize = 120; split.Panel2MinSize = 100; split.SplitterDistance = Math.Max(120, (int)(split.Height * 0.55)); }
                catch (Exception) { }
            };
            FormClosed += delegate
            {
                Instance = null;
                try { openTimer.Stop(); openTimer.Dispose(); } catch (Exception) { }
                try { Ev.Dispose(); } catch (Exception) { }
                try { fontBold.Dispose(); fontHead.Dispose(); } catch (Exception) { }
            };

            ShowDetails();
            UpdateButtons();
        }

        // ---------------------------------------------------------------- helpers to build controls

        private static WF.Label MakeLabel(string text)
        {
            var l = new WF.Label();
            l.Text = text;
            l.AutoSize = true;
            l.Margin = new WF.Padding(0, 6, 6, 0);
            return l;
        }

        private static WF.Button MakeButton(string text)
        {
            var b = new WF.Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = WF.AutoSizeMode.GrowAndShrink;
            b.Padding = new WF.Padding(10, 2, 10, 2);
            b.Margin = new WF.Padding(0, 0, 8, 0);
            return b;
        }

        // ---------------------------------------------------------------- called by WarningsWindow / WarningsHandler

        /// <summary>Restores and activates the open window.</summary>
        public void BringBack()
        {
            try
            {
                if (WindowState == WF.FormWindowState.Minimized) WindowState = WF.FormWindowState.Normal;
                Show();
                Activate();
                BringToFront();
            }
            catch (Exception) { }
        }

        public void RequestRefresh()
        {
            RaiseRequest(WarningsHandler.ActRefresh, null);
        }

        /// <summary>The ExternalEvent finished a request. groups == null keeps the list as it is.</summary>
        public void Finish(string status, bool error, List<RwisGroupInfo> groups, string docTitle, string keepKey)
        {
            if (IsDisposed) return;
            busy = false;
            if (groups != null)
            {
                all = groups;
                ListDocTitle = docTitle ?? "";
                RebuildList(keepKey);
            }
            UpdateButtons();
            SetStatus(status, error);
        }

        // ---------------------------------------------------------------- requests to the ExternalEvent

        private bool RaiseRequest(string request, string key)
        {
            if (busy)
            {
                SetStatus("Revit is still working on the previous request.", false);
                return false;
            }
            H.Request = request;
            H.Key = key;
            H.UnpinToFix = chkUnpin.Checked;
            H.SkipConfirmation = chkNoAsk.Checked;
            H.SeverityChoice = SeverityChoice();
            H.Contains = txtContains.Text;
            busy = true;
            UpdateButtons();
            SetStatus(request == WarningsHandler.ActRefresh ? "Reading the warnings ..." : "Working in Revit ...", false);
            ExternalEventRequest r;
            try
            {
                r = Ev.Raise();
            }
            catch (Exception ex)
            {
                busy = false;
                UpdateButtons();
                SetStatus("Could not start the request: " + ex.Message, true);
                return false;
            }
            if (r != ExternalEventRequest.Accepted)
            {
                busy = false;
                UpdateButtons();
                SetStatus("Revit is busy - try again in a moment.", false);
                return false;
            }
            return true;
        }

        private string SeverityChoice()
        {
            string s = cmbSeverity.SelectedItem as string;
            return s ?? "All";
        }

        // ---------------------------------------------------------------- list

        private RwisGroupInfo Selected()
        {
            if (list.SelectedItems.Count == 0) return null;
            return list.SelectedItems[0].Tag as RwisGroupInfo;
        }

        private int SelectedIndex()
        {
            return list.SelectedIndices.Count == 0 ? -1 : list.SelectedIndices[0];
        }

        private void ApplyFilters()
        {
            RwisGroupInfo g = Selected();
            RebuildList(g != null ? g.Key : null);
        }

        private void RebuildList(string keepKey)
        {
            int oldIdx = SelectedIndex();
            shown = RwisFilter.Apply(all, SeverityChoice(), txtContains.Text);
            loading = true;
            try
            {
                list.BeginUpdate();
                list.Items.Clear();
                foreach (RwisGroupInfo g in shown) list.Items.Add(MakeItem(g));
                int sel = RwisFilter.SelectionAfterRefresh(shown, keepKey, oldIdx);
                if (sel >= 0 && sel < list.Items.Count)
                {
                    list.Items[sel].Selected = true;
                    list.Items[sel].Focused = true;
                    list.EnsureVisible(sel);
                }
            }
            finally
            {
                list.EndUpdate();
                loading = false;
            }
            ShowDetails();
            UpdateCounts();
            UpdateButtons();
        }

        private static string OneLine(string s)
        {
            return (s ?? "").Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
        }

        private static WF.ListViewItem MakeItem(RwisGroupInfo g)
        {
            var it = new WF.ListViewItem(g.Severity);
            it.UseItemStyleForSubItems = false;
            it.SubItems.Add(g.Count.ToString());
            it.SubItems.Add(g.ClassDisplay);
            it.SubItems.Add(OneLine(g.Key));
            WF.ListViewItem.ListViewSubItem plan = it.SubItems.Add(OneLine(g.PlannedFix));
            if (g.AutoFixable == 0) plan.ForeColor = SD.Color.DimGray;
            // severity cell colours: CRITICAL red, HIGH orange, MEDIUM yellow, LOW green
            if (g.Severity == "CRITICAL") { it.BackColor = SD.Color.FromArgb(214, 40, 40); it.ForeColor = SD.Color.White; }
            else if (g.Severity == "HIGH") { it.BackColor = SD.Color.FromArgb(255, 140, 0); it.ForeColor = SD.Color.Black; }
            else if (g.Severity == "MEDIUM") { it.BackColor = SD.Color.FromArgb(255, 215, 0); it.ForeColor = SD.Color.Black; }
            else { it.BackColor = SD.Color.FromArgb(76, 175, 80); it.ForeColor = SD.Color.White; }
            it.Tag = g;
            return it;
        }

        private void UpdateCounts()
        {
            int warnings = 0;
            foreach (RwisGroupInfo g in all) warnings += g.Count;
            lblModel.Text = (ListDocTitle.Length > 0 ? "Model: " + ListDocTitle + "     " : "") + all.Count + " warning type(s), " + warnings + " warning(s) in the model; " + shown.Count + " type(s) shown";
        }

        private void UpdateButtons()
        {
            bool has = Selected() != null;
            btnRefresh.Enabled = !busy;
            btnSolve.Enabled = !busy && has;
            btnNext.Enabled = !busy && shown.Count > 0;
            btnSolveAll.Enabled = !busy && shown.Count > 0;
            btnReport.Enabled = true;
        }

        private void SetStatus(string text, bool error)
        {
            txtStatus.ForeColor = error ? SD.Color.Firebrick : SD.SystemColors.WindowText;
            txtStatus.Text = text ?? "";
        }

        // ---------------------------------------------------------------- details panel

        private void AddText(string text, bool bold)
        {
            details.SelectionStart = details.TextLength;
            details.SelectionLength = 0;
            details.SelectionFont = bold ? fontBold : Font;
            details.SelectedText = text;
        }

        private void AddPara(string label, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            AddText(label + "\n", true);
            AddText(text.Trim() + "\n\n", false);
        }

        private void ShowDetails()
        {
            details.Clear();
            RwisGroupInfo g = Selected();
            if (g == null)
            {
                details.SelectionFont = Font;
                details.SelectedText = all.Count == 0 ? "No warnings in this model (or the list is not read yet)." : "Select a warning type to see its meaning, causes and fix, and to open its WRN 3D view.";
                return;
            }
            details.SelectionStart = 0;
            details.SelectionFont = fontHead;
            details.SelectedText = g.Severity + "  -  " + (g.ClassDisplay.Length > 0 ? g.ClassDisplay : "Unclassified") + "\n";
            AddText(g.Count + " warning(s) of this type, " + g.AutoFixable + " can be fixed by the tool.   WRN view number " + g.Number.ToString("00") + "\n\n", false);
            AddPara("Warning", g.Key);
            AddPara("Meaning", g.Meaning);
            AddPara("Likely causes", g.Causes);
            AddPara("Recommended fix", g.Fix);
            AddPara("Alternative fix", g.Alternative);
            AddPara("Risk if ignored", g.Risk);
            AddPara("What the tool will do", g.PlanDetails);
            details.SelectionStart = 0;
            details.ScrollToCaret();
        }

        // ---------------------------------------------------------------- user actions

        private void OnSelectionChanged()
        {
            if (loading) return;
            ShowDetails();
            UpdateButtons();
            // open the WRN view when the selection settles (arrow keys do not start one view per row)
            openTimer.Stop();
            if (Selected() != null) openTimer.Start();
        }

        private void OpenTimerTick()
        {
            openTimer.Stop();
            RwisGroupInfo g = Selected();
            if (g == null) return;
            if (busy) { openTimer.Start(); return; }   // try again when Revit is free
            RaiseRequest(WarningsHandler.ActOpen, g.Key);
        }

        private void OnNext()
        {
            int n = RwisFilter.NextIndex(SelectedIndex(), shown.Count);
            if (n < 0)
            {
                SetStatus(shown.Count == 0 ? "The list is empty." : "This is the last warning type of the list.", false);
                return;
            }
            list.Items[n].Selected = true;
            list.Items[n].Focused = true;
            list.EnsureVisible(n);
            // OnSelectionChanged started the timer: open now instead (if Revit is busy the timer opens it later)
            if (!busy)
            {
                openTimer.Stop();
                RaiseRequest(WarningsHandler.ActOpen, shown[n].Key);
            }
        }

        private void OnSolve()
        {
            RwisGroupInfo g = Selected();
            if (g == null) { SetStatus("Select a warning type first.", false); return; }
            if (g.AutoFixable == 0)
            {
                SetStatus("The tool cannot fix this type automatically. Recommended fix: " + g.Fix, false);
                return;
            }
            openTimer.Stop();
            RaiseRequest(WarningsHandler.ActSolve, g.Key);
        }

        private void OnSolveAll()
        {
            int warnings, types;
            RwisFilter.CountFixable(shown, out warnings, out types);
            if (warnings == 0)
            {
                SetStatus("Nothing in the list can be fixed automatically.", false);
                return;
            }
            string msg = "Fix " + warnings + " warning(s) across " + types + " warning type(s) now?\n\n"
                + "Only the types shown in the list are included (severity filter: " + SeverityChoice() + (txtContains.Text.Trim().Length > 0 ? ", contains: " + txtContains.Text.Trim() : "") + ").\n"
                + "One Ctrl+Z undoes the whole run. Every change is logged in the RWIS report.\n\n"
                + (chkNoAsk.Checked
                    ? "\"Don't ask before fixing\" is ticked: 3D elements that a fix deletes will be deleted WITHOUT asking."
                    : "Before a 3D element is deleted you will see it highlighted and be asked again.");
            WF.DialogResult r = WF.MessageBox.Show(this, msg, "Solve all warnings", WF.MessageBoxButtons.YesNo, WF.MessageBoxIcon.Warning, WF.MessageBoxDefaultButton.Button2);
            if (r != WF.DialogResult.Yes) { SetStatus("Cancelled - nothing changed.", false); return; }
            openTimer.Stop();
            RaiseRequest(WarningsHandler.ActSolveAll, null);
        }

        private void OnOpenReport()
        {
            try
            {
                if (ListDocTitle.Length == 0) { SetStatus("Click Refresh first so the model is known.", false); return; }
                string path = RwisEngine.ReportPathFor(ListDocTitle);
                if (!System.IO.File.Exists(path))
                {
                    SetStatus("No report yet: " + path + "\nSolve a type, or use 'Warnings Report' on the ribbon, to create it.", false);
                    return;
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                SetStatus("Opened " + path, false);
            }
            catch (Exception ex)
            {
                SetStatus("Could not open the report: " + ex.Message, true);
            }
        }
    }
}
