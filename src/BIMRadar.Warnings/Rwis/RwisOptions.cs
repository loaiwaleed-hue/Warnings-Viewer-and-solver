using System.Collections.Generic;
using BIMRadar.Warnings.Rwis.Pure;

namespace BIMRadar.Warnings.Rwis
{
    /// <summary>Inputs of the RWIS engine. Same meaning and defaults as the inputs of routine.json (isolate-warnings-rwis-v7).</summary>
    public sealed class RwisOptions
    {
        /// <summary>1 Review (3D view per type, no changes), 2 Solve the type of the active WRN view, 3 Step through each type, 4 Solve ALL.</summary>
        public int Action = 1;

        /// <summary>Severities to review / solve. Default all four.</summary>
        public HashSet<string> Severities = RwisSeverity.All();

        /// <summary>"Unpin to fix": pinned elements are unpinned, fixed and re-pinned.</summary>
        public bool UnpinToFix;

        /// <summary>"Only warnings containing" (text, class name, T-code, severity or class ID). Empty = all.</summary>
        public string Contains = "";

        /// <summary>"Don't ask before fixing": no confirmation dialogs, delete without asking.</summary>
        public bool SkipConfirmation;

        /// <summary>"Don't create WRN 3D views".</summary>
        public bool Skip3DViews;

        /// <summary>"Don't update the Excel report".</summary>
        public bool SkipReport;

        /// <summary>
        /// Addition to v7 (not in routine.json) for the Warnings Report button: collect, classify and update the workbook only.
        /// No transaction, no views (also no renaming of existing views), no fixes. Implies Skip3DViews; use with Action = 1.
        /// </summary>
        public bool ReportOnly;
    }
}
