using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMRadar.Warnings.Rwis.Pure
{
    /// <summary>One warning type (= all warnings with the same text) as shown in the Warnings window. No Revit types.</summary>
    public sealed class RwisGroupInfo
    {
        /// <summary>The warning text = the group key of v7.</summary>
        public string Key = "";

        /// <summary>1-based position in the full severity order = the "nn" of the view name "WRN nn - ...".</summary>
        public int Number;

        public string Severity = "";
        public int Count;

        /// <summary>How many of the warnings of this type the tool can fix automatically (planned fix is not MANUAL / OK).</summary>
        public int AutoFixable;

        public string ClassId = "";
        public string ClassName = "";
        public string Taxonomy = "";

        /// <summary>The v7 summary text of the planned fix (first part of the plan of the first fixable warning, else of the first warning).</summary>
        public string PlannedFix = "";

        /// <summary>The v7 per-warning plan lines ("Warning n [ids ..]: plan").</summary>
        public string PlanDetails = "";

        // TBL_Fix_Guide (class level, taxonomy level as fallback - same lookup as the report)
        public string Meaning = "";
        public string Causes = "";
        public string Fix = "";
        public string Alternative = "";
        public string Risk = "";

        /// <summary>Element ids (Compat.IdValue) of all warnings of the type, distinct.</summary>
        public List<long> ElementIds = new List<long>();

        /// <summary>"ID name", e.g. for the Class column.</summary>
        public string ClassDisplay
        {
            get { return ((ClassId ?? "") + " " + (ClassName ?? "")).Trim(); }
        }
    }

    /// <summary>Filtering and selection logic of the Warnings window (no Revit types).</summary>
    internal static class RwisFilter
    {
        /// <summary>
        /// The v7 "Only warnings containing" test: the text matches the normalised warning text, the class name (contains), the T-code,
        /// the severity or the class ID (equal). Empty filter = everything. normText = RwisText.Normalize(warning text).
        /// </summary>
        public static bool Matches(string normText, string className, string taxonomy, string severity, string classId, string filter)
        {
            string f = (filter ?? "").Trim();
            if (f.Length == 0) return true;
            if ((normText ?? "").IndexOf(f.ToLowerInvariant()) >= 0) return true;
            if ((className ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (string.Equals(taxonomy, f, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(severity, f, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(classId, f, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// The groups the window shows: severity choice (RwisSeverity.Choices text) and Contains text, most severe first
        /// (stable: the engine order within a severity is kept).
        /// </summary>
        public static List<RwisGroupInfo> Apply(IEnumerable<RwisGroupInfo> groups, string severityChoice, string contains)
        {
            HashSet<string> sev = RwisSeverity.Parse(severityChoice);
            return groups
                .Where(g => sev.Contains(g.Severity) && Matches(RwisText.Normalize(g.Key ?? ""), g.ClassName, g.Taxonomy, g.Severity, g.ClassId, contains))
                .OrderByDescending(g => RwisSeverity.Rank(g.Severity))
                .ToList();
        }

        /// <summary>Row after "Next type": current + 1, or -1 when there is none (no wrap). current &lt; 0 (nothing selected) gives 0.</summary>
        public static int NextIndex(int current, int count)
        {
            if (count <= 0) return -1;
            if (current < 0) return 0;
            return current + 1 < count ? current + 1 : -1;
        }

        public static int IndexOfKey(IList<RwisGroupInfo> groups, string key)
        {
            if (groups == null || key == null) return -1;
            for (int i = 0; i < groups.Count; i++) if (groups[i].Key == key) return i;
            return -1;
        }

        /// <summary>Row to select after the list was rebuilt: the same type if it is still there, else the row at the old position (clamped), else -1.</summary>
        public static int SelectionAfterRefresh(IList<RwisGroupInfo> groups, string oldKey, int oldIndex)
        {
            if (groups == null || groups.Count == 0) return -1;
            int k = IndexOfKey(groups, oldKey);
            if (k >= 0) return k;
            if (oldIndex < 0) return -1;
            return Math.Min(oldIndex, groups.Count - 1);
        }

        /// <summary>Number of warnings the tool can fix in the groups, and the number of types that have at least one such warning (the numbers of the v7 "Solve ALL" question).</summary>
        public static void CountFixable(IEnumerable<RwisGroupInfo> groups, out int warnings, out int types)
        {
            warnings = 0; types = 0;
            foreach (RwisGroupInfo g in groups) { warnings += g.AutoFixable; if (g.AutoFixable > 0) types++; }
        }
    }

    /// <summary>Shortens the text returned by RwisEngine.Run to one status line.</summary>
    internal static class RwisSummary
    {
        private static readonly string[] Prefixes = { "Note:", "Fixed this run:", "Report:", "Open now:", "Health score:", "Report note:" };

        /// <summary>Keeps the lines that tell the outcome (stop at the line equal to stopAt, the per-type list) and joins them with " | ".</summary>
        public static string Condense(string text, string stopAt)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var parts = new List<string>();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (stopAt != null && line == stopAt) break;
                foreach (string p in Prefixes)
                    if (line.StartsWith(p, StringComparison.Ordinal)) { parts.Add(line); break; }
            }
            return string.Join("  |  ", parts.ToArray());
        }
    }
}
