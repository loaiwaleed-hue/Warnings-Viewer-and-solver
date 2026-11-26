using System;
using System.Collections.Generic;

namespace BIMRadar.Warnings.Rwis.Pure
{
    /// <summary>Severity helpers of the RWIS tables (no Revit types). Ported from v7 (sevRank, severity filter, action parsing).</summary>
    internal static class RwisSeverity
    {
        /// <summary>Most to least severe.</summary>
        public static readonly string[] Order = { "CRITICAL", "HIGH", "MEDIUM", "LOW" };

        /// <summary>CRITICAL 4, HIGH 3, MEDIUM 2, anything else 1 (as v7).</summary>
        public static int Rank(string s)
        {
            return s == "CRITICAL" ? 4 : s == "HIGH" ? 3 : s == "MEDIUM" ? 2 : 1;
        }

        public static HashSet<string> All()
        {
            return new HashSet<string>(Order);
        }

        /// <summary>
        /// The routine.json "severities" option text -> allowed set. Empty or "All ..." = all four; otherwise every severity word that occurs
        /// in the text ("CRITICAL + HIGH" = both). Exactly v7: sevTxt.Length == 0 || sevTxt.StartsWith("ALL") || sevTxt.Contains(s0).
        /// </summary>
        public static HashSet<string> Parse(string text)
        {
            string sevTxt = (text ?? "").Trim().ToUpperInvariant();
            var set = new HashSet<string>();
            foreach (string s0 in Order) if (sevTxt.Length == 0 || sevTxt.StartsWith("ALL") || sevTxt.Contains(s0)) set.Add(s0);
            return set;
        }

        /// <summary>The routine.json "action" option text ("2. Solve ...") -> 1..4; anything else = 1 (as v7).</summary>
        public static int ParseAction(string text)
        {
            string actionTxt = (text ?? "").Trim();
            return actionTxt.StartsWith("2") ? 2 : actionTxt.StartsWith("3") ? 3 : actionTxt.StartsWith("4") ? 4 : 1;
        }
    }
}
