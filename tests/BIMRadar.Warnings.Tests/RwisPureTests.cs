using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using BIMRadar.Warnings.Rwis.Pure;
using Xunit;

namespace BIMRadar.Warnings.Tests
{
    // All data in these tests is synthetic (invented warning texts, ids and rows). The only external file is the public RWIS template.
    internal static class Fixture
    {
        public static string TemplatePath
        {
            get { return Path.Combine(AppContext.BaseDirectory, "RWIS_Template.xlsx"); }
        }

        public static Dictionary<string, List<string[]>> Template()
        {
            return XlsxIO.ReadXlsx(XlsxIO.UnzipFile(TemplatePath));
        }

        public static RwisClassifier Classifier()
        {
            return RwisClassifier.FromTable(Template()["TBL_Warning_Classes"]);
        }

        public static string[] Classify(RwisClassifier c, string warningText)
        {
            return c.Classify(RwisText.Normalize(warningText));
        }
    }

    public class TemplateTests
    {
        [Fact]
        public void Template_HasTheConfigurationTables()
        {
            var t = Fixture.Template();
            foreach (string n in new[] { "TBL_Warning_Classes", "TBL_Taxonomy", "TBL_Severity", "TBL_Assignment_Rules", "TBL_Fix_Guide", "TBL_Roles_List" })
            {
                Assert.True(t.ContainsKey(n), n);
                Assert.True(t[n].Count >= 2, n + " has no body rows");
            }
            Assert.True(t["TBL_Warning_Classes"].Count > 50);
        }

        [Fact]
        public void Template_ShipsWithoutWarningRows()
        {
            var t = Fixture.Template();
            Assert.True(t["TBL_Warnings_Master"].Count <= 1);
            Assert.True(t["TBL_Snapshot_Log"].Count <= 1);
        }

        [Fact]
        public void Template_SeverityTableCoversAllFourLabels()
        {
            var labels = Fixture.Template()["TBL_Severity"].Skip(1).Select(r => XlsxIO.C(r, 0).ToUpperInvariant()).ToList();
            foreach (string s in RwisSeverity.Order) Assert.Contains(s, labels);
        }
    }

    public class ClassifierTests
    {
        [Theory]
        [InlineData("Highlighted walls overlap.", "WC-010", "T03", "HIGH", "walls overlap")]
        [InlineData("Highlighted floors overlap.", "WC-011", "T03", "HIGH", "floors overlap")]
        [InlineData("There are identical instances in the same place in the project.", "WC-007", "T02", "CRITICAL", "identical instances in the same place")]
        [InlineData("Room is not in a properly enclosed region.", "WC-017", "T04", "HIGH", "room is not in a properly enclosed region")]
        [InlineData("Two elements were not automatically joined", "WC-029", "T06", "MEDIUM", "two elements were not automatically joined")]
        [InlineData("Wall slightly off axis", "WC-055", "T12", "MEDIUM", "slightly off axis")]
        public void Classify_UsesTheLongestMatchingKeyword(string text, string classId, string taxonomy, string severity, string keyword)
        {
            string[] r = Fixture.Classify(Fixture.Classifier(), text);
            Assert.Equal(classId, r[0]);
            Assert.Equal(taxonomy, r[2]);
            Assert.Equal(severity, r[3]);
            Assert.Equal(keyword, r[5]);
        }

        [Fact]
        public void Classify_LongerKeywordBeatsShorterOne()
        {
            // "slightly off axis" (WC-055) is also inside this text, but the room separation keyword is longer
            string[] r = Fixture.Classify(Fixture.Classifier(), "Room separation line is slightly off axis and may produce inaccurate results.");
            Assert.Equal("WC-060", r[0]);
            Assert.Equal("LOW", r[3]);
        }

        [Fact]
        public void Classify_QuotesAndCaseAreIgnored()
        {
            string[] r = Fixture.Classify(Fixture.Classifier(), "ELEMENTS HAVE DUPLICATE \"Mark\" VALUES.");
            Assert.Equal("T01", r[2]);
            Assert.Equal("HIGH", r[3]);
        }

        [Fact]
        public void Classify_AliasMapsRevitWordingToTheTemplateClass()
        {
            var c = Fixture.Classifier();
            string[] a = Fixture.Classify(c, "Room Tag is outside of its room.");
            Assert.Equal("T14", a[2]);
            Assert.Equal("room tag is outside", a[5]);
            string[] b = Fixture.Classify(c, "Highlighted elements are joined but do not intersect.");
            Assert.Equal("WC-029", b[0]);
            string[] d = Fixture.Classify(c, "Can't keep elements joined");   // apostrophe is stripped on both sides
            Assert.Equal("WC-029", d[0]);
            Assert.Equal("cant keep elements joined", d[5]);
        }

        [Fact]
        public void Classify_UnknownTextIsUnclassifiedMedium()
        {
            string[] r = Fixture.Classify(Fixture.Classifier(), "Something that no table knows about.");
            Assert.Equal("", r[0]);
            Assert.Equal("Unclassified", r[1]);
            Assert.Equal("-", r[2]);
            Assert.Equal("MEDIUM", r[3]);
            Assert.Equal("", r[5]);
        }

        [Fact]
        public void Classify_InvalidDefaultSeverityFallsBackToMedium()
        {
            var table = new List<string[]>
            {
                new[] { "Class_ID", "Keyword_Match", "Class_Name", "Taxonomy_ID", "Default_Severity", "Revit_Source", "Notes" },
                new[] { "X-1", "widget is broken", "Broken Widget", "T99", "SEVERE", "Test", "synthetic" },
                new[] { "X-2", "", "No keyword", "T98", "LOW", "Test", "skipped: empty keyword" }
            };
            var c = RwisClassifier.FromTable(table);
            string[] r = c.Classify("the widget is broken here");
            Assert.Equal("X-1", r[0]);
            Assert.Equal("MEDIUM", r[3]);
            Assert.Equal("synthetic", r[4]);
            Assert.Equal("Unclassified", c.Classify("no keyword")[1]);
        }

        [Fact]
        public void Classify_EveryTemplateKeywordClassifiesItself()
        {
            // a text made of exactly a class keyword must land on a class whose keyword is at least as long
            var c = Fixture.Classifier();
            foreach (string[] row in RwisClassifier.BodyRows(Fixture.Template()["TBL_Warning_Classes"]))
            {
                string kw = XlsxIO.C(row, 1).Trim().ToLowerInvariant().Replace("'", "");
                string[] r = c.Classify(kw);
                Assert.NotEqual("Unclassified", r[1]);
                Assert.True(r[5].Length >= kw.Length, kw);
            }
        }
    }

    public class SeverityTests
    {
        [Fact]
        public void Rank_OrdersCriticalFirst()
        {
            Assert.Equal(4, RwisSeverity.Rank("CRITICAL"));
            Assert.Equal(3, RwisSeverity.Rank("HIGH"));
            Assert.Equal(2, RwisSeverity.Rank("MEDIUM"));
            Assert.Equal(1, RwisSeverity.Rank("LOW"));
            Assert.Equal(1, RwisSeverity.Rank("anything else"));
            Assert.True(RwisSeverity.Order.Select(RwisSeverity.Rank).SequenceEqual(new[] { 4, 3, 2, 1 }));
        }

        [Theory]
        [InlineData("", 4)]
        [InlineData("All severities", 4)]
        [InlineData("CRITICAL only", 1)]
        [InlineData("HIGH only", 1)]
        [InlineData("MEDIUM only", 1)]
        [InlineData("LOW only", 1)]
        [InlineData("CRITICAL + HIGH", 2)]
        [InlineData("MEDIUM + LOW", 2)]
        public void Parse_MatchesTheRoutineJsonOptions(string text, int count)
        {
            Assert.Equal(count, RwisSeverity.Parse(text).Count);
        }

        [Fact]
        public void Parse_ContainsTheNamedSeverities()
        {
            var s = RwisSeverity.Parse("critical + high");
            Assert.Contains("CRITICAL", s);
            Assert.Contains("HIGH", s);
            Assert.DoesNotContain("LOW", s);
            Assert.Equal(4, RwisSeverity.Parse(null).Count);
        }

        [Theory]
        [InlineData("1. Review - 3D view per warning type (no changes)", 1)]
        [InlineData("2. Solve the warning type in the active WRN 3D view", 2)]
        [InlineData("3. Solve - step through each warning type (confirm each)", 3)]
        [InlineData("4. Solve ALL warnings at once (hurry mode)", 4)]
        [InlineData("", 1)]
        [InlineData(null, 1)]
        public void ParseAction_ReadsTheLeadingNumber(string text, int action)
        {
            Assert.Equal(action, RwisSeverity.ParseAction(text));
        }
    }

    public class GroupingTests
    {
        [Fact]
        public void OrderKeys_SeverityFirstThenText()
        {
            var texts = new List<string> { "b warning", "a warning", "c warning", "a warning", "d warning" };
            var sevs = new List<string> { "LOW", "HIGH", "CRITICAL", "HIGH", "HIGH" };
            var keys = RwisGrouping.OrderKeys(texts, sevs);
            Assert.Equal(new[] { "c warning", "a warning", "d warning", "b warning" }, keys.ToArray());
        }

        [Fact]
        public void OrderKeys_GroupsIdenticalTextsOnce()
        {
            var keys = RwisGrouping.OrderKeys(new List<string> { "x", "x", "x" }, new List<string> { "MEDIUM", "MEDIUM", "MEDIUM" });
            Assert.Single(keys);
        }

        [Fact]
        public void OrderKeys_EmptyInput()
        {
            Assert.Empty(RwisGrouping.OrderKeys(new List<string>(), new List<string>()));
        }
    }

    public class TextTests
    {
        [Fact]
        public void Normalize_LowersAndRemovesQuotes()
        {
            Assert.Equal("cant x is", RwisText.Normalize("Can't \"X\" IS"));
        }

        [Fact]
        public void HashOf_IsStableAndIgnoresIdOrder()
        {
            string h1 = RwisText.HashOf("Highlighted walls overlap.", new long[] { 30, 12 });
            string h2 = RwisText.HashOf("  highlighted WALLS overlap.  ", new long[] { 12, 30 });
            Assert.Equal(h1, h2);
            Assert.Equal("RWIS-FA5B40C674E3", h1);   // md5("highlighted walls overlap.|12,30") first 12 hex digits, upper case
            Assert.NotEqual(h1, RwisText.HashOf("Highlighted walls overlap.", new long[] { 12, 31 }));
            Assert.Equal(17, h1.Length);
        }

        [Fact]
        public void Clean_RemovesForbiddenViewNameCharacters()
        {
            Assert.Equal("WRN 01 HIGH a b c", RwisText.Clean("WRN 01: HIGH [a]/b|c"));
            Assert.Equal("a b", RwisText.Clean("a   b "));
        }

        [Fact]
        public void Trunc_CutsAtFirstSentenceAndLength()
        {
            Assert.Equal("Highlighted walls overlap", RwisText.Trunc("Highlighted walls overlap. They are too close.", 80));
            Assert.Equal("Short. Text", RwisText.Trunc("Short. Text", 80));          // first dot is within the first 15 characters: kept
            Assert.Equal("abcde...", RwisText.Trunc("abcdefghij", 5));
        }
    }

    public class XlsxTests
    {
        private static readonly string[] SheetNames = { "RWIS_Summary", "All_Warnings", "AICon_Change_Log", "Snapshot_Log", "Column_Guide", "TBL_Warning_Classes", "TBL_Taxonomy", "TBL_Severity", "TBL_Assignment_Rules", "TBL_Fix_Guide", "TBL_Roles_List" };

        private static string Sheet(List<object[]> rows)
        {
            return XlsxWriter.SheetXml(rows, (r, c) => r == 0 ? 1 : 9, XlsxWriter.ColsOf(new double[] { 10, 20, 30 }), XlsxWriter.FreezeView(0, 1), r => "", "");
        }

        [Fact]
        public void ColL_ConvertsIndexesToLetters()
        {
            Assert.Equal("A", XlsxWriter.ColL(0));
            Assert.Equal("Z", XlsxWriter.ColL(25));
            Assert.Equal("AA", XlsxWriter.ColL(26));
            Assert.Equal("AJ", XlsxWriter.ColL(35));
        }

        [Fact]
        public void WriteThenRead_RoundTripsTextNumbersAndSpecialCharacters()
        {
            var all = new List<object[]>
            {
                new object[] { "Warning_ID", "Text", "Number" },
                new object[] { "WRN-20990101-0001", "a < b & \"c\" 'd' > e", 12.5 },
                new object[] { "WRN-20990101-0002", "  leading and trailing  ", (double)3 },
                new object[] { "WRN-20990101-0003", "unicode: éü العربية", 0.001 },
                new object[] { "", "gap before this cell", "" }
            };
            var bodies = new List<string>();
            for (int i = 0; i < SheetNames.Length; i++) bodies.Add(Sheet(i == 1 ? all : new List<object[]> { new object[] { "H" + i, "x" } }));
            var parts = XlsxWriter.BuildPackage(SheetNames, bodies, XlsxWriter.TabColors, "All_Warnings!$A$1:$C$5");

            byte[] zip = XlsxIO.ZipBytes(parts);
            var read = XlsxIO.ReadXlsx(XlsxIO.Unzip(zip));

            Assert.Equal(SheetNames, read.Keys.ToArray());
            var rows = read["All_Warnings"];
            Assert.Equal(5, rows.Count);
            Assert.Equal("Warning_ID", XlsxIO.C(rows[0], 0));
            Assert.Equal("a < b & \"c\" 'd' > e", XlsxIO.C(rows[1], 1));
            Assert.Equal("12.5", XlsxIO.C(rows[1], 2));
            Assert.Equal("  leading and trailing  ", XlsxIO.C(rows[2], 1));
            Assert.Equal("3", XlsxIO.C(rows[2], 2));
            Assert.Equal("0.001", XlsxIO.C(rows[3], 2));
            Assert.Equal("unicode: éü العربية", XlsxIO.C(rows[3], 1));
            Assert.Equal("", XlsxIO.C(rows[4], 0));
            Assert.Equal("gap before this cell", XlsxIO.C(rows[4], 1));
            Assert.Equal("H5", XlsxIO.C(read["TBL_Warning_Classes"][0], 0));
        }

        [Fact]
        public void WrittenZip_IsAValidZipPackage()
        {
            var parts = XlsxWriter.BuildPackage(SheetNames, SheetNames.Select(n => Sheet(new List<object[]> { new object[] { n } })).ToList(), XlsxWriter.TabColors, "All_Warnings!$A$1:$C$1");
            byte[] zip = XlsxIO.ZipBytes(parts);
            using (var za = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                Assert.Equal(parts.Count, za.Entries.Count);
                Assert.NotNull(za.GetEntry("[Content_Types].xml"));
                Assert.NotNull(za.GetEntry("xl/workbook.xml"));
                Assert.NotNull(za.GetEntry("xl/styles.xml"));
                using (var sr = new StreamReader(za.GetEntry("xl/styles.xml").Open()))
                    Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" + XlsxStyles.Styles, sr.ReadToEnd());
            }
        }

        [Fact]
        public void Package_HasOneWorksheetPerSheetAndTabColours()
        {
            var parts = XlsxWriter.BuildPackage(SheetNames, SheetNames.Select(n => Sheet(new List<object[]> { new object[] { n } })).ToList(), XlsxWriter.TabColors, "All_Warnings!$A$1:$C$1");
            Assert.Equal(7 + SheetNames.Length, parts.Count);   // 7 fixed parts + 11 sheets
            Assert.Contains("<tabColor rgb=\"FF1A252F\" />", parts.First(p => p.Key == "xl/worksheets/sheet1.xml").Value);
            Assert.Contains("<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"1\" hidden=\"1\">All_Warnings!$A$1:$C$1</definedName>", parts.First(p => p.Key == "xl/workbook.xml").Value);
        }

        [Fact]
        public void Unzip_ReadsTheDeflatedTemplate()
        {
            var parts = XlsxIO.UnzipFile(Fixture.TemplatePath);
            Assert.True(parts.ContainsKey("xl/workbook.xml"));
            Assert.True(parts["xl/workbook.xml"].Length > 100);
        }

        [Fact]
        public void Unzip_RejectsNonZipData()
        {
            Assert.Throws<Exception>(() => XlsxIO.Unzip(new byte[100]));
        }

        [Fact]
        public void ToNum_ParsesInvariantAndFallsBackToZero()
        {
            Assert.Equal(1.5, XlsxIO.ToNum("1.5"));
            Assert.Equal(0, XlsxIO.ToNum("abc"));
            Assert.Equal(0, XlsxIO.ToNum(null));
        }
    }
}
