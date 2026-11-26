using System.Collections.Generic;
using System.Linq;
using BIMRadar.Warnings.Rwis.Pure;
using Xunit;

namespace BIMRadar.Warnings.Tests
{
    // Synthetic groups only (invented texts and ids).
    public class WindowLogicTests
    {
        private static RwisGroupInfo G(string key, string sev, string classId, string className, string tax, int count = 1, int fixable = 0)
        {
            var g = new RwisGroupInfo();
            g.Key = key; g.Severity = sev; g.ClassId = classId; g.ClassName = className; g.Taxonomy = tax; g.Count = count; g.AutoFixable = fixable;
            return g;
        }

        private static List<RwisGroupInfo> Sample()
        {
            return new List<RwisGroupInfo>
            {
                G("Walls overlap on level A", "CRITICAL", "WC-001", "Overlapping walls", "T03", 3, 2),
                G("Duplicate mark value", "HIGH", "WC-002", "Duplicate marks", "T01", 5, 5),
                G("Room tag is outside of its room", "MEDIUM", "WC-003", "Tags outside rooms", "T14", 2, 2),
                G("Something odd", "MEDIUM", "", "Unclassified", "-", 1, 0),
                G("Slightly off axis", "LOW", "WC-004", "Off axis", "T12", 4, 4)
            };
        }

        [Theory]
        [InlineData("All", 4)]
        [InlineData("CRITICAL", 1)]
        [InlineData("HIGH", 1)]
        [InlineData("MEDIUM", 1)]
        [InlineData("LOW", 1)]
        [InlineData("CRITICAL + HIGH", 2)]
        [InlineData("MEDIUM + LOW", 2)]
        public void SeverityChoicesParse(string choice, int count)
        {
            Assert.Equal(count, RwisSeverity.Parse(choice).Count);
        }

        [Fact]
        public void EveryChoiceIsUnderstood()
        {
            foreach (string c in RwisSeverity.Choices) Assert.NotEmpty(RwisSeverity.Parse(c));
            Assert.Equal("All", RwisSeverity.Choices[0]);
        }

        [Fact]
        public void ApplyBySeverity()
        {
            var r = RwisFilter.Apply(Sample(), "CRITICAL + HIGH", "");
            Assert.Equal(new[] { "CRITICAL", "HIGH" }, r.Select(g => g.Severity).ToArray());
            r = RwisFilter.Apply(Sample(), "MEDIUM + LOW", "");
            Assert.Equal(3, r.Count);
            Assert.Equal(new[] { "MEDIUM", "MEDIUM", "LOW" }, r.Select(g => g.Severity).ToArray());
        }

        [Fact]
        public void ApplyKeepsSeverityOrderAndEngineOrderWithinSeverity()
        {
            var shuffled = Sample(); shuffled.Reverse();
            var r = RwisFilter.Apply(shuffled, "All", "");
            Assert.Equal(new[] { "CRITICAL", "HIGH", "MEDIUM", "MEDIUM", "LOW" }, r.Select(g => g.Severity).ToArray());
            // stable: the two MEDIUM groups keep their relative (reversed input) order
            Assert.Equal(new[] { "Something odd", "Room tag is outside of its room" }, r.Where(g => g.Severity == "MEDIUM").Select(g => g.Key).ToArray());
        }

        [Fact]
        public void ContainsMatchesTextClassNameTCodeSeverityAndClassId()
        {
            Assert.Equal(1, RwisFilter.Apply(Sample(), "All", "duplicate").Count);          // text, case-insensitive
            Assert.Equal(1, RwisFilter.Apply(Sample(), "All", "Overlapping").Count);        // class name
            Assert.Equal("Slightly off axis", RwisFilter.Apply(Sample(), "All", "t12").Single().Key);   // T-code, equal
            Assert.Equal(2, RwisFilter.Apply(Sample(), "All", "medium").Count);             // severity, equal
            Assert.Equal("Duplicate mark value", RwisFilter.Apply(Sample(), "All", "wc-002").Single().Key);   // class ID, equal
            Assert.Empty(RwisFilter.Apply(Sample(), "All", "T1"));                          // T-code is an exact match, not a prefix
            Assert.Equal(5, RwisFilter.Apply(Sample(), "All", "   ").Count);                // blank = no filter
        }

        [Fact]
        public void ContainsAndSeverityCombine()
        {
            var r = RwisFilter.Apply(Sample(), "MEDIUM + LOW", "tag");
            Assert.Equal("Room tag is outside of its room", r.Single().Key);
        }

        [Fact]
        public void MatchesIgnoresQuotesInTheWarningText()
        {
            // the engine matches the normalised text (lower case, quotes removed)
            Assert.True(RwisFilter.Matches(RwisText.Normalize("Wall \"A\" is 'bad'"), "", "", "LOW", "", "wall a is bad"));
        }

        [Theory]
        [InlineData(-1, 3, 0)]
        [InlineData(0, 3, 1)]
        [InlineData(1, 3, 2)]
        [InlineData(2, 3, -1)]
        [InlineData(0, 0, -1)]
        [InlineData(-1, 0, -1)]
        public void NextIndex(int current, int count, int expected)
        {
            Assert.Equal(expected, RwisFilter.NextIndex(current, count));
        }

        [Fact]
        public void SelectionAfterRefresh()
        {
            var l = Sample();
            Assert.Equal(1, RwisFilter.SelectionAfterRefresh(l, "Duplicate mark value", 4));   // same type, moved
            Assert.Equal(4, RwisFilter.SelectionAfterRefresh(l, "gone", 9));                    // clamped
            Assert.Equal(2, RwisFilter.SelectionAfterRefresh(l, "gone", 2));                    // next one takes the row
            Assert.Equal(-1, RwisFilter.SelectionAfterRefresh(l, "gone", -1));                  // nothing was selected
            Assert.Equal(-1, RwisFilter.SelectionAfterRefresh(new List<RwisGroupInfo>(), "x", 0));
        }

        [Fact]
        public void CountFixable()
        {
            int w, t;
            RwisFilter.CountFixable(Sample(), out w, out t);
            Assert.Equal(13, w);
            Assert.Equal(4, t);
            RwisFilter.CountFixable(new List<RwisGroupInfo>(), out w, out t);
            Assert.Equal(0, w); Assert.Equal(0, t);
        }

        [Fact]
        public void ClassDisplayJoinsIdAndName()
        {
            Assert.Equal("WC-001 Overlapping walls", Sample()[0].ClassDisplay);
            Assert.Equal("Unclassified", Sample()[3].ClassDisplay);
        }

        [Fact]
        public void SummaryCondenseKeepsOutcomeLinesAndStopsAtTypeList()
        {
            string text = "RWIS Warning Manager v7 - Solve\nNote: Cancelled - nothing changed.\nWarnings in model: 10\nFixed this run: 3\nReport: C:\\x\\r.xlsx\nOpen now: 7\nHealth score: 96.5\nReport note: none\n\nWarning types:\n01 [HIGH] Fixed this run: 99";
            string s = RwisSummary.Condense(text, "Warning types:");
            Assert.Equal("Note: Cancelled - nothing changed.  |  Fixed this run: 3  |  Report: C:\\x\\r.xlsx  |  Open now: 7  |  Health score: 96.5  |  Report note: none", s);
            Assert.Equal("", RwisSummary.Condense(null, "x"));
        }
    }
}
