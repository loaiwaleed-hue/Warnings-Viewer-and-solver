using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMRadar.Warnings.Rwis.Pure
{
    /// <summary>
    /// Keyword classification of a Revit warning text against TBL_Warning_Classes (no Revit types).
    /// Ported verbatim from AICon routine isolate-warnings-rwis-v7 (ALIASES + classify).
    /// </summary>
    internal sealed class RwisClassifier
    {
        // keyword that Revit's own wording uses -> Keyword_Match of the class it belongs to
        private static readonly string[][] ALIASES = {
            new string[] { "room tag is outside", "room tags are outside of rooms" },
            new string[] { "space tag is outside", "space type tag is outside of its space" },
            new string[] { "lines overlap", "space separation lines overlap" },
            new string[] { "multiple rooms are in the same enclosed region", "multiple space type are in the same enclosed" },
            new string[] { "joined but do not intersect", "two elements were not automatically joined" },
            new string[] { "can't keep elements joined", "two elements were not automatically joined" }
        };

        private readonly List<string[]> CLS;

        /// <param name="classRows">TBL_Warning_Classes body rows (header already removed, rows with an empty Keyword_Match already removed - see FromTable).</param>
        public RwisClassifier(List<string[]> classRows)
        {
            CLS = classRows;
        }

        /// <summary>Body rows of TBL_Warning_Classes that have a keyword (column B).</summary>
        public static List<string[]> BodyRows(List<string[]> table)
        {
            return table.Skip(1).Where(r => XlsxIO.C(r, 1).Trim().Length > 0).ToList();
        }

        public static RwisClassifier FromTable(List<string[]> table)
        {
            return new RwisClassifier(BodyRows(table));
        }

        /// <summary>d = RwisText.Normalize(warning text). Returns { classId, className, taxonomy, severity, notes, matched keyword }.</summary>
        public string[] Classify(string d)
        {
            string[] best = null; string bestKw = "";
            foreach (string[] r in CLS)
            {
                string k = XlsxIO.C(r, 1).Trim().ToLowerInvariant().Replace("'", "");
                if (k.Length > bestKw.Length && d.Contains(k)) { best = r; bestKw = k; }
            }
            foreach (string[] a in ALIASES)
            {
                string k = a[0].Replace("'", "");
                if (k.Length > bestKw.Length && d.Contains(k))
                {
                    string[] par = CLS.FirstOrDefault(r => XlsxIO.C(r, 1).Trim().ToLowerInvariant() == a[1]);
                    if (par != null) { best = par; bestKw = k; }
                }
            }
            if (best == null) return new string[] { "", "Unclassified", "-", "MEDIUM", "Not in TBL_Warning_Classes", "" };
            string sv = XlsxIO.C(best, 4).Trim().ToUpperInvariant();
            if (sv != "LOW" && sv != "MEDIUM" && sv != "HIGH" && sv != "CRITICAL") sv = "MEDIUM";
            return new string[] { XlsxIO.C(best, 0), XlsxIO.C(best, 2), XlsxIO.C(best, 3), sv, XlsxIO.C(best, 6), bestKw };
        }
    }
}
