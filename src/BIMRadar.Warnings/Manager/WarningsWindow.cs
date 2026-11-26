using System;
using Autodesk.Revit.UI;

namespace BIMRadar.Warnings.Manager
{
    /// <summary>Entry of the Warnings button: opens the modeless "BIMRadar Warnings Manager" window, or brings the open one to the front (single instance).</summary>
    internal static class WarningsWindow
    {
        public static void Open(UIApplication uiapp)
        {
            if (uiapp.ActiveUIDocument == null)
            {
                TaskDialog.Show("BIMRadar - Warnings Manager", "Open a Revit project first.");
                return;
            }
            string title = uiapp.ActiveUIDocument.Document.Title;

            WarningsForm inst = WarningsForm.Instance;
            if (inst != null && !inst.IsDisposed)
            {
                inst.BringBack();
                if (inst.ListDocTitle != title) inst.RequestRefresh();   // another model is active now
                return;
            }

            // ExternalEvent.Create needs a valid Revit API context: this runs inside the button's command
            var handler = new WarningsHandler();
            ExternalEvent ev = ExternalEvent.Create(handler);
            var form = new WarningsForm(handler, ev);
            handler.Form = form;
            WarningsForm.Instance = form;
            form.Show(new Win32Owner(uiapp.MainWindowHandle));
            form.RequestRefresh();
        }
    }
}
