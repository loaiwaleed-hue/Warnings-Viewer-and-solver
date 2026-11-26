using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMRadar.Warnings.Rwis;

namespace BIMRadar.Warnings
{
    /// <summary>Static entry points called by both the pyRevit buttons and the standalone add-in. Keep this API stable: later tickets fill the bodies.</summary>
    public static class Launcher
    {
        private const string DialogTitle = "BIMRadar · Warnings Manager";

        /// <summary>Warnings button: opens the Warnings window.</summary>
        public static void Open(UIApplication uiapp)
        {
            // T4 fills this in (modeless Warnings window).
            Guard("Warnings", delegate
            {
                TaskDialog.Show(DialogTitle, "Warnings window comes in the next build.\n\nRevit " + uiapp.Application.VersionNumber
                    + " (" + Compat.RuntimeDescription + ")");
            });
        }

        /// <summary>Review Warnings button: v7 action 1, one WRN 3D view per warning type, all severities, defaults.</summary>
        public static void Review(UIApplication uiapp)
        {
            Guard("Review Warnings", delegate
            {
                var o = new RwisOptions();
                o.Action = 1;
                ShowSummary("Review Warnings", RwisEngine.Run(uiapp, o));
            });
        }

        /// <summary>Solve This View button: v7 action 2, solves the warning type of the active WRN 3D view.</summary>
        public static void SolveView(UIApplication uiapp)
        {
            Guard("Solve This View", delegate
            {
                UIDocument uidoc = uiapp.ActiveUIDocument;
                if (uidoc == null)
                {
                    TaskDialog.Show(DialogTitle, "Open a Revit project first.");
                    return;
                }
                // the check v7 action 2 uses: the view carries the "RWIS|" marker of a WRN review view
                if (!RwisEngine.IsWrnView(uidoc.Document.ActiveView))
                {
                    TaskDialog.Show(DialogTitle, "The active view is not a WRN review view.\n\nRun 'Review Warnings' first, open one of the 'WRN ..' 3D views, then run 'Solve This View' again.");
                    return;
                }
                var o = new RwisOptions();
                o.Action = 2;
                ShowSummary("Solve This View", RwisEngine.Run(uiapp, o));
            });
        }

        /// <summary>Warnings Report button: collects, classifies and updates the Excel report only (no views, no changes to the model).</summary>
        public static void Report(UIApplication uiapp)
        {
            Guard("Warnings Report", delegate
            {
                var o = new RwisOptions();
                o.Action = 1;
                o.Skip3DViews = true;
                o.ReportOnly = true;
                ShowSummary("Warnings Report", RwisEngine.Run(uiapp, o));
            });
        }

        // ---- helpers ----

        private static void Guard(string feature, Action body)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                try
                {
                    var td = new TaskDialog(DialogTitle);
                    td.MainInstruction = feature + " failed";
                    td.MainContent = ex.Message;
                    td.ExpandedContent = ex.ToString();
                    td.Show();
                }
                catch (Exception)
                {
                    // never crash Revit
                }
            }
        }

        /// <summary>Shows the v7 summary: the scalar lines as content, the per-type lines under "Show details".</summary>
        private static void ShowSummary(string feature, string text)
        {
            string main = text, details = "";
            int cut = text.IndexOf(RwisEngine.TypesMarker + "\n", StringComparison.Ordinal);
            if (cut < 0) cut = text.IndexOf(RwisEngine.TypesMarker + "\r\n", StringComparison.Ordinal);
            if (cut >= 0)
            {
                main = text.Substring(0, cut).TrimEnd();
                details = text.Substring(cut);
            }
            var td = new TaskDialog(DialogTitle);
            td.MainInstruction = feature;
            td.MainContent = main;
            if (details.Length > 0) td.ExpandedContent = details;
            td.CommonButtons = TaskDialogCommonButtons.Close;
            td.Show();
        }
    }
}
