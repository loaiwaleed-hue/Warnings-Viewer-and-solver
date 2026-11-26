using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMRadar.Warnings.Rwis;
using BIMRadar.Warnings.Rwis.Pure;

namespace BIMRadar.Warnings.Manager
{
    /// <summary>
    /// All Revit work of the Warnings window. The form fills the request fields and raises the ExternalEvent; Execute runs in a valid
    /// Revit API context, catches every exception (shown in the window status line) and hands the result back to the form.
    /// </summary>
    internal sealed class WarningsHandler : IExternalEventHandler
    {
        public const string ActRefresh = "Refresh";
        public const string ActOpen = "Open";
        public const string ActSolve = "Solve";
        public const string ActSolveAll = "SolveAll";

        public WarningsForm Form;

        // ---- request, set by the form just before Raise ----
        public string Request = ActRefresh;
        public string Key;                       // warning type (warning text) for Open / Solve
        public bool UnpinToFix;
        public bool SkipConfirmation;
        public string SeverityChoice = "All";    // for Solve all: the same filter as the list
        public string Contains = "";

        public string GetName()
        {
            return "BIMRadar Warnings Manager";
        }

        public void Execute(UIApplication app)
        {
            WarningsForm f = Form;
            if (f == null || f.IsDisposed) return;
            string req = Request, key = Key;
            string status = "", title = null;
            bool error = false;
            List<RwisGroupInfo> groups = null;
            try
            {
                UIDocument uidoc = app.ActiveUIDocument;
                if (uidoc == null)
                {
                    f.Finish("Open a Revit project first.", true, null, null, null);
                    return;
                }
                Document doc = uidoc.Document;
                title = doc.Title;
                if (req != ActRefresh && f.ListDocTitle != title)
                {
                    status = "The list shows another model (" + f.ListDocTitle + "). Click Refresh first.";
                    error = true;
                }
                else if (req == ActRefresh)
                {
                    groups = RwisEngine.Scan(app);
                    status = groups.Count + " warning type(s), " + groups.Sum(g => g.Count) + " warning(s) in " + title + ".";
                }
                else if (req == ActOpen)
                {
                    status = OpenType(app, doc, key);
                }
                else if (req == ActSolve || req == ActSolveAll)
                {
                    try
                    {
                        status = req == ActSolve ? SolveType(app, uidoc, doc, key) : SolveAll(app);
                    }
                    catch (Exception ex)
                    {
                        status = "Solve failed: " + ex.Message + " (the run was rolled back)";
                        error = true;
                    }
                    try
                    {
                        groups = RwisEngine.Scan(app);   // the list follows the model after every solve
                    }
                    catch (Exception ex2)
                    {
                        status += "  |  The list could not be refreshed: " + ex2.Message;
                        error = true;
                    }
                }
            }
            catch (Exception ex3)
            {
                status = "Error: " + ex3.Message;
                error = true;
            }
            try
            {
                f.Finish(status, error, groups, title, key);
            }
            catch (Exception)
            {
                // never crash Revit
            }
        }

        // v7 action 1 for one group: its WRN 3D view is created / re-used, coloured (delete = magenta) and activated. No report, no other view is touched.
        private string OpenType(UIApplication app, Document doc, string key)
        {
            if (string.IsNullOrEmpty(key)) return "Select a warning type first.";
            var o = new RwisOptions();
            o.Action = 1;
            o.GroupKey = key;
            o.SkipReport = true;
            string text = RwisEngine.Run(app, o);
            string note = NoteOf(text);
            View3D v = RwisEngine.FindWrnView(doc, key);
            if (v != null) return "Opened view: " + v.Name + (note.Length > 0 ? "  |  " + note : "");
            return note.Length > 0 ? note : "No WRN 3D view was created (no 3D view type in this project?).";
        }

        // v7 action 2 for one group, all v7 confirmations (Fix this warning type, magenta / green Delete / Keep / Delete all).
        private string SolveType(UIApplication app, UIDocument uidoc, Document doc, string key)
        {
            if (string.IsNullOrEmpty(key)) return "Select a warning type first.";
            // the delete confirmation highlights elements in the ACTIVE view: make it the type's WRN view when it exists (no transaction is open here)
            View3D wv = RwisEngine.FindWrnView(doc, key);
            if (wv != null && !doc.ActiveView.Id.Equals(wv.Id))
            {
                try { uidoc.ActiveView = wv; } catch (Exception) { }
            }
            var o = new RwisOptions();
            o.Action = 2;
            o.GroupKey = key;
            o.UnpinToFix = UnpinToFix;
            o.SkipConfirmation = SkipConfirmation;
            string text = RwisEngine.Run(app, o);
            return RwisSummary.Condense(text, RwisEngine.TypesMarker);
        }

        // v7 action 4 for the filtered list. The window already asked Yes / No (count + one Ctrl+Z), so the engine's own question is skipped.
        private string SolveAll(UIApplication app)
        {
            var o = new RwisOptions();
            o.Action = 4;
            o.Severities = RwisSeverity.Parse(SeverityChoice);
            o.Contains = Contains ?? "";
            o.UnpinToFix = UnpinToFix;
            o.SkipConfirmation = SkipConfirmation;
            o.SolveAllConfirmed = true;
            string text = RwisEngine.Run(app, o);
            return RwisSummary.Condense(text, RwisEngine.TypesMarker);
        }

        private static string NoteOf(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("Note:", StringComparison.Ordinal)) return line;
            }
            return "";
        }
    }
}
