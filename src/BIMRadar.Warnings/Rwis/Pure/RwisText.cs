using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BIMRadar.Warnings.Rwis.Pure
{
    /// <summary>Text helpers of the RWIS engine (no Revit types). Ported verbatim from v7 (normalisation, warning hash, view-name cleaning).</summary>
    internal static class RwisText
    {
        /// <summary>Lower case, quotes removed: the form in which warning texts are matched.</summary>
        public static string Normalize(string text)
        {
            return text.ToLowerInvariant().Replace("\"", "").Replace("'", "");
        }

        /// <summary>"RWIS-" + 12 hex characters of MD5(text.trim.lower + "|" + sorted ids). Same warning text + same element ids = same hash.</summary>
        public static string HashOf(string text, IEnumerable<long> ids)
        {
            string key = text.Trim().ToLowerInvariant() + "|" + string.Join(",", ids.OrderBy(x => x).Select(x => x.ToString()).ToArray());
            using (var md5 = System.Security.Cryptography.MD5.Create())
                return "RWIS-" + BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "").Substring(0, 12);
        }

        /// <summary>Removes characters Revit view names do not allow and collapses blanks.</summary>
        public static string Clean(string s)
        {
            foreach (char ch in "\\:{}[]|;<>?`~/\"") s = s.Replace(ch, ' ');
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s.Trim();
        }

        /// <summary>Shortens a warning text for view names / dialog titles: first sentence (if longer than 15), max n characters.</summary>
        public static string Trunc(string s, int n)
        {
            s = s.Trim().TrimEnd('.'); int dot = s.IndexOf(". "); if (dot > 15) s = s.Substring(0, dot);
            return s.Length > n ? s.Substring(0, n).Trim() + "..." : s;
        }
    }

    /// <summary>Grouping of warnings by type = same warning text (exactly like Revit's own dialog), most severe first.</summary>
    internal static class RwisGrouping
    {
        /// <summary>
        /// texts[i] / severities[i] describe warning i. Returns the distinct texts ordered by the severity of the group's first warning
        /// (descending), then by text (v7: OrderByDescending(sevRank).ThenBy(key) with the default string comparer).
        /// </summary>
        public static List<string> OrderKeys(IList<string> texts, IList<string> severities)
        {
            return Enumerable.Range(0, texts.Count).GroupBy(i => texts[i])
                .OrderByDescending(g => RwisSeverity.Rank(severities[g.First()])).ThenBy(g => g.Key).Select(g => g.Key).ToList();
        }
    }
}
