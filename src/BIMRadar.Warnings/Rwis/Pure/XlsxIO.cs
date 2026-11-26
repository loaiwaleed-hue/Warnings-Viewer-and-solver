using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;

namespace BIMRadar.Warnings.Rwis.Pure
{
    /// <summary>
    /// Minimal xlsx reader / zip writer (no Revit types). Ported verbatim from AICon routine isolate-warnings-rwis-v7:
    /// unzip, zipWrite, readXlsx, C, toNum. Only change: byte[] and Stream overloads so the embedded template can be read without a file.
    /// </summary>
    internal static class XlsxIO
    {
        public const string NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string RelNS = "http://schemas.openxmlformats.org/package/2006/relationships";
        private const string DocRelNS = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        // ---------------- zip: read ----------------
        public static Dictionary<string, byte[]> UnzipFile(string path)
        {
            return Unzip(File.ReadAllBytes(path));
        }

        public static Dictionary<string, byte[]> Unzip(byte[] z)
        {
            var res = new Dictionary<string, byte[]>();
            int eocd = -1;
            for (int i = z.Length - 22; i >= Math.Max(0, z.Length - 65557); i--) if (BitConverter.ToUInt32(z, i) == 0x06054b50) { eocd = i; break; }
            if (eocd < 0) throw new Exception("not an xlsx file");
            int n = BitConverter.ToUInt16(z, eocd + 10), p = (int)BitConverter.ToUInt32(z, eocd + 16);
            for (int k = 0; k < n; k++)
            {
                int method = BitConverter.ToUInt16(z, p + 10), csize = (int)BitConverter.ToUInt32(z, p + 20);
                int nl = BitConverter.ToUInt16(z, p + 28), el = BitConverter.ToUInt16(z, p + 30), cl = BitConverter.ToUInt16(z, p + 32);
                int lho = (int)BitConverter.ToUInt32(z, p + 42);
                string name = Encoding.UTF8.GetString(z, p + 46, nl);
                p += 46 + nl + el + cl;
                int ds = lho + 30 + BitConverter.ToUInt16(z, lho + 26) + BitConverter.ToUInt16(z, lho + 28);
                byte[] data;
                if (method == 0) { data = new byte[csize]; Buffer.BlockCopy(z, ds, data, 0, csize); }
                else
                {
                    using (var ms = new MemoryStream(z, ds, csize))
                    using (var df = new DeflateStream(ms, CompressionMode.Decompress))
                    using (var om = new MemoryStream()) { df.CopyTo(om); data = om.ToArray(); }
                }
                res[name] = data;
            }
            return res;
        }

        // ---------------- zip: write ----------------
        private static readonly uint[] CRCT = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++) { uint c = i; for (int j = 0; j < 8; j++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[i] = c; }
            return t;
        }

        public static void ZipWrite(string path, List<KeyValuePair<string, string>> parts)
        {
            using (var fs = new FileStream(path, FileMode.Create)) WriteZip(fs, parts);
        }

        public static byte[] ZipBytes(List<KeyValuePair<string, string>> parts)
        {
            using (var ms = new MemoryStream()) { WriteZip(ms, parts); return ms.ToArray(); }
        }

        private static void WriteZip(Stream fs, List<KeyValuePair<string, string>> parts)
        {
            using (var bw = new BinaryWriter(fs, new UTF8Encoding(false), true))
            {
                var cdir = new MemoryStream(); var cw = new BinaryWriter(cdir);
                foreach (var part in parts)
                {
                    byte[] raw = Encoding.UTF8.GetBytes(part.Value), nb = Encoding.UTF8.GetBytes(part.Key);
                    uint crc = 0xFFFFFFFF; foreach (byte b in raw) crc = CRCT[(crc ^ b) & 0xFF] ^ (crc >> 8); crc ^= 0xFFFFFFFF;
                    byte[] comp;
                    using (var ms = new MemoryStream()) { using (var df = new DeflateStream(ms, CompressionMode.Compress, true)) df.Write(raw, 0, raw.Length); comp = ms.ToArray(); }
                    bw.Flush();
                    uint off = (uint)fs.Position;
                    bw.Write(0x04034b50u); bw.Write((ushort)20); bw.Write((ushort)0x0800); bw.Write((ushort)8); bw.Write((ushort)0); bw.Write((ushort)0x21);
                    bw.Write(crc); bw.Write((uint)comp.Length); bw.Write((uint)raw.Length); bw.Write((ushort)nb.Length); bw.Write((ushort)0); bw.Write(nb); bw.Write(comp);
                    cw.Write(0x02014b50u); cw.Write((ushort)20); cw.Write((ushort)20); cw.Write((ushort)0x0800); cw.Write((ushort)8); cw.Write((ushort)0); cw.Write((ushort)0x21);
                    cw.Write(crc); cw.Write((uint)comp.Length); cw.Write((uint)raw.Length); cw.Write((ushort)nb.Length); cw.Write((ushort)0); cw.Write((ushort)0); cw.Write((ushort)0); cw.Write((ushort)0); cw.Write(0u); cw.Write(off); cw.Write(nb);
                }
                bw.Flush();
                uint cdOff = (uint)fs.Position; byte[] cdb = cdir.ToArray(); bw.Write(cdb);
                bw.Write(0x06054b50u); bw.Write((ushort)0); bw.Write((ushort)0); bw.Write((ushort)parts.Count); bw.Write((ushort)parts.Count); bw.Write((uint)cdb.Length); bw.Write(cdOff); bw.Write((ushort)0);
                bw.Flush();
            }
        }

        // ---------------- xlsx: read ----------------
        /// <summary>sheet name -> rows (each row a string[]; missing cells are null).</summary>
        public static Dictionary<string, List<string[]>> ReadXlsx(Dictionary<string, byte[]> parts)
        {
            var result = new Dictionary<string, List<string[]>>();
            Func<string, XmlDocument> X = nm2 => { var d = new XmlDocument(); using (var ms = new MemoryStream(parts[nm2])) d.Load(ms); return d; };
            var shared = new List<string>();
            if (parts.ContainsKey("xl/sharedStrings.xml"))
                foreach (XmlElement si in X("xl/sharedStrings.xml").GetElementsByTagName("si", NS))
                {
                    var sb = new StringBuilder();
                    foreach (XmlElement t in si.GetElementsByTagName("t", NS)) sb.Append(t.InnerText);
                    shared.Add(sb.ToString());
                }
            var rels = new Dictionary<string, string>();
            foreach (XmlElement r in X("xl/_rels/workbook.xml.rels").GetElementsByTagName("Relationship", RelNS))
            {
                string t = r.GetAttribute("Target"); rels[r.GetAttribute("Id")] = t.StartsWith("/") ? t.Substring(1) : "xl/" + t;
            }
            foreach (XmlElement sh in X("xl/workbook.xml").GetElementsByTagName("sheet", NS))
            {
                string rid = sh.GetAttribute("id", DocRelNS);
                if (!rels.ContainsKey(rid) || !parts.ContainsKey(rels[rid])) continue;
                var rows = new List<string[]>();
                foreach (XmlElement row in X(rels[rid]).GetElementsByTagName("row", NS))
                {
                    int rIdx = rows.Count; int tmp; if (int.TryParse(row.GetAttribute("r"), out tmp)) rIdx = tmp - 1;
                    var cells = new Dictionary<int, string>(); int auto = 0;
                    foreach (XmlElement c in row.GetElementsByTagName("c", NS))
                    {
                        string cref = c.GetAttribute("r"); int col = 0;
                        foreach (char ch in cref) { if (char.IsLetter(ch)) col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1); else break; }
                        col = col > 0 ? col - 1 : auto; auto = col + 1;
                        string t = c.GetAttribute("t"), val = null;
                        if (t == "inlineStr") val = c.InnerText;
                        else
                        {
                            var v = c.GetElementsByTagName("v", NS);
                            if (v.Count > 0) { val = v[0].InnerText; if (t == "s") val = shared[int.Parse(val)]; else if (t == "b") val = val == "1" ? "True" : "False"; }
                        }
                        if (val != null) cells[col] = val;
                    }
                    while (rows.Count < rIdx) rows.Add(new string[0]);
                    var arr = new string[cells.Count > 0 ? cells.Keys.Max() + 1 : 0];
                    foreach (var kv in cells) arr[kv.Key] = kv.Value;
                    if (rows.Count == rIdx) rows.Add(arr);
                }
                result[sh.GetAttribute("name")] = rows;
            }
            return result;
        }

        /// <summary>Cell i of row r as text; "" when the row or cell is missing.</summary>
        public static string C(string[] r, int i)
        {
            return (r != null && i < r.Length && r[i] != null) ? r[i] : "";
        }

        public static double ToNum(string s)
        {
            double v;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }
    }
}
