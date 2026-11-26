using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Autodesk.Revit.UI;

namespace BIMRadar.Warnings.Standalone
{
    public class App : IExternalApplication
    {
        private const string TabName = "BIMRadar";
        private const string PanelName = "Health";
        private const string ExtensionName = "BIMRadar-Warnings.extension";

        public Result OnStartup(UIControlledApplication application)
        {
            // Never show two Health panels. If the pyRevit extension is registered, stay hidden.
            try
            {
                string reason = PyRevitRegistration();
                if (reason != null)
                {
                    WriteLog("pyRevit extension registered (" + reason + "); standalone add-in did not create its ribbon panel.");
                    return Result.Succeeded;
                }
            }
            catch (Exception)
            {
            }

            try
            {
                CreateRibbon(application);
            }
            catch (Exception ex)
            {
                WriteLog("Ribbon creation failed: " + ex.Message);
            }
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private static void CreateRibbon(UIControlledApplication application)
        {
            // The tab may already exist (created by another BIMRadar tool): ignore that error.
            try
            {
                application.CreateRibbonTab(TabName);
            }
            catch (Exception)
            {
            }

            RibbonPanel panel = null;
            foreach (RibbonPanel p in application.GetRibbonPanels(TabName))
            {
                if (p.Name == PanelName)
                {
                    panel = p;
                    break;
                }
            }
            if (panel == null)
            {
                panel = application.CreateRibbonPanel(TabName, PanelName);
            }

            string asm = typeof(App).Assembly.Location;
            string ns = "BIMRadar.Warnings.Standalone.";

            AddButton(panel, asm, ns, "BIMRadar_Warnings_Open", "Warnings", "OpenCommand",
                "Open the Warnings window: warning types by severity, with meaning, causes and fix.", "Warnings");
            AddButton(panel, asm, ns, "BIMRadar_Warnings_Review", "Review\nWarnings", "ReviewCommand",
                "Create one WRN 3D view per warning type, coloured by severity. Elements are not changed.", "Review");
            AddButton(panel, asm, ns, "BIMRadar_Warnings_SolveView", "Solve\nThis View", "SolveViewCommand",
                "Solve the warning type of the active WRN 3D view.", "Solve");
            AddButton(panel, asm, ns, "BIMRadar_Warnings_Report", "Warnings\nReport", "ReportCommand",
                "Update the Excel warnings report only.", "Report");
        }

        private static void AddButton(RibbonPanel panel, string asm, string ns, string id, string text, string command, string tooltip, string icon)
        {
            try
            {
                var data = new PushButtonData(id, text, asm, ns + command);
                data.ToolTip = tooltip;
                SetImages(data, icon);
                panel.AddItem(data);
            }
            catch (Exception ex)
            {
                WriteLog("Button " + id + " failed: " + ex.Message);
            }
        }

        private static void SetImages(PushButtonData data, string baseName)
        {
            try
            {
                var large = LoadImage(baseName + "32.png");
                var small = LoadImage(baseName + "16.png");
                if (large != null) data.LargeImage = large;
                if (small != null) data.Image = small;
            }
            catch (Exception)
            {
                // A missing icon must never break startup.
            }
        }

        private static System.Windows.Media.ImageSource LoadImage(string fileName)
        {
            try
            {
                Assembly a = typeof(App).Assembly;
                string name = a.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase));
                if (name == null) return null;
                using (Stream s = a.GetManifestResourceStream(name))
                {
                    if (s == null) return null;
                    var img = new System.Windows.Media.Imaging.BitmapImage();
                    img.BeginInit();
                    img.StreamSource = s;
                    img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    img.EndInit();
                    img.Freeze();
                    return img;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Returns a short reason when the pyRevit extension is registered, otherwise null.</summary>
        private static string PyRevitRegistration()
        {
            string ini = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pyRevit", "pyRevit_config.ini");
            if (!File.Exists(ini)) return null;
            string text = File.ReadAllText(ini);
            // #98: pyRevit keeps an [<ext>] settings section after its folder is removed, so the section name
            // alone is not proof. Only a loaded folder counts: pyRevit's default Extensions folder or a userextensions path.
            string defDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pyRevit", "Extensions");
            try { if (Directory.Exists(Path.Combine(defDir, ExtensionName))) return "found in " + defDir; } catch (Exception) { }

            foreach (string line in text.Split('\n'))
            {
                string l = line.Trim();
                if (!l.StartsWith("userextensions", StringComparison.OrdinalIgnoreCase)) continue;
                int eq = l.IndexOf('=');
                if (eq < 0) continue;
                string value = l.Substring(eq + 1).Trim();
                // Format: ["C:\\path one", "D:\\path two"] (JSON-style list; backslashes may be escaped).
                foreach (Match m in Regex.Matches(value, "\"([^\"]*)\""))
                {
                    string p = m.Groups[1].Value.Replace("\\\\", "\\").Trim();
                    if (p.Length == 0) continue;
                    try
                    {
                        if (Directory.Exists(Path.Combine(p, ExtensionName))) return "found in " + p;
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            return null;
        }

        private static void WriteLog(string text)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BIMRadar", "Warnings");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "startup.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine);
            }
            catch (Exception)
            {
            }
        }
    }
}
