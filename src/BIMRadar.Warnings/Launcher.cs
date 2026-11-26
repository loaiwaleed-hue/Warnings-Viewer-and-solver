using System;
using Autodesk.Revit.UI;

namespace BIMRadar.Warnings
{
    /// <summary>Static entry points called by both the pyRevit buttons and the standalone add-in. Keep this API stable: later tickets fill the bodies.</summary>
    public static class Launcher
    {
        private const string DialogTitle = "BIMRadar · Warnings Manager";

        /// <summary>Warnings button: opens the Warnings window.</summary>
        public static void Open(UIApplication uiapp)
        {
            ComingSoon(uiapp, "Warnings");
        }

        /// <summary>Review Warnings button: one WRN 3D view per warning type.</summary>
        public static void Review(UIApplication uiapp)
        {
            ComingSoon(uiapp, "Review Warnings");
        }

        /// <summary>Solve This View button: solves the warning type of the active WRN 3D view.</summary>
        public static void SolveView(UIApplication uiapp)
        {
            ComingSoon(uiapp, "Solve This View");
        }

        /// <summary>Warnings Report button: updates the Excel report only.</summary>
        public static void Report(UIApplication uiapp)
        {
            ComingSoon(uiapp, "Warnings Report");
        }

        private static void ComingSoon(UIApplication uiapp, string feature)
        {
            try
            {
                string text = feature + ": Coming in the next build.\n\nRevit " + uiapp.Application.VersionNumber
                    + " (" + Compat.RuntimeDescription + ")";
                TaskDialog.Show(DialogTitle, text);
            }
            catch (Exception ex)
            {
                TaskDialog.Show(DialogTitle, ex.Message);
            }
        }
    }
}
