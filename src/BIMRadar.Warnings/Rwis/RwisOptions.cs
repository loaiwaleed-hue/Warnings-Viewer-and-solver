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

        // ---- additions for the Warnings window (T4); all default to "off" = exact v7 behaviour ----

        /// <summary>
        /// One warning type (the warning text = group key). Action 1: only this type gets its WRN 3D view (no renaming of other views).
        /// Action 2: solve this type instead of "the type of the active WRN view". Ignored by actions 3 and 4. Empty = v7 behaviour.
        /// </summary>
        public string GroupKey = "";

        /// <summary>Read-only scan: collect, classify and plan (preview) like Run, then stop before any view, transaction or report. The result is ScanResult.</summary>
        public bool ScanOnly;

        /// <summary>Output of a ScanOnly run: one entry per warning type, in the v7 severity order.</summary>
        public List<RwisGroupInfo> ScanResult;

        /// <summary>Action 4: the caller (Warnings window) already asked "Fix N warnings ... ?", so the engine skips its own Yes/No dialog. The delete confirmations stay.</summary>
        public bool SolveAllConfirmed;
    }
}
