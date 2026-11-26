using System;
using WF = System.Windows.Forms;

namespace BIMRadar.Warnings.Manager
{
    /// <summary>Owner of the modeless window: the Revit main window (the window stays above Revit and is minimised with it).</summary>
    internal sealed class Win32Owner : WF.IWin32Window
    {
        private readonly IntPtr _h;

        public Win32Owner(IntPtr h)
        {
            _h = h;
        }

        public IntPtr Handle
        {
            get { return _h; }
        }
    }
}
