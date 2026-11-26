using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BIMRadar.Warnings.Rwis.Pure
{
    /// <summary>
    /// SpreadsheetML writing helpers (no Revit types). Ported verbatim from AICon routine isolate-warnings-rwis-v7
    /// (colL, xe, cellX, colsOf, freezeView, sheetXml and the package assembly at the end of the report section).
    /// </summary>
    internal static class XlsxWriter
    {
        private static readonly CultureInfo IC = CultureInfo.InvariantCulture;
        private const string NS = XlsxIO.NS;

        /// <summary>0-based column index to letters (0 = A, 26 = AA).</summary>
        public static string ColL(int ci)
        {
            string s = ""; ci++;
            while (ci > 0) { int m = (ci - 1) % 26; s = (char)('A' + m) + s; ci = (ci - 1) / 26; }
            return s;
        }

        public static string Xe(string s)
        {
            return System.Security.SecurityElement.Escape(s ?? "");
        }

        /// <summary>One cell. v: null / "" (empty), double / int (number) or anything else (inline string). st = style index (0 = none).</summary>
        public static string CellX(object v, string cref, int st)
        {
            string sa = st > 0 ? " s=\"" + st + "\"" : "";
            if (v == null || (v is string && ((string)v).Length == 0)) return st > 0 ? "<c r=\"" + cref + "\"" + sa + " />" : "";
            if (v is double || v is int) return "<c r=\"" + cref + "\"" + sa + " t=\"n\"><v>" + Convert.ToDouble(v).ToString("R", IC) + "</v></c>";
            return "<c r=\"" + cref + "\"" + sa + " t=\"inlineStr\"><is><t xml:space=\"preserve\">" + Xe(v.ToString()) + "</t></is></c>";
        }

        public static string ColsOf(double[] ws)
        {
            var sb = new StringBuilder("<cols>");
            for (int q = 0; q < ws.Length; q++) sb.Append("<col min=\"" + (q + 1) + "\" max=\"" + (q + 1) + "\" width=\"" + ws[q].ToString(IC) + "\" customWidth=\"1\" />");
            return sb.Append("</cols>").ToString();
        }

        public static string FreezeView(int xs, int ys)
        {
            return "<sheetViews><sheetView workbookViewId=\"0\"" + (xs == 0 && ys == 0 ? " showGridLines=\"0\"" : "") + ">"
                + (ys > 0 ? "<pane " + (xs > 0 ? "xSplit=\"" + xs + "\" " : "") + "ySplit=\"" + ys + "\" topLeftCell=\"" + ColL(xs) + (ys + 1) + "\" activePane=\"" + (xs > 0 ? "bottomRight" : "bottomLeft") + "\" state=\"frozen\" />" : "")
                + "</sheetView></sheetViews>";
        }

        public static string SheetXml(List<object[]> rows, Func<int, int, int> styleOf, string colsXml, string viewXml, Func<int, string> rowAttr, string tailXml)
        {
            int maxC = Math.Max(1, rows.Count > 0 ? rows.Max(r => r.Length) : 1);
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<worksheet xmlns=\"" + NS + "\">");
            sb.Append("<dimension ref=\"A1:" + ColL(maxC - 1) + Math.Max(1, rows.Count) + "\" />").Append(viewXml).Append("<sheetFormatPr defaultRowHeight=\"15\" />").Append(colsXml).Append("<sheetData>");
            for (int r = 0; r < rows.Count; r++)
            {
                sb.Append("<row r=\"" + (r + 1) + "\"" + rowAttr(r) + ">");
                for (int ci = 0; ci < rows[r].Length; ci++) sb.Append(CellX(rows[r][ci], ColL(ci) + (r + 1), styleOf(r, ci)));
                sb.Append("</row>");
            }
            sb.Append("</sheetData>").Append(tailXml).Append("<pageMargins left=\"0.5\" right=\"0.5\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\" /></worksheet>");
            return sb.ToString();
        }

        /// <summary>Tab colours of the 11 report sheets (v7 order).</summary>
        public static readonly string[] TabColors = { "1A252F", "C0392B", "27AE60", "2E4057", "7F8C8D", "5D6D7E", "5D6D7E", "5D6D7E", "5D6D7E", "5D6D7E", "5D6D7E" };

        /// <summary>
        /// Assembles the package parts (content types, rels, workbook, styles, sheets) exactly like v7.
        /// sheets[q] / bodies[q] / tabColors[q] describe sheet q+1; allWarningsFilterRange = "All_Warnings!$A$1:$AJ$n" for the hidden filter name of sheet 2.
        /// </summary>
        public static List<KeyValuePair<string, string>> BuildPackage(string[] sheets, List<string> bodies, string[] tabColors, string allWarningsFilterRange)
        {
            string X0 = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";
            var ct = new StringBuilder(X0 + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\" /><Default Extension=\"xml\" ContentType=\"application/xml\" /><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\" /><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\" /><Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\" /><Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\" />");
            var wbx = new StringBuilder(X0 + "<workbook xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns=\"" + NS + "\"><bookViews><workbookView activeTab=\"0\" /></bookViews><sheets>");
            var rel = new StringBuilder(X0 + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            var parts = new List<KeyValuePair<string, string>>();
            for (int q = 0; q < sheets.Length; q++)
            {
                ct.Append("<Override PartName=\"/xl/worksheets/sheet" + (q + 1) + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\" />");
                wbx.Append("<sheet name=\"" + sheets[q] + "\" sheetId=\"" + (q + 1) + "\" r:id=\"rId" + (q + 1) + "\" />");
                rel.Append("<Relationship Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet" + (q + 1) + ".xml\" Id=\"rId" + (q + 1) + "\" />");
                string body = bodies[q];
                body = body.Replace("<worksheet xmlns=\"" + NS + "\">", "<worksheet xmlns=\"" + NS + "\"><sheetPr><tabColor rgb=\"FF" + tabColors[q] + "\" /></sheetPr>");
                parts.Add(new KeyValuePair<string, string>("xl/worksheets/sheet" + (q + 1) + ".xml", body));
            }
            rel.Append("<Relationship Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\" Id=\"rId" + (sheets.Length + 1) + "\" /></Relationships>");
            ct.Append("</Types>");
            wbx.Append("</sheets><definedNames><definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"1\" hidden=\"1\">" + allWarningsFilterRange + "</definedName></definedNames></workbook>");
            string iso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            parts.Insert(0, new KeyValuePair<string, string>("[Content_Types].xml", ct.ToString()));
            parts.Insert(1, new KeyValuePair<string, string>("_rels/.rels", X0 + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\" /><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\" /><Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\" /></Relationships>"));
            parts.Insert(2, new KeyValuePair<string, string>("docProps/app.xml", X0 + "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Application>Microsoft Excel</Application></Properties>"));
            parts.Insert(3, new KeyValuePair<string, string>("docProps/core.xml", X0 + "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><dc:title>RWIS Warnings Report</dc:title><dc:creator>RWIS - AICon</dc:creator><dcterms:created xsi:type=\"dcterms:W3CDTF\">" + iso + "</dcterms:created><dcterms:modified xsi:type=\"dcterms:W3CDTF\">" + iso + "</dcterms:modified></cp:coreProperties>"));
            parts.Insert(4, new KeyValuePair<string, string>("xl/workbook.xml", wbx.ToString()));
            parts.Insert(5, new KeyValuePair<string, string>("xl/_rels/workbook.xml.rels", rel.ToString()));
            parts.Insert(6, new KeyValuePair<string, string>("xl/styles.xml", X0 + XlsxStyles.Styles));
            return parts;
        }
    }
}
