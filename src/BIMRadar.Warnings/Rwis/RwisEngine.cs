using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMRadar.Warnings.Rwis.Pure;

namespace BIMRadar.Warnings.Rwis
{
    /// <summary>
    /// RWIS Warning Manager engine: a port of AICon routine isolate-warnings-rwis-v7 (reference-source/Routine.cs).
    /// The script body lives in Run() almost verbatim (local lambdas kept) so that the behaviour is exactly v7.
    /// The changes are listed in docs/verification/T2-port.md. Section headers below keep the "// ===== X =====" names of the script.
    /// Must be called from a valid Revit API context (IExternalCommand / ExternalEvent handler).
    /// </summary>
    public static class RwisEngine
    {
        /// <summary>Separator between the scalar summary lines and the per-type lines in the text returned by Run().</summary>
        public const string TypesMarker = "Warning types:";

        private const string TemplateResource = "BIMRadar.Warnings.RWIS_Template.xlsx";

        private static byte[] LoadTemplateBytes()
        {
            using (var s = typeof(RwisEngine).Assembly.GetManifestResourceStream(TemplateResource))
            {
                if (s == null) throw new Exception("RWIS template workbook not found (embedded resource " + TemplateResource + ")");
                using (var ms = new System.IO.MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); }
            }
        }

        /// <summary>True when the view is one of the "WRN .." 3D review views created by Review (marker "RWIS|..." in its Description). The check v7 action 2 uses.</summary>
        public static bool IsWrnView(View v)
        {
            if (v == null) return false;
            Parameter p = v.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION); string s = p != null ? p.AsString() : null;
            return s != null && s.StartsWith("RWIS|");
        }

        /// <summary>Runs the v7 routine for the options and returns the summary text (scalar lines, then TypesMarker and one line per warning type).</summary>
        public static string Run(UIApplication uiapp, RwisOptions opts)
        {
            if (uiapp == null) throw new ArgumentNullException("uiapp");
            if (opts == null) opts = new RwisOptions();
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null) throw new InvalidOperationException("No Revit project is open.");
            Document doc = uidoc.Document;

// ================= INPUTS =================
int action = opts.Action < 1 || opts.Action > 4 ? 1 : opts.Action;
bool allowPinned = opts.UnpinToFix;
bool skipConfirm = opts.SkipConfirmation;
bool skipViews = opts.Skip3DViews || opts.ReportOnly;
string filter = (opts.Contains ?? "").Trim();
bool skipReport = opts.SkipReport;

string[] ACTION_NAMES = { "", "Review - 3D view per warning type (no changes)", "Solve warning type of the active WRN 3D view", "Solve - step through each warning type", "Solve ALL warnings at once" };


Func<string, int> sevRank = RwisSeverity.Rank;
var sevAllowed = new HashSet<string>();
foreach (string s0 in RwisSeverity.Order) if (opts.Severities == null || opts.Severities.Contains(s0)) sevAllowed.Add(s0);

double TOL = 1.0 / 304.8;
double MAX_OFFAXIS = 0.5 * Math.PI / 180;
bool preview = true;
var touched = new HashSet<ElementId>();
var recDel = new List<ElementId>();          // elements a fix deletes (preview = would delete)
bool delAll = skipConfirm;                   // "delete all remaining without asking"
Color MAGENTA = new Color(255, 0, 255), KEEPGREEN = new Color(0, 170, 70);
ElementId solidFill = ElementId.InvalidElementId;
foreach (FillPatternElement fpe in new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)))
    if (fpe.GetFillPattern().IsSolidFill) { solidFill = fpe.Id; break; }

Func<ElementId, Element> get = id => { Element e = doc.GetElement(id); return (e != null && e.IsValidObject) ? e : null; };
Func<ElementId, string> sid = id => Compat.IdValue(id).ToString();
Func<Element, string> nm = e => (e.Category != null ? e.Category.Name.TrimEnd('s') : e.GetType().Name) + " " + sid(e.Id);
Func<double, string> deg = r => string.Format("{0:0.####}deg", Math.Abs(r) * 180 / Math.PI);
Func<Element, bool> inGroup = e => e.GroupId != ElementId.InvalidElementId;
Func<Element, string> pinBlock = e => (e.Pinned && !inGroup(e) && !allowPinned) ? nm(e) + " is PINNED - tick 'Unpin to fix' to allow" : null;
Func<Element, string> pinNote = e => (e.Pinned && !inGroup(e)) ? " (unpin -> fix -> re-pin)" : "";
Action<Element, Action> withUnpin = (e, act) => {
    bool p = e.Pinned && !inGroup(e);
    if (p) e.Pinned = false;
    try { act(); } finally { if (p) e.Pinned = true; }
    touched.Add(e.Id);
};
Func<Element, BuiltInCategory, bool> isCat = (e, c) => e.Category != null && Compat.IdValue(e.Category.Id) == (long)(int)c;
Func<Element, bool> isSketchLine = e => e is CurveElement && isCat(e, BuiltInCategory.OST_SketchLines);
Func<Element, bool> isSepLine = e => isCat(e, BuiltInCategory.OST_RoomSeparationLines) || isCat(e, BuiltInCategory.OST_MEPSpaceSeparationLines) || isCat(e, BuiltInCategory.OST_AreaSchemeLines);
Func<Element, bool> is3D = e => !(e is CurveElement) && !(e is SpatialElement) && !(e is Group) && e.Category != null && e.Category.CategoryType == CategoryType.Model;
// highlight what will be deleted (magenta + selected + zoom) and what is kept (green), then ask
Func<List<Element>, List<Element>, string, bool> confirmDelete = (delEls, keepEls, why) => {
    var geo = delEls.Where(is3D).ToList();
    if (geo.Count == 0 || delAll) return true;
    View av = doc.ActiveView;
    var saved = new Dictionary<ElementId, OverrideGraphicSettings>();
    Action<Element, Color> mark = (e, col) => {
        try {
            if (!saved.ContainsKey(e.Id)) saved[e.Id] = av.GetElementOverrides(e.Id);
            var o = new OverrideGraphicSettings();
            o.SetProjectionLineColor(col); o.SetCutLineColor(col); o.SetProjectionLineWeight(8); o.SetCutLineWeight(8);
            if (solidFill != ElementId.InvalidElementId) { o.SetSurfaceForegroundPatternId(solidFill); o.SetSurfaceForegroundPatternColor(col); o.SetCutForegroundPatternId(solidFill); o.SetCutForegroundPatternColor(col); }
            av.SetElementOverrides(e.Id, o);
        } catch { }
    };
    foreach (Element e in delEls) mark(e, MAGENTA);
    foreach (Element e in keepEls) mark(e, KEEPGREEN);
    bool visible = false;
    try { var inView = new HashSet<ElementId>(new FilteredElementCollector(doc, av.Id).ToElementIds()); visible = geo.Any(e => inView.Contains(e.Id)); } catch { }
    try {
        doc.Regenerate();
        uidoc.Selection.SetElementIds(geo.Select(e => e.Id).ToList());
        if (visible) uidoc.ShowElements(geo.Select(e => e.Id).Concat(keepEls.Select(e => e.Id)).ToList());
        uidoc.RefreshActiveView();
    } catch { }
    var td = new TaskDialog("RWIS - confirm delete");
    td.MainInstruction = "Delete " + geo.Count + " 3D element(s)?";
    td.MainContent = "WILL BE DELETED (magenta, selected):\n   " + string.Join("\n   ", geo.Select(e => nm(e) + (e is FamilyInstance ? " - " + ((FamilyInstance)e).Symbol.FamilyName + " : " + e.Name : " - " + e.Name)).ToArray())
        + (keepEls.Count > 0 ? "\n\nWILL BE KEPT (green):\n   " + string.Join("\n   ", keepEls.Select(e => nm(e) + " - " + e.Name).ToArray()) : "")
        + "\n\nWhy: " + why
        + (visible ? "" : "\n\nNot visible in the current view '" + av.Name + "'. Open its WRN 3D view to see it, or check the IDs above.");
    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Delete", "Delete the magenta element(s)");
    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Keep - don't delete", "Leave this warning open");
    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Delete this and all remaining 3D elements without asking");
    TaskDialogResult res0 = td.Show();
    foreach (var kv in saved) { try { av.SetElementOverrides(kv.Key, kv.Value); } catch { } }
    if (res0 == TaskDialogResult.CommandLink3) delAll = true;
    return res0 == TaskDialogResult.CommandLink1 || res0 == TaskDialogResult.CommandLink3;
};

// ================= GEOMETRY HELPERS =================
Func<Element, Curve> curveOf = e => {
    if (e.Location is LocationCurve) return ((LocationCurve)e.Location).Curve;
    if (e is Grid) return ((Grid)e).Curve;
    if (e is CurveElement) return ((CurveElement)e).GeometryCurve;
    return null;
};
Func<XYZ, double> devDir = d => {
    if (Math.Abs(d.Z) > 0.99) return double.NaN;
    double a = Math.Atan2(d.Y, d.X), q = Math.PI / 2;
    return a - Math.Round(a / q) * q;
};
Func<Element, double> devOf = e => {
    if (e is ReferencePlane) { XYZ n = ((ReferencePlane)e).Normal; if (Math.Abs(n.Z) > 0.99) return double.NaN; return devDir(new XYZ(-n.Y, n.X, 0)); }
    Line l = curveOf(e) as Line;
    return l == null ? double.NaN : devDir(l.Direction);
};
Func<Element, XYZ> midOf = e => {
    if (e is ReferencePlane) { ReferencePlane rp = (ReferencePlane)e; return (rp.BubbleEnd + rp.FreeEnd) / 2; }
    Curve c = curveOf(e); return (c.GetEndPoint(0) + c.GetEndPoint(1)) / 2;
};
Func<Curve, Curve, bool> containedIn = (small, big) => {
    for (int k = 0; k < 2; k++) {
        XYZ p = small.GetEndPoint(k); IntersectionResult r = big.Project(p);
        if (r == null || r.XYZPoint.DistanceTo(p) > TOL) return false;
    }
    return small.Length <= big.Length + TOL;
};
Func<Element, Element, bool> zContained = (a, b) => {
    BoundingBoxXYZ ba = a.get_BoundingBox(null), bb = b.get_BoundingBox(null);
    if (ba == null || bb == null) return false;
    return ba.Min.Z >= bb.Min.Z - TOL && ba.Max.Z <= bb.Max.Z + TOL;
};

// ================= FIX HANDLERS (preview = describe only) =================
Func<List<Element>, string> joinPairs = els => {
    var pairs = new List<string>(); int failed = 0;
    for (int i = 0; i < els.Count; i++)
        for (int j = i + 1; j < els.Count; j++) {
            if (JoinGeometryUtils.AreElementsJoined(doc, els[i], els[j])) continue;
            if (preview) { pairs.Add(nm(els[i]) + " + " + nm(els[j])); continue; }
            try { JoinGeometryUtils.JoinGeometry(doc, els[i], els[j]); pairs.Add(nm(els[i]) + " + " + nm(els[j])); touched.Add(els[i].Id); touched.Add(els[j].Id); } catch { failed++; }
        }
    if (pairs.Count == 0 && failed == 0) return "OK: already joined";
    if (pairs.Count == 0) return "MANUAL: Revit refused the join";
    return (preview ? "Join geometry " : "Joined ") + string.Join(", ", pairs.ToArray());
};
Func<List<Element>, string> unjoinPairs = els => {
    var pairs = new List<string>();
    for (int i = 0; i < els.Count; i++)
        for (int j = i + 1; j < els.Count; j++) {
            if (!JoinGeometryUtils.AreElementsJoined(doc, els[i], els[j])) continue;
            if (!preview) { try { JoinGeometryUtils.UnjoinGeometry(doc, els[i], els[j]); touched.Add(els[i].Id); touched.Add(els[j].Id); } catch { continue; } }
            pairs.Add(nm(els[i]) + " + " + nm(els[j]));
        }
    if (pairs.Count == 0) return "OK: not joined any more";
    return (preview ? "Unjoin " : "Unjoined ") + string.Join(", ", pairs.ToArray()) + " (they do not touch, so the join does nothing)";
};

Func<List<Element>, bool, string> removeContained = (els, checkZ) => {
    var del = new List<Element>(); var keepFor = new List<string>(); var keepEls = new List<Element>();
    for (int i = 0; i < els.Count; i++)
        for (int j = 0; j < els.Count; j++) {
            if (i == j || del.Contains(els[i]) || del.Contains(els[j])) continue;
            Curve ci = curveOf(els[i]), cj = curveOf(els[j]);
            if (ci == null || cj == null) continue;
            if (Math.Abs(ci.Length - cj.Length) < TOL && Compat.IdValue(els[i].Id) < Compat.IdValue(els[j].Id)) continue;
            if (containedIn(ci, cj) && (!checkZ || zContained(els[i], els[j]))) { del.Add(els[i]); keepFor.Add(nm(els[j])); if (!keepEls.Contains(els[j])) keepEls.Add(els[j]); }
        }
    if (del.Count == 0) return "MANUAL: partial overlap - nothing is fully inside the other";
    foreach (Element e in del) { string pb = pinBlock(e); if (pb != null) return "MANUAL: " + pb; }
    string txt = string.Join(", ", del.Select(nm).ToArray()) + " (fully inside " + string.Join(", ", keepFor.Distinct().ToArray()) + ")";
    if (preview) { recDel.AddRange(del.Select(e => e.Id)); return "Delete " + txt; }
    if (!confirmDelete(del, keepEls, "it lies completely inside the kept element (same line and height), so it only duplicates it")) return "MANUAL: Skipped by you - kept " + string.Join(", ", del.Select(nm).ToArray());
    recDel.AddRange(del.Select(e => e.Id));
    doc.Delete(del.Select(e => e.Id).ToList());
    return "Deleted " + txt;
};

Func<string, List<Element>, string> fixDuplicateValue = (d, els) => {
    bool typeMark = d.Contains("type mark");
    bool number = !typeMark && d.Contains("number");
    var targets = new List<Element>();
    foreach (Element e in els.OrderBy(x => Compat.IdValue(x.Id))) {
        Element t = e;
        if (typeMark && !(e is ElementType)) t = get(e.GetTypeId());
        if (t != null && !targets.Any(x => x.Id == t.Id)) targets.Add(t);
    }
    if (targets.Count < 2) return "OK: nothing to renumber";
    Func<Element, Parameter> P = e => {
        Parameter p = null;
        if (typeMark) p = e.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK);
        else if (number) { p = e.get_Parameter(BuiltInParameter.ROOM_NUMBER); if (p == null) p = e.LookupParameter("Number"); }
        else { p = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK); if (p == null) p = e.get_Parameter(BuiltInParameter.DOOR_NUMBER); }
        return p;
    };
    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var col = new FilteredElementCollector(doc);
    if (typeMark) col.WhereElementIsElementType(); else col.WhereElementIsNotElementType();
    if (targets[0].Category != null) col.OfCategoryId(targets[0].Category.Id);
    foreach (Element e in col) { Parameter p = P(e); if (p != null && p.HasValue) { string s = p.AsString(); if (!string.IsNullOrEmpty(s)) used.Add(s); } }
    var log = new List<string>();
    for (int i = 1; i < targets.Count; i++) {
        Parameter p = P(targets[i]);
        if (p == null || p.IsReadOnly) { log.Add(nm(targets[i]) + " read-only"); continue; }
        string baseV = p.AsString() ?? ""; int n = 2; string nv;
        do { nv = baseV + "-" + n; n++; } while (used.Contains(nv));
        used.Add(nv);
        if (!preview) { p.Set(nv); touched.Add(targets[i].Id); }
        log.Add(string.Format("{0} '{1}' -> '{2}'", nm(targets[i]), baseV, nv));
    }
    return (preview ? "Renumber " : "Renumbered ") + string.Join("; ", log.ToArray()) + " (keeps " + nm(targets[0]) + ")";
};

Func<List<Element>, string> fixIdentical = els => {
    var sorted = els.OrderBy(x => Compat.IdValue(x.Id)).ToList();
    if (sorted.Count < 2) return "OK: duplicate already gone";
    var del = sorted.Skip(1).ToList();
    foreach (Element e in del) { string pb = pinBlock(e); if (pb != null) return "MANUAL: " + pb; }
    string grp = inGroup(sorted[1]) ? " (member of group " + sid(sorted[1].GroupId) + ")" : "";
    string txt = string.Join(", ", del.Select(nm).ToArray()) + grp + ", keep " + nm(sorted[0]);
    if (preview) { recDel.AddRange(del.Select(e => e.Id)); return "Delete duplicate " + txt; }
    if (!confirmDelete(del, new List<Element> { sorted[0] }, "identical copy in the same place - it is counted twice in schedules")) return "MANUAL: Skipped by you - kept " + string.Join(", ", del.Select(nm).ToArray());
    recDel.AddRange(del.Select(e => e.Id));
    doc.Delete(del.Select(e => e.Id).ToList());
    return "Deleted duplicate " + txt;
};

Func<string, List<Element>, string> fixOverlap = (d, els) => {
    if (els.Count < 2) return "OK: already resolved";
    if (d.Contains("insert") || d.Contains("opening")) return "MANUAL";
    var walls = els.Where(e => e is Wall).ToList();
    var seps = els.Where(e => isSepLine(e)).ToList();
    if (walls.Count > 0 && seps.Count > 0) {
        bool bounding = walls.Any(wl => { Parameter p = wl.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING); return p != null && p.AsInteger() == 1; });
        if (!bounding) return "MANUAL: wall is not Room Bounding - keep the separation line or set the wall to Room Bounding";
        foreach (Element e in seps) { string pb = pinBlock(e); if (pb != null) return "MANUAL: " + pb; }
        string txt = string.Join(", ", seps.Select(nm).ToArray()) + " (" + string.Join(", ", walls.Select(nm).ToArray()) + " is room bounding, so rooms stay the same)";
        recDel.AddRange(seps.Select(e => e.Id));
        if (preview) return "Delete redundant room separation line " + txt;
        doc.Delete(seps.Select(e => e.Id).ToList());
        return "Deleted redundant room separation line " + txt;
    }
    if (walls.Count == els.Count) return removeContained(els, true);
    if (els.All(e => e is CurveElement)) return removeContained(els, false);
    return joinPairs(els);
};

Func<Element, Element, string> snapSketchLine = (L, host) => {
    Line l = ((CurveElement)L).GeometryCurve as Line;
    if (l == null) return "MANUAL: sketch line is not straight";
    double dv = devDir(l.Direction);
    if (double.IsNaN(dv) || Math.Abs(dv) < 1e-9) return "OK: sketch line " + sid(L.Id) + " already orthogonal";
    if (Math.Abs(dv) > MAX_OFFAXIS) return "MANUAL: sketch line " + sid(L.Id) + " is " + deg(dv) + " off - looks intentional";
    if (host != null) { string pb = pinBlock(host); if (pb != null) return "MANUAL: " + pb; }
    var lines = new List<CurveElement>();
    if (host != null)
        foreach (ElementId id in host.GetDependentElements(new ElementClassFilter(typeof(CurveElement))))
            if (id != L.Id) { CurveElement ce = get(id) as CurveElement; if (ce != null) lines.Add(ce); }
    XYZ A = l.GetEndPoint(0), B = l.GetEndPoint(1);
    XYZ s = Math.Abs(l.Direction.X) >= Math.Abs(l.Direction.Y) ? new XYZ(1, 0, 0) : new XYZ(0, 1, 0);
    for (int k = 0; k < 2; k++) {
        XYZ keep = k == 0 ? A : B, move = k == 0 ? B : A;
        CurveElement nb = lines.FirstOrDefault(c => c.GeometryCurve.GetEndPoint(0).DistanceTo(move) < TOL || c.GeometryCurve.GetEndPoint(1).DistanceTo(move) < TOL);
        XYZ np = null;
        if (nb == null) np = keep + s * (move - keep).DotProduct(s);
        else {
            Line nl = nb.GeometryCurve as Line; if (nl == null) continue;
            XYZ v = nl.Direction; double den = s.X * v.Y - s.Y * v.X;
            if (Math.Abs(den) < 1e-6) continue;
            XYZ w = move - keep; double t = (w.X * v.Y - w.Y * v.X) / den;
            np = new XYZ(keep.X + t * s.X, keep.Y + t * s.Y, move.Z);
        }
        if (np.DistanceTo(keep) < 10 * TOL) continue;
        string txt = string.Format("sketch line {0} of {1} ({2} off): one end moves {3:0.#} mm{4}{5}", sid(L.Id), host != null ? nm(host) : "sketch", deg(dv), np.DistanceTo(move) * 304.8,
            nb != null ? ", neighbour line " + sid(nb.Id) + " keeps its angle" : "", host != null ? pinNote(host) : "");
        if (preview) return "Straighten " + txt;
        CurveElement nbc = nb; XYZ mv = move, kp = keep, nwp = np; int kk = k;
        Action act = () => {
            ((CurveElement)L).SetGeometryCurve(kk == 0 ? Line.CreateBound(kp, nwp) : Line.CreateBound(nwp, kp), false);
            if (nbc != null) {
                Curve g = nbc.GeometryCurve; XYZ e0 = g.GetEndPoint(0), e1 = g.GetEndPoint(1);
                if (e0.DistanceTo(mv) <= e1.DistanceTo(mv)) nbc.SetGeometryCurve(Line.CreateBound(nwp, e1), false);
                else nbc.SetGeometryCurve(Line.CreateBound(e0, nwp), false);
            }
        };
        if (host != null) withUnpin(host, act); else act();
        touched.Add(L.Id);
        return "Straightened " + txt;
    }
    return "MANUAL: could not straighten - Edit Boundary and align the line";
};

Func<List<Element>, string> fixOffAxis = els => {
    var log = new List<string>();
    // 1) hosts with a location line (walls, beams...): rotating the host also fixes its profile sketch + ref planes
    var lineHosts = els.Where(e => !(e is CurveElement) && !(e is ReferencePlane) && e.Location is LocationCurve && curveOf(e) is Line).ToList();
    foreach (Element h in lineHosts) {
        double dv = devOf(h);
        if (double.IsNaN(dv)) continue;
        if (Math.Abs(dv) < 1e-9) { log.Add("OK: " + nm(h) + " already orthogonal"); continue; }
        if (Math.Abs(dv) > MAX_OFFAXIS) { log.Add("MANUAL: " + nm(h) + " is " + deg(dv) + " off - looks intentional"); continue; }
        string pb = pinBlock(h); if (pb != null) { log.Add("MANUAL: " + pb); continue; }
        string txt = string.Format("{0} by {1} about its midpoint{2}", nm(h), deg(dv), pinNote(h));
        if (preview) { log.Add("Rotate " + txt); continue; }
        XYZ mid = midOf(h); double rot = -dv; Element hh = h;
        withUnpin(h, () => ElementTransformUtils.RotateElement(doc, hh.Id, Line.CreateBound(mid, mid + XYZ.BasisZ), rot));
        log.Add("Rotated " + txt);
    }
    bool viaHost = lineHosts.Count > 0;
    Element sketchHost = els.FirstOrDefault(x => !(x is CurveElement) && !(x is ReferencePlane) && !(x.Location is LocationCurve));
    foreach (Element e in els) {
        if (lineHosts.Contains(e) || e == sketchHost) continue;
        if (viaHost && (e is ReferencePlane || isSketchLine(e))) continue;
        if (isSketchLine(e)) { log.Add(snapSketchLine(e, sketchHost)); continue; }
        if (e is ReferencePlane || e is Grid || e is CurveElement) {
            double dv = devOf(e);
            if (double.IsNaN(dv)) { log.Add("MANUAL: " + nm(e) + " not a straight line"); continue; }
            if (Math.Abs(dv) < 1e-9) { log.Add("OK: " + nm(e) + " already orthogonal"); continue; }
            if (Math.Abs(dv) > MAX_OFFAXIS) { log.Add("MANUAL: " + nm(e) + " is " + deg(dv) + " off - looks intentional"); continue; }
            string pb = pinBlock(e); if (pb != null) { log.Add("MANUAL: " + pb); continue; }
            string txt = string.Format("{0} by {1} about its midpoint{2}", nm(e), deg(dv), pinNote(e));
            if (preview) { log.Add("Rotate " + txt); continue; }
            XYZ mid = midOf(e); double rot = -dv; Element ee = e;
            withUnpin(e, () => ElementTransformUtils.RotateElement(doc, ee.Id, Line.CreateBound(mid, mid + XYZ.BasisZ), rot));
            log.Add("Rotated " + txt);
        }
    }
    if (log.Count == 0) return "MANUAL: nothing auto-fixable";
    var act = log.Where(x => !x.StartsWith("OK:")).ToList();
    if (act.Count == 0) return log[0];
    var man = act.Where(x => x.StartsWith("MANUAL")).ToList();
    if (man.Count == act.Count) return man[0];
    return string.Join("; ", act.Where(x => !x.StartsWith("MANUAL")).ToArray());
};

Func<List<Element>, string> fixTag = els => {
    var log = new List<string>();
    foreach (Element e in els) {
        SpatialElement host = null;
        if (e is RoomTag) host = ((RoomTag)e).Room;
        else if (e is Autodesk.Revit.DB.Mechanical.SpaceTag) host = ((Autodesk.Revit.DB.Mechanical.SpaceTag)e).Space;
        if (host == null) continue;
        LocationPoint hp = host.Location as LocationPoint, tp = e.Location as LocationPoint;
        if (hp == null || tp == null) continue;
        if (preview) { log.Add("Move tag " + sid(e.Id) + " to the centre of " + nm(host)); continue; }
        XYZ delta = new XYZ(hp.Point.X - tp.Point.X, hp.Point.Y - tp.Point.Y, 0);
        try { ElementTransformUtils.MoveElement(doc, e.Id, delta); touched.Add(e.Id); log.Add("Moved tag " + sid(e.Id) + " into " + nm(host)); }
        catch { try { ((SpatialElementTag)e).HasLeader = true; touched.Add(e.Id); log.Add("Leader enabled on tag " + sid(e.Id)); } catch { } }
    }
    return log.Count > 0 ? string.Join("; ", log.ToArray()) : "MANUAL";
};

Func<List<Element>, string> fixUnplaced = els => {
    var del = els.Where(e => e is SpatialElement && e.Location == null).ToList();
    if (del.Count == 0) return "MANUAL";
    string txt = string.Join(", ", del.Select(nm).ToArray());
    recDel.AddRange(del.Select(e => e.Id));
    if (preview) return "Delete unplaced " + txt;
    doc.Delete(del.Select(e => e.Id).ToList());
    return "Deleted unplaced " + txt;
};
Func<List<Element>, string> fixEmptyGroup = els => {
    var del = els.Where(e => e is Group && ((Group)e).GetMemberIds().Count == 0).ToList();
    if (del.Count == 0) return "MANUAL";
    recDel.AddRange(del.Select(e => e.Id));
    if (preview) return "Delete " + del.Count + " empty group(s)";
    doc.Delete(del.Select(e => e.Id).ToList());
    return "Deleted " + del.Count + " empty group(s)";
};

Func<string, string, string, List<Element>, string> dispatch = (d, kw, tax, els) => {
    recDel.Clear();
    try {
        if (els.Count == 0) return "OK: elements no longer exist";
        if (tax == "T01") return fixDuplicateValue(d, els);
        if ((tax == "T02" || tax == "T22") && (kw.Contains("identical") || kw.Contains("duplicate instances"))) return fixIdentical(els);
        if (tax == "T03") return fixOverlap(d, els);
        if (tax == "T04" && kw.Contains("deleted from all")) return fixUnplaced(els);
        if (tax == "T06" && kw.Contains("not automatically joined")) return joinPairs(els);
        if (tax == "T06" && (kw.Contains("do not intersect") || kw.Contains("keep elements joined"))) return unjoinPairs(els);
        if (tax == "T12") return fixOffAxis(els);
        if (tax == "T14" && kw.Contains("tag")) return fixTag(els);
        if (tax == "T21" && kw.Contains("group")) return fixEmptyGroup(els);
        return "MANUAL";
    } catch (Exception ex) { return "MANUAL: auto-fix failed - " + ex.Message; }
};
Func<string, bool> actionable = p => !p.StartsWith("MANUAL") && !p.StartsWith("OK:");

// ================= RWIS MASTER WORKBOOK: zip / xlsx helpers (pure code in Rwis/Pure/XlsxIO.cs) =================
Func<string, Dictionary<string, byte[]>> unzip = XlsxIO.UnzipFile;
Action<string, List<KeyValuePair<string, string>>> zipWrite = XlsxIO.ZipWrite;
Func<Dictionary<string, byte[]>, Dictionary<string, List<string[]>>> readXlsx = XlsxIO.ReadXlsx;
Func<string[], int, string> C = XlsxIO.C;
Func<string, double> toNum = XlsxIO.ToNum;

// ================= LOAD (or seed) THE MASTER WORKBOOK =================
string reportDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RWIS Reports");
string safeModel = new string(doc.Title.Select(ch => System.IO.Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
string wbPath = System.IO.Path.Combine(reportDir, safeModel + "_RWIS_Warnings_Report.xlsx");
var WB = new Dictionary<string, List<string[]>>();
string reportNote = "";
if (System.IO.File.Exists(wbPath)) {
    try { WB = readXlsx(unzip(wbPath)); }
    catch (Exception ex) { reportNote = "Existing workbook could not be read (" + ex.Message + ") - a new one was started. "; WB = new Dictionary<string, List<string[]>>(); }
}
string[] CFG = { "TBL_Warning_Classes", "TBL_Taxonomy", "TBL_Severity", "TBL_Assignment_Rules", "TBL_Fix_Guide", "TBL_Roles_List" };
var tplParts = XlsxIO.Unzip(LoadTemplateBytes());   // v7: file in %AppData%\AICon\routines\...; now the embedded RWIS_Template.xlsx
var tpl = readXlsx(tplParts);
foreach (string n2 in CFG) if ((!WB.ContainsKey(n2) || WB[n2].Count < 2) && tpl.ContainsKey(n2)) WB[n2] = tpl[n2];
var CLS = WB["TBL_Warning_Classes"].Skip(1).Where(r => C(r, 1).Trim().Length > 0).ToList();
// taxonomy-level fix text (meaning, causes, fix, alt, risk) from TBL_Fix_Guide
var TAX = new Dictionary<string, string[]>();
foreach (string[] fr in WB["TBL_Fix_Guide"].Skip(1)) {
    string[] cr = CLS.FirstOrDefault(x => C(x, 0) == C(fr, 1));
    if (cr != null && !TAX.ContainsKey(C(cr, 3))) TAX[C(cr, 3)] = new string[] { C(fr, 2), C(fr, 3), C(fr, 4), C(fr, 5), C(fr, 6) };
}
// keyword classification: Rwis/Pure/RwisClassifier.cs (ALIASES + classify)
var classifier = new RwisClassifier(CLS);
Func<string, string[]> classify = classifier.Classify;
Func<string, List<ElementId>, string> hashOf = (text, ids) => RwisText.HashOf(text, ids.Select(x => Compat.IdValue(x)));
Func<ElementId, string[]> elInfo = id => {
    Element e = doc.GetElement(id);
    if (e == null) return new string[] { "", "" };
    string cat = e.Category != null ? e.Category.Name : "", lvl = "";
    try { if (e.LevelId != ElementId.InvalidElementId) { Level lv = doc.GetElement(e.LevelId) as Level; if (lv != null) lvl = lv.Name; } } catch { }
    if (lvl == "")
        foreach (BuiltInParameter bp in new BuiltInParameter[] { BuiltInParameter.WALL_BASE_CONSTRAINT, BuiltInParameter.SCHEDULE_LEVEL_PARAM, BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM, BuiltInParameter.LEVEL_PARAM }) {
            try { Parameter p = e.get_Parameter(bp); if (p != null && p.HasValue) { string s = p.AsValueString(); if (!string.IsNullOrEmpty(s)) { lvl = s; break; } } } catch { }
        }
    return new string[] { cat, lvl };
};

// ================= COLLECT ALL WARNINGS (snapshot for the report) =================
var allWarnings = doc.GetWarnings();
var recText = new List<string>(); var recIds = new List<List<ElementId>>(); var recCls = new List<string[]>(); var recHash = new List<string>(); var recEl = new List<string[]>();
foreach (FailureMessage w in allWarnings) {
    string text = w.GetDescriptionText() ?? "";
    var ids = new List<ElementId>(w.GetFailingElements()); ids.AddRange(w.GetAdditionalElements()); ids = ids.Distinct().ToList();
    recText.Add(text); recIds.Add(ids);
    recCls.Add(classify(RwisText.Normalize(text)));
    recHash.Add(hashOf(text, ids));
    string[] e1 = ids.Count > 0 ? elInfo(ids[0]) : new string[] { "", "" }, e2 = ids.Count > 1 ? elInfo(ids[1]) : new string[] { "", "" };
    recEl.Add(new string[] { ids.Count > 0 ? sid(ids[0]) : "", ids.Count > 1 ? sid(ids[1]) : "", e1[0], e2[0], e1[1], e2[1] });
}

// planned fix for every warning (no model change)
preview = true;
var recPlan = new string[recText.Count];
var planDelAll = new HashSet<ElementId>();
for (int ri = 0; ri < recText.Count; ri++) {
    string p = dispatch(RwisText.Normalize(recText[ri]), recCls[ri][5], recCls[ri][2], recIds[ri].Select(get).Where(e => e != null).ToList());
    if (p == "MANUAL") { string[] tt = TAX.ContainsKey(recCls[ri][2]) ? TAX[recCls[ri][2]] : null; p = "MANUAL: " + (tt != null ? tt[2] : "review manually"); }
    recPlan[ri] = p;
    if (actionable(p)) foreach (ElementId x in recDel) planDelAll.Add(x);
}

// ================= WARNINGS IN SCOPE (for fixing) =================
var infos = new List<string[]>();          // text, d, className, tax, sev, note, kw, classId, hash
var infoIds = new List<List<ElementId>>();
for (int ri = 0; ri < recText.Count; ri++) {
    string text = recText[ri]; string d = RwisText.Normalize(text);
    string[] c = recCls[ri];
    if (!sevAllowed.Contains(c[3])) continue;
    if (filter.Length > 0 && d.IndexOf(filter.ToLowerInvariant()) < 0 && c[1].IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
        !string.Equals(c[2], filter, StringComparison.OrdinalIgnoreCase) && !string.Equals(c[3], filter, StringComparison.OrdinalIgnoreCase) && !string.Equals(c[0], filter, StringComparison.OrdinalIgnoreCase)) continue;
    infos.Add(new string[] { text, d, c[1], c[2], c[3], c[4], c[5], c[0], recHash[ri], ri.ToString() });
    infoIds.Add(recIds[ri]);
}
Func<int, List<Element>> elsOf = i => infoIds[i].Select(get).Where(e => e != null).ToList();

// groups = same warning text (exactly like Revit's dialog), ordered by severity
var groupKeys = RwisGrouping.OrderKeys(infos.Select(x => x[0]).ToList(), infos.Select(x => x[4]).ToList());
var groupItems = new Dictionary<string, List<int>>();
foreach (string k in groupKeys) groupItems[k] = Enumerable.Range(0, infos.Count).Where(i => infos[i][0] == k).ToList();

// planned fix for every single warning (no model change)
var plans = new string[infos.Count];
for (int i = 0; i < infos.Count; i++) plans[i] = recPlan[int.Parse(infos[i][9])];
var results = new string[infos.Count];
var resDel = new string[infos.Count];

// ================= 3D VIEWS =================
ElementId solid = ElementId.InvalidElementId;
foreach (FillPatternElement fp in new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)))
    if (fp.GetFillPattern().IsSolidFill) { solid = fp.Id; break; }
Color[] SEVCOL = { null, new Color(0, 120, 215), new Color(255, 200, 0), new Color(255, 120, 0), new Color(220, 0, 0) };
Color GREEN = new Color(0, 170, 70);
Action<View, ElementId, Color> paint = (v, id, c) => {
    try {
        var ogs = new OverrideGraphicSettings();
        ogs.SetProjectionLineColor(c); ogs.SetCutLineColor(c);
        if (solid != ElementId.InvalidElementId) { ogs.SetSurfaceForegroundPatternId(solid); ogs.SetSurfaceForegroundPatternColor(c); ogs.SetCutForegroundPatternId(solid); ogs.SetCutForegroundPatternColor(c); }
        v.SetElementOverrides(id, ogs);
    } catch { }
};
var allViews = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().ToList();
Func<View, string> keyOf = v => { Parameter p = v.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION); string s = p != null ? p.AsString() : null; return (s != null && s.StartsWith("RWIS|")) ? s.Substring(5) : null; };
Func<string, string> clean = RwisText.Clean;
Func<string, int, string> trunc = RwisText.Trunc;
Action<View, string> setName = (v, name) => {
    if (v.Name == name) return;
    string n = name; int k = 2;
    while (allViews.Any(x => x.Id != v.Id && x.Name == n)) { n = name + " (" + k + ")"; k++; }
    try { v.Name = n; } catch { }
};
ViewFamilyType vft3d = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);
var viewNames = new Dictionary<string, string>();
View firstView = null;

Func<string, bool, View3D> makeView = (key, done) => {
    if (skipViews || vft3d == null) return null;
    var items = groupItems[key];
    int num = groupKeys.IndexOf(key) + 1;
    string sev = infos[items[0]][4];
    View3D v = allViews.OfType<View3D>().FirstOrDefault(x => !x.IsTemplate && keyOf(x) == key);
    if (v == null) {
        v = View3D.CreateIsometric(doc, vft3d.Id);
        allViews.Add(v);
        try { v.ViewTemplateId = ElementId.InvalidElementId; } catch { }
        Parameter dp = v.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION); if (dp != null && !dp.IsReadOnly) dp.Set("RWIS|" + key);
        try { v.DisplayStyle = DisplayStyle.ShadingWithEdges; v.DetailLevel = ViewDetailLevel.Fine; } catch { }
    }
    setName(v, clean(string.Format("WRN {0:00} - {1} - {2}", num, done ? "DONE" : sev, trunc(key, 80))));
    var ids = items.SelectMany(i => infoIds[i]).Distinct().Where(id => get(id) != null).ToList();
    // section box around the problem (+1 m)
    double mn = 1000 / 304.8; XYZ lo = null, hi = null;
    foreach (ElementId id in ids) {
        BoundingBoxXYZ bb = get(id).get_BoundingBox(null); if (bb == null) continue;
        lo = lo == null ? bb.Min : new XYZ(Math.Min(lo.X, bb.Min.X), Math.Min(lo.Y, bb.Min.Y), Math.Min(lo.Z, bb.Min.Z));
        hi = hi == null ? bb.Max : new XYZ(Math.Max(hi.X, bb.Max.X), Math.Max(hi.Y, bb.Max.Y), Math.Max(hi.Z, bb.Max.Z));
    }
    if (lo != null) {
        try { var box = new BoundingBoxXYZ(); box.Min = lo - new XYZ(mn, mn, mn); box.Max = hi + new XYZ(mn, mn, mn); v.IsSectionBoxActive = true; v.SetSectionBox(box); } catch { }
    }
    try {
        if (v.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate)) v.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
        if (ids.Count > 0) v.IsolateElementsTemporary(ids);
    } catch { }
    foreach (ElementId id in ids) paint(v, id, done && touched.Contains(id) ? GREEN : (!done && planDelAll.Contains(id) ? MAGENTA : SEVCOL[sevRank(sev)]));
    viewNames[key] = v.Name;
    if (firstView == null) firstView = v;
    return v;
};

// room-boundary-only fixes (separation lines) never move or break a room -> applied without asking
Func<int, bool> roomOnly = i => { string p = plans[i]; if (!actionable(p)) return true; if (p.StartsWith("Delete redundant room separation line")) return true; var es = elsOf(i); return es.Count > 0 && es.All(e => isSepLine(e)); };
Func<string, bool> groupAuto = key => groupItems[key].Any(i => actionable(plans[i])) && groupItems[key].All(i => roomOnly(i));

// ================= DIALOG TEXT =================
Func<string, string> groupText = key => {
    var items = groupItems[key]; string[] f = infos[items[0]];
    string[] t = TAX.ContainsKey(f[3]) ? TAX[f[3]] : new string[] { "", "", "Review manually", "", "" };
    var sb = new System.Text.StringBuilder();
    sb.AppendLine(string.Format("Severity: {0}    RWIS: {1} {2}    Occurrences: {3}", f[4], f[3], f[2], items.Count));
    sb.AppendLine("Risk if ignored: " + t[4] + (f[5].Length > 0 ? "  (" + f[5] + ")" : ""));
    sb.AppendLine("RWIS recommended fix: " + t[2] + (t[3].Length > 0 ? "   |  Alt: " + t[3] : ""));
    sb.AppendLine();
    if (items.Any(i => actionable(plans[i]) && plans[i].StartsWith("Delete") && !roomOnly(i)))
        sb.AppendLine("Elements that will be deleted are MAGENTA in the WRN 3D view. Before any 3D element is deleted you will see it highlighted and be asked again.");
    sb.AppendLine("WHAT THE TOOL WILL DO:");
    int n = 0;
    foreach (int i in items) { n++; if (n > 8) break; sb.AppendLine(string.Format("  Warning {0}: {1}", n, plans[i])); }
    if (items.Count > 8) sb.AppendLine(string.Format("  ... {0} more - click 'Show details'", items.Count - 8));
    return sb.ToString();
};
Func<string, string> groupDetails = key => {
    var sb = new System.Text.StringBuilder(); int n = 0;
    foreach (int i in groupItems[key]) { n++; sb.AppendLine(string.Format("Warning {0} [ids {1}]: {2}", n, string.Join(", ", infoIds[i].Select(sid).ToArray()), plans[i])); }
    return sb.ToString();
};

var groupStatus = new Dictionary<string, string>();
Action<string> solveGroup = key => {
    preview = false;
    int ok = 0;
    foreach (int i in groupItems[key]) {
        if (!actionable(plans[i])) { results[i] = plans[i]; continue; }
        results[i] = dispatch(infos[i][1], infos[i][6], infos[i][3], elsOf(i));
        resDel[i] = actionable(results[i]) ? string.Join(" ", recDel.Select(sid).ToArray()) : "";
        if (actionable(results[i])) ok++;
    }
    int act = groupItems[key].Count(i => actionable(plans[i]));
    groupStatus[key] = act == 0 ? "MANUAL" : string.Format("FIXED {0}/{1}", ok, act);
    makeView(key, true);
};

// ================= RUN THE ACTION =================
string stopNote = "";
// One named transaction per run = one Ctrl+Z (AICon wrapped the v7 script in one). ReportOnly changes nothing in the model.
Transaction rwTx = null;
if (!opts.ReportOnly) { rwTx = new Transaction(doc, "RWIS Warnings - " + ACTION_NAMES[action]); rwTx.Start(); }
try {
if (action == 1) {
    foreach (string k in groupKeys) { makeView(k, false); groupStatus[k] = "REVIEW"; }
    // views of warnings that no longer exist -> mark DONE
    if (!opts.ReportOnly)
    foreach (View3D v in allViews.OfType<View3D>().ToList()) {
        string k = keyOf(v);
        if (k != null && !groupItems.ContainsKey(k) && !v.Name.Contains(" - DONE - ")) setName(v, clean("WRN -- - DONE - " + trunc(k, 80)));
    }
}
else if (action == 2) {
    string k = keyOf(doc.ActiveView);
    if (k == null) stopNote = "The active view is not a WRN review view. Run 'Review Warnings' first, open one of the 'WRN ..' 3D views, then run 'Solve This View' again.";
    else if (!groupItems.ContainsKey(k)) { stopNote = "This warning type no longer exists in the model - nothing to fix."; setName(doc.ActiveView, clean("WRN -- - DONE - " + trunc(k, 80))); }
    else {
        bool go = true;
        if (!skipConfirm && !groupAuto(k)) {
            var td = new TaskDialog("RWIS - fix warning type");
            td.MainInstruction = trunc(k, 200);
            td.MainContent = groupText(k);
            td.ExpandedContent = groupDetails(k);
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Fix this warning type", "Apply the changes listed above (one Ctrl+Z undoes them)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Cancel - change nothing");
            go = td.Show() == TaskDialogResult.CommandLink1;
        }
        if (go) solveGroup(k); else { groupStatus[k] = "CANCELLED"; stopNote = "Cancelled - nothing changed."; }
    }
}
else if (action == 3) {
    bool all = false, viewsOnly = false, skipRest = false; int idx = 0;
    foreach (string k in groupKeys) {
        idx++;
        bool hasFix = groupItems[k].Any(i => actionable(plans[i]));
        if (!hasFix) { groupStatus[k] = "MANUAL"; makeView(k, false); continue; }
        if (viewsOnly) { groupStatus[k] = "SKIPPED (review view)"; makeView(k, false); continue; }
        if (skipRest) { groupStatus[k] = "SKIPPED"; continue; }
        if (all || skipConfirm || groupAuto(k)) { solveGroup(k); continue; }
        var td = new TaskDialog(string.Format("RWIS - warning type {0} of {1}", idx, groupKeys.Count));
        td.MainInstruction = trunc(k, 200);
        td.MainContent = groupText(k);
        td.ExpandedContent = groupDetails(k);
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Fix this warning type", "Apply the changes listed above");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Skip - make a 3D view so I can review it", "No change; creates/updates its WRN 3D view");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Skip", "No change");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Skip all remaining - make a 3D view for each warning type", "No changes and no more pop-ups: every remaining warning type gets its WRN 3D view");
        td.VerificationText = "Apply my choice to all remaining warning types (e.g. tick + Fix = fix all remaining)";
        td.CommonButtons = TaskDialogCommonButtons.Cancel;
        TaskDialogResult r = td.Show();
        bool applyAll = td.WasVerificationChecked();
        if (r == TaskDialogResult.CommandLink1) { solveGroup(k); if (applyAll) all = true; }
        else if (r == TaskDialogResult.CommandLink2) { groupStatus[k] = "SKIPPED (review view)"; makeView(k, false); if (applyAll) viewsOnly = true; }
        else if (r == TaskDialogResult.CommandLink3) { groupStatus[k] = "SKIPPED"; if (applyAll) skipRest = true; }
        else if (r == TaskDialogResult.CommandLink4) { groupStatus[k] = "SKIPPED (review view)"; makeView(k, false); viewsOnly = true; }
        else { stopNote = "Stopped by user - warning types already fixed are kept (Ctrl+Z undoes the whole run)."; break; }
    }
}
else if (action == 4) {
    int nAct = plans.Count(actionable);
    bool go = true;
    if (!skipConfirm && nAct > 0) {
        var td = new TaskDialog("RWIS - solve ALL");
        td.MainInstruction = string.Format("Fix {0} warnings across {1} warning types now?", nAct, groupKeys.Count(k => groupItems[k].Any(i => actionable(plans[i]))));
        td.MainContent = "Every change is logged in the RWIS Warnings Report (Desktop\\RWIS Reports) and shown in green in a 'WRN .. DONE' 3D view per type. One Ctrl+Z undoes everything.";
        td.ExpandedContent = string.Join("\n", groupKeys.Select(k => string.Format("[{0}] {1} x{2}", infos[groupItems[k][0]][4], trunc(k, 70), groupItems[k].Count)).ToArray());
        td.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
        go = td.Show() == TaskDialogResult.Yes;
    }
    if (go) foreach (string k in groupKeys) solveGroup(k);
    else stopNote = "Cancelled - nothing changed.";
}
    if (rwTx != null) {
        TransactionStatus txs = rwTx.Commit();
        if (txs != TransactionStatus.Committed) stopNote += (stopNote.Length > 0 ? " " : "") + "Revit did not commit the changes of this run (status " + txs + ").";
    }
} catch {
    if (rwTx != null && rwTx.HasStarted()) { try { rwTx.RollBack(); } catch { } }
    throw;
}


// ================= SUMMARY FOR THE CHAT / DIALOG =================
var summary = new List<object>();
foreach (string k in groupKeys) {
    var items = groupItems[k]; string[] f = infos[items[0]];
    string[] t = TAX.ContainsKey(f[3]) ? TAX[f[3]] : new string[] { "", "", "Review manually", "", "" };
    int num2 = groupKeys.IndexOf(k) + 1; string st = groupStatus.ContainsKey(k) ? groupStatus[k] : "NOT RUN";
    string vn = viewNames.ContainsKey(k) ? viewNames[k] : "";
    int act = items.Count(i => actionable(plans[i]));
    summary.Add(string.Format("{0:00} [{1}] {2} {3} | x{4} | auto-fixable {5}/{4} | tool: {6} | RWIS: {7} | {8}{9}",
        num2, f[4], f[3], trunc(k, 70), items.Count, act, plans[items.Any(i => actionable(plans[i])) ? items.First(i => actionable(plans[i])) : items[0]].Split(';')[0], t[2], st, vn.Length > 0 ? " | view: " + vn : ""));
}

// ================= RWIS REPORT WORKBOOK: update + save =================
string reportPath = ""; double health = 0; int openNow = 0, solvedAicon = 0, solvedOut = 0;
if (!skipReport) {
  try {
    DateTime now = DateTime.Now;
    var IC = System.Globalization.CultureInfo.InvariantCulture;
    string ts = now.ToString("yyyy-MM-dd HH:mm");
    string runLabel = opts.ReportOnly ? "Report only - no views, no changes" : ACTION_NAMES[action];
    var sevT = WB["TBL_Severity"].Skip(1).Where(r => C(r, 0).Length > 0).ToDictionary(r => C(r, 0).ToUpperInvariant(), r => r);
    var taxT = WB["TBL_Taxonomy"].Skip(1).Where(r => C(r, 0).Length > 0).ToDictionary(r => C(r, 0), r => r);
    var ruleT = WB["TBL_Assignment_Rules"].Skip(1).Where(r => C(r, 1).Length > 0).ToList();
    var fgT = WB["TBL_Fix_Guide"].Skip(1).Where(r => C(r, 1).Length > 0).ToList();
    string[] IMPACT = { "Model Integrity", "Coordination", "Data Quality", "Documentation", "Performance", "Delivery" };
    var freq = new Dictionary<string, int>();
    for (int ri = 0; ri < recText.Count; ri++) { string fk = recCls[ri][0].Length > 0 ? recCls[ri][0] : "UNCLASSIFIED"; freq[fk] = freq.ContainsKey(fk) ? freq[fk] + 1 : 1; }

    string[] H = { "Rank", "Warning_ID", "Severity", "Criticality_Score", "Status", "Can_AICon_Fix", "Solved_By", "Solved_On", "What_AICon_Changed", "Deleted_Element_IDs", "Warning_Type", "Class_ID", "Taxonomy", "Warning_Text", "Element_ID_1", "Category_1", "Level_1", "Element_ID_2", "Category_2", "Level_2", "AICon_Proposed_Fix", "RWIS_Recommended_Fix", "RWIS_Alternative_Fix", "Meaning", "Likely_Causes", "Risk_If_Ignored", "Impact_Areas", "Assigned_Role", "Assigned_Person", "Coordinator_Notes", "Occurrences_Of_Type", "First_Seen", "Last_Seen", "Times_Seen", "Warning_Hash", "Model" };
    int NC = H.Length;
    int cRank = 0, cId = 1, cSev = 2, cCrit = 3, cStat = 4, cCan = 5, cBy = 6, cOn = 7, cChg = 8, cDel = 9, cType = 10, cCls = 11, cTax = 12, cText = 13, cE1 = 14, cC1 = 15, cL1 = 16, cE2 = 17, cC2 = 18, cL2 = 19,
        cPlan = 20, cFix = 21, cAlt = 22, cMean = 23, cCause = 24, cRisk = 25, cImp = 26, cRole = 27, cPers = 28, cNote = 29, cFreq = 30, cFirst = 31, cLast = 32, cTimes = 33, cHash = 34, cModel = 35;

    // ---- previous report rows (matched by column name, so manual columns survive)
    var rowsAll = new List<string[]>();
    if (WB.ContainsKey("All_Warnings") && WB["All_Warnings"].Count > 1) {
        string[] ph = WB["All_Warnings"][0];
        var map = new int[NC]; for (int q = 0; q < NC; q++) map[q] = Array.IndexOf(ph, H[q]);
        foreach (string[] pr in WB["All_Warnings"].Skip(1)) {
            var na = new string[NC]; for (int q = 0; q < NC; q++) na[q] = map[q] >= 0 ? C(pr, map[q]) : "";
            if (na[cHash].Length > 0) rowsAll.Add(na);
        }
    }
    var byHash = new Dictionary<string, string[]>();
    foreach (string[] pa in rowsAll) byHash[pa[cHash]] = pa;
    string[] CH = { "Date_Time", "Warning_ID", "Severity", "Warning_Type", "Element_IDs", "What_AICon_Changed", "Deleted_Element_IDs", "Run", "Model" };
    var chg = new List<string[]>();
    if (WB.ContainsKey("AICon_Change_Log") && WB["AICon_Change_Log"].Count > 1) {
        string[] ph2 = WB["AICon_Change_Log"][0];
        int[] map2 = CH.Select(h2 => Array.IndexOf(ph2, h2)).ToArray();
        foreach (string[] pr2 in WB["AICon_Change_Log"].Skip(1)) if (C(pr2, 0).Length > 0) chg.Add(map2.Select(m2 => m2 >= 0 ? C(pr2, m2) : "").ToArray());
    }
    var snap = WB.ContainsKey("Snapshot_Log") ? WB["Snapshot_Log"].Skip(1).Where(r => C(r, 0).Length > 0).ToList() : new List<string[]>();
    // older runs did not store deleted IDs - recover them from the action text
    Func<string, string> parseDeleted = t0 => {
        if (t0 == null || !t0.StartsWith("Deleted")) return "";
        int cut = t0.Length; int k1 = t0.IndexOf(" ("), k2 = t0.IndexOf(", keep");
        if (k1 > 0 && k1 < cut) cut = k1; if (k2 > 0 && k2 < cut) cut = k2;
        return string.Join(" ", System.Text.RegularExpressions.Regex.Matches(t0.Substring(0, cut), "\\d{5,}").Cast<System.Text.RegularExpressions.Match>().Select(mt => mt.Value).ToArray());
    };
    foreach (string[] pa2 in rowsAll)
        if (pa2[cDel].Length == 0 && pa2[cStat] == "SOLVED - AICon") { string dd = parseDeleted(pa2[cChg]); if (dd.Length > 0) { pa2[cDel] = dd; pa2[cStat] = "SOLVED - AICon (DELETED)"; } }
    foreach (string[] cl in chg) if (cl[6].Length == 0) cl[6] = parseDeleted(cl[5]);
    string idPrefix = "WRN-" + now.ToString("yyyyMMdd") + "-";
    int nextId = rowsAll.Where(r => r[cId].StartsWith(idPrefix)).Select(r => (int)toNum(r[cId].Substring(idPrefix.Length))).DefaultIfEmpty(0).Max() + 1;
    var recRes = new string[recText.Count];
    var recDelRes = new string[recText.Count];
    for (int i = 0; i < infos.Count; i++) if (results[i] != null) { recRes[int.Parse(infos[i][9])] = results[i]; recDelRes[int.Parse(infos[i][9])] = resDel[i] ?? ""; }

    int newN = 0, seenN = 0, aiconN = 0, goneN = 0;
    var current = new HashSet<string>();
    for (int ri = 0; ri < recText.Count; ri++) {
        string h = recHash[ri]; if (current.Contains(h)) continue; current.Add(h);
        string[] c = recCls[ri], el = recEl[ri]; string[] row;
        if (byHash.ContainsKey(h)) {
            row = byHash[h]; seenN++;
            if (row[cStat].StartsWith("SOLVED")) { row[cStat] = "RE-OPENED"; row[cBy] = ""; row[cOn] = ""; row[cChg] = (row[cChg].Length > 0 ? row[cChg] + " | " : "") + "warning came back on " + ts; }
            row[cTimes] = ((int)toNum(row[cTimes]) + 1).ToString();
        } else {
            row = new string[NC]; for (int q = 0; q < NC; q++) row[q] = "";
            row[cId] = idPrefix + (nextId++).ToString("0000"); row[cHash] = h; row[cFirst] = ts; row[cTimes] = "1"; row[cStat] = "OPEN";
            row[cText] = recText[ri]; row[cE1] = el[0]; row[cE2] = el[1]; row[cC1] = el[2]; row[cC2] = el[3]; row[cL1] = el[4]; row[cL2] = el[5]; row[cModel] = doc.Title;
            rowsAll.Add(row); byHash[h] = row; newN++;
        }
        row[cLast] = ts;
        string sv = c[3]; string[] sr = sevT.ContainsKey(sv) ? sevT[sv] : null;
        double score = sr != null ? toNum(C(sr, 1)) : sevRank(sv);
        string fk2 = c[0].Length > 0 ? c[0] : "UNCLASSIFIED"; int fq = freq[fk2];
        double fmod = fq <= 1 ? 1 : fq < 5 ? (sr != null ? toNum(C(sr, 2)) : 1) : fq <= 20 ? (sr != null ? toNum(C(sr, 3)) : 1.5) : (sr != null ? toNum(C(sr, 4)) : 2);
        string[] tx = taxT.ContainsKey(c[2]) ? taxT[c[2]] : null;
        string[] rule = ruleT.FirstOrDefault(r => C(r, 1) == c[2] && (C(r, 3) == "" || string.Equals(C(r, 3), sv, StringComparison.OrdinalIgnoreCase)));
        string role = rule != null ? (sv == "CRITICAL" && C(rule, 5).Length > 0 ? C(rule, 5) : C(rule, 4)) : (tx != null ? C(tx, 9) : "BIM Coordinator");
        var imp = new List<string>(); if (tx != null) for (int q = 0; q < 6; q++) if (C(tx, 3 + q).ToLowerInvariant() == "true") imp.Add(IMPACT[q]);
        string[] fgc = fgT.FirstOrDefault(r => c[0].Length > 0 && C(r, 1) == c[0]);
        string[] fx = fgc != null ? new string[] { C(fgc, 2), C(fgc, 3), C(fgc, 4), C(fgc, 5), C(fgc, 6) } : (TAX.ContainsKey(c[2]) ? TAX[c[2]] : new string[] { "", "", "Review manually", "", "" });
        row[cSev] = sv; row[cCrit] = Math.Round(score * fmod, 2).ToString(IC); row[cType] = c[1]; row[cCls] = c[0];
        row[cTax] = tx != null ? c[2] + " - " + C(tx, 1) : c[2];
        row[cFix] = fx[2]; row[cAlt] = fx[3]; row[cMean] = fx[0]; row[cCause] = fx[1];
        row[cRisk] = fx[4] + (c[4].Length > 0 && !c[4].StartsWith("Not in") ? " - " + c[4] : "");
        row[cImp] = string.Join(", ", imp.ToArray()); row[cRole] = role; row[cFreq] = fq.ToString();
        string pl = recPlan[ri];
        row[cPlan] = pl.StartsWith("MANUAL") ? "Manual fix: " + pl.Substring(pl.IndexOf(':') + 1).Trim() : (pl.StartsWith("OK:") ? pl.Substring(3).Trim() : pl);
        row[cCan] = actionable(pl) ? "Yes" : (pl.StartsWith("OK:") ? "Already OK" : "No - " + pl.Substring(pl.IndexOf(':') + 1).Trim());
        string res = recRes[ri];
        if (res != null && actionable(res)) {
            row[cDel] = recDelRes[ri] ?? "";
            row[cStat] = row[cDel].Length > 0 ? "SOLVED - AICon (DELETED)" : "SOLVED - AICon"; row[cBy] = "AICon"; row[cOn] = ts; row[cChg] = res; aiconN++;
            chg.Add(new string[] { ts, row[cId], sv, c[1], string.Join(" ", recIds[ri].Select(sid).ToArray()), res, row[cDel], runLabel, doc.Title });
        } else if (res != null && res.StartsWith("MANUAL: Skipped by you")) {
            row[cChg] = "Not changed - you chose to keep the element(s) on " + ts;
        }
    }
    foreach (string[] r0 in rowsAll)
        if ((r0[cModel] == doc.Title || r0[cModel] == "") && (r0[cStat] == "OPEN" || r0[cStat] == "RE-OPENED") && !current.Contains(r0[cHash])) {
            r0[cStat] = "SOLVED - outside AICon"; r0[cBy] = "Outside AICon (manual or other change)"; r0[cOn] = ts;
            if (r0[cChg].Length == 0) r0[cChg] = "Not changed by AICon - the warning is no longer in the model";
            goneN++;
        }
    Func<string, int> stOrder = s => s.StartsWith("SOLVED") ? 1 : 0;
    var sorted = rowsAll.Where(r => r[cModel] == doc.Title || r[cModel] == "")
        .OrderByDescending(r => toNum(r[cCrit])).ThenByDescending(r => sevRank(r[cSev])).ThenBy(r => stOrder(r[cStat])).ThenBy(r => r[cType]).ThenBy(r => r[cId]).ToList();
    for (int q = 0; q < sorted.Count; q++) sorted[q][cRank] = (q + 1).ToString();
    var openRows = sorted.Where(r => !r[cStat].StartsWith("SOLVED")).ToList();
    openNow = openRows.Count; solvedAicon = sorted.Count(r => r[cStat].StartsWith("SOLVED - AICon")); solvedOut = sorted.Count(r => r[cStat] == "SOLVED - outside AICon");
    int reopened = sorted.Count(r => r[cStat] == "RE-OPENED");
    int oC = openRows.Count(r => r[cSev] == "CRITICAL"), oH = openRows.Count(r => r[cSev] == "HIGH"), oM = openRows.Count(r => r[cSev] == "MEDIUM"), oL = openRows.Count(r => r[cSev] == "LOW");
    health = Math.Max(0, Math.Round(100 - (3.3 * oC + 0.4 * oH + 0.1 * oM + 0.05 * oL), 1));
    snap.Add(new string[] { ts, runLabel, doc.Title, allWarnings.Count.ToString(), newN.ToString(), seenN.ToString(), aiconN.ToString(), goneN.ToString(), openNow.ToString(), oC.ToString(), oH.ToString(), oM.ToString(), oL.ToString(), health.ToString(IC) });

    // ================= XLSX WRITER (pure code in Rwis/Pure/XlsxWriter.cs) =================
    Func<int, string> colL = XlsxWriter.ColL;
    Func<string, string> xe = XlsxWriter.Xe;
    Func<object, string, int, string> cellX = XlsxWriter.CellX;
    Func<double[], string> colsOf = XlsxWriter.ColsOf;
    Func<int, int, string> freezeView = XlsxWriter.FreezeView;
    Func<List<object[]>, Func<int, int, int>, string, string, Func<int, string>, string, string> sheetXml = XlsxWriter.SheetXml;
    Func<int, string> noAttr = r => "";
    Func<string, int> sevStyle = s => s == "CRITICAL" ? 5 : s == "HIGH" ? 6 : s == "MEDIUM" ? 7 : s == "LOW" ? 8 : 9;
    Func<string, int> statStyle = s => s == "SOLVED - AICon (DELETED)" ? 24 : s == "SOLVED - AICon" ? 13 : s.StartsWith("SOLVED") ? 20 : s == "RE-OPENED" ? 15 : 14;
    // simple table (header + body, auto widths, frozen header, filter)
    Func<string[], List<string[]>, bool, string> simpleTable = (hdr, body, numeric) => {
        var rows = new List<object[]> { hdr.Cast<object>().ToArray() };
        foreach (string[] b in body) rows.Add(Enumerable.Range(0, hdr.Length).Select(q => { string v = C(b, q); double dv; return (object)(numeric && v.Length > 0 && v.Length < 12 && !v.StartsWith("0") && double.TryParse(v, System.Globalization.NumberStyles.Float, IC, out dv) ? (object)dv : v); }).ToArray());
        var ws = new double[hdr.Length];
        for (int q = 0; q < hdr.Length; q++) ws[q] = Math.Min(60, Math.Max(10, Math.Max(hdr[q].Length + 2, body.Count == 0 ? 0 : body.Max(b => C(b, q).Length) * 0.9 + 2)));
        return sheetXml(rows, (r, ci) => r == 0 ? 1 : (C(body[r - 1], ci).Length < 12 ? 11 : 9), colsOf(ws), freezeView(0, 1) , r => r == 0 ? " ht=\"30\" customHeight=\"1\"" : "",
            "<autoFilter ref=\"A1:" + colL(hdr.Length - 1) + Math.Max(1, rows.Count) + "\" />");
    };

    // change log with red rows for deletions
    Func<string[], List<string[]>, string> chgSheet = (hdr, body) => {
        var rows = new List<object[]> { hdr.Cast<object>().ToArray() };
        foreach (string[] b in body) rows.Add(Enumerable.Range(0, hdr.Length).Select(q => (object)C(b, q)).ToArray());
        return sheetXml(rows, (r, ci) => r == 0 ? 1 : (C(body[r - 1], 6).Length > 0 ? (ci == 5 || ci == 6 ? 21 : 22) : (ci == 5 ? 9 : 11)), colsOf(new double[] { 17, 19, 11, 28, 22, 70, 22, 30, 34 }), freezeView(0, 1), r => r == 0 ? " ht=\"30\" customHeight=\"1\"" : "",
            "<autoFilter ref=\"A1:" + colL(hdr.Length - 1) + Math.Max(1, rows.Count) + "\" />");
    };
    // ---- All_Warnings sheet
    var aRows = new List<object[]> { H.Cast<object>().ToArray() };
    int[] numCols = { cRank, cCrit, cE1, cE2, cFreq, cTimes };
    foreach (string[] srow in sorted) aRows.Add(Enumerable.Range(0, NC).Select(q => numCols.Contains(q) && srow[q].Length > 0 ? (object)toNum(srow[q]) : (object)srow[q]).ToArray());
    int[] centerCols = { cRank, cId, cCrit, cCls, cE1, cE2, cFreq, cFirst, cLast, cTimes, cOn };
    Func<int, int, int> aStyle = (r, ci) => {
        if (r == 0) return 1;
        string[] row = sorted[r - 1]; bool solved = row[cStat].StartsWith("SOLVED"); bool deleted = row[cDel].Length > 0;
        if (ci == cSev) return sevStyle(row[cSev]);
        if (ci == cStat) return statStyle(row[cStat]);
        if (deleted) return (ci == cChg || ci == cDel) ? 21 : (centerCols.Contains(ci) ? 23 : 22);
        if (ci == cCan && row[cCan].StartsWith("No") && !solved) return 19;
        if (centerCols.Contains(ci)) return solved ? 12 : 11;
        return solved ? 10 : 9;
    };
    double[] aW = { 6, 18, 11, 10, 24, 30, 18, 16, 55, 22, 30, 9, 28, 60, 12, 20, 22, 12, 20, 22, 55, 32, 28, 32, 28, 34, 30, 18, 18, 30, 11, 16, 16, 8, 19, 34 };

    // ---- Summary sheet
    var sRows = new List<object[]>(); var sSt = new List<int[]>(); var merges = new List<string>(); var tall = new Dictionary<int, string>();
    Action<object[], int[]> add = (vals, sts) => { sRows.Add(vals); sSt.Add(sts); };
    Func<int, int, int[]> fill = (st, n) => Enumerable.Repeat(st, n).ToArray();
    add(new object[] { "RWIS  -  Revit Warnings Report" }, fill(2, 10)); merges.Add("A1:J1"); tall[0] = "34";
    add(new object[] { "Model: " + doc.Title + "      |      Updated: " + ts + "      |      Last run: " + runLabel }, fill(18, 10)); merges.Add("A2:J2");
    add(new object[0], new int[0]);
    add(new object[] { "OVERVIEW" }, fill(3, 10)); merges.Add("A4:J4"); tall[3] = "22";
    int delCount = sorted.Sum(r => r[cDel].Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length);
    add(new object[] { "Warnings tracked (all runs)", "Open now", "Solved by AICon", "Solved outside AICon", "Re-opened", "Elements deleted by AICon", "Health score (100 = clean)" }, fill(17, 7)); tall[4] = "32";
    add(new object[] { (double)sorted.Count, (double)openNow, (double)solvedAicon, (double)solvedOut, (double)reopened, (double)delCount, health }, new int[] { 16, 16, 16, 16, 16, delCount > 0 ? 24 : 16, 16 }); tall[5] = "34";
    add(new object[0], new int[0]);
    add(new object[] { "BY SEVERITY" }, fill(3, 10)); merges.Add("A8:J8"); tall[7] = "22";
    add(new object[] { "Severity", "Total tracked", "Open", "Solved by AICon", "Solved outside AICon", "% of open", "Action required (RWIS)" }, fill(1, 7)); merges.Add("G9:J9"); tall[8] = "30";
    foreach (string sv in new string[] { "CRITICAL", "HIGH", "MEDIUM", "LOW" }) {
        var g = sorted.Where(r => r[cSev] == sv).ToList(); int op = g.Count(r => !r[cStat].StartsWith("SOLVED"));
        string act2 = sevT.ContainsKey(sv) ? C(sevT[sv], 8) : "";
        add(new object[] { sv, (double)g.Count, (double)op, (double)g.Count(r => r[cStat].StartsWith("SOLVED - AICon")), (double)g.Count(r => r[cStat] == "SOLVED - outside AICon"), openNow == 0 ? "0%" : (100.0 * op / openNow).ToString("0.0", IC) + "%", act2 },
            new int[] { sevStyle(sv), 11, 11, 11, 11, 11, 9, 9, 9, 9 });
        merges.Add("G" + sRows.Count + ":J" + sRows.Count);
    }
    add(new object[0], new int[0]);
    add(new object[] { "WARNING TYPES  (sorted by criticality)" }, fill(3, 10)); merges.Add("A" + sRows.Count + ":J" + sRows.Count); tall[sRows.Count - 1] = "22";
    add(new object[] { "Warning type", "Severity", "Max criticality", "Total", "Open", "Solved by AICon", "Solved outside AICon", "Can AICon fix the open ones?", "RWIS recommended fix", "Risk if ignored" }, fill(1, 10)); tall[sRows.Count - 1] = "30";
    foreach (var grp in sorted.GroupBy(r => r[cType] + "|" + r[cSev]).OrderByDescending(gg => gg.Max(r => toNum(r[cCrit]))).ThenByDescending(gg => sevRank(gg.First()[cSev]))) {
        var lst = grp.ToList(); var op = lst.Where(r => !r[cStat].StartsWith("SOLVED")).ToList();
        int yes = op.Count(r => r[cCan] == "Yes");
        string can = op.Count == 0 ? "-" : yes == op.Count ? "Yes (all " + yes + ")" : yes > 0 ? "Partly (" + yes + " of " + op.Count + ")" : "No - manual";
        add(new object[] { lst[0][cType], lst[0][cSev], lst.Max(r => toNum(r[cCrit])), (double)lst.Count, (double)op.Count, (double)lst.Count(r => r[cStat].StartsWith("SOLVED - AICon")), (double)lst.Count(r => r[cStat] == "SOLVED - outside AICon"), can, lst[0][cFix], lst[0][cRisk] },
            new int[] { 9, sevStyle(lst[0][cSev]), 11, 11, 11, 11, 11, can.StartsWith("No") ? 19 : 11, 9, 9 });
    }
    add(new object[0], new int[0]);
    add(new object[] { "All_Warnings = every warning ever found in this model, open or solved, sorted by criticality (green rows = solved, RED rows = AICon deleted an element).  AICon_Change_Log = every model change AICon made.  Column_Guide = what each column means." }, fill(18, 10)); merges.Add("A" + sRows.Count + ":J" + sRows.Count);
    Func<int, int, int> sStyle = (r, ci) => r < sSt.Count && ci < sSt[r].Length ? sSt[r][ci] : 0;
    Func<int, string> sAttr = r => tall.ContainsKey(r) ? " ht=\"" + tall[r] + "\" customHeight=\"1\"" : "";
    string sMerge = "<mergeCells count=\"" + merges.Count + "\">" + string.Join("", merges.Select(m => "<mergeCell ref=\"" + m + "\" />").ToArray()) + "</mergeCells>";

    // ---- other sheets
    string[] SH = { "Run_Date_Time", "Action", "Model", "Warnings_In_Model", "New_This_Run", "Seen_Before", "Solved_By_AICon_This_Run", "Solved_Outside_AICon_Since_Last_Run", "Open_After_Run", "Open_CRITICAL", "Open_HIGH", "Open_MEDIUM", "Open_LOW", "Health_Score" };
    var guide = new List<string[]> {
        new string[] { "Rank", "Position when all warnings are sorted by Criticality_Score (highest first), then severity, then open before solved." },
        new string[] { "Warning_ID", "Unique ID given when the warning was first found (WRN-date-number)." },
        new string[] { "Severity", "CRITICAL / HIGH / MEDIUM / LOW from TBL_Warning_Classes." },
        new string[] { "Criticality_Score", "Severity_Score (TBL_Severity) x frequency modifier (how often this warning type occurs in the model)." },
        new string[] { "Status", "OPEN = still in the model.  SOLVED - AICon = fixed by this tool.  SOLVED - outside AICon = disappeared without AICon (manual fix or other change).  RE-OPENED = was solved, came back." },
        new string[] { "Can_AICon_Fix", "Yes = the tool can fix it automatically. No = manual fix needed (reason shown, e.g. pinned element)." },
        new string[] { "Solved_By / Solved_On", "Who solved it and when." },
        new string[] { "What_AICon_Changed", "The exact model change AICon made (element IDs, angle, deleted/kept element, renumbered value...)." },
        new string[] { "AICon_Proposed_Fix", "What AICon will do if you run a Solve action (or the manual fix if it cannot)." },
        new string[] { "RWIS_Recommended_Fix / Alternative_Fix", "From TBL_Fix_Guide." },
        new string[] { "Meaning / Likely_Causes / Risk_If_Ignored / Impact_Areas", "From TBL_Fix_Guide, TBL_Warning_Classes and TBL_Taxonomy." },
        new string[] { "Assigned_Role", "From TBL_Assignment_Rules (CRITICAL goes to the escalation role)." },
        new string[] { "Assigned_Person / Coordinator_Notes", "Yours to fill in - kept on every run." },
        new string[] { "Occurrences_Of_Type", "How many warnings of the same type are in the model now." },
        new string[] { "First_Seen / Last_Seen / Times_Seen", "History across runs." },
        new string[] { "Warning_Hash", "Fingerprint (warning text + element IDs) used to recognise the same warning in later runs." },
        new string[] { "Deleted_Element_IDs", "Elements AICon deleted to solve the warning. These rows are RED in All_Warnings and AICon_Change_Log. Before deleting any 3D element AICon highlights it (magenta) and asks you." },
        new string[] { "Health score", "100 - (3.3 x open CRITICAL + 0.4 x open HIGH + 0.1 x open MEDIUM + 0.05 x open LOW)." }
    };
    string[] SHEETS = { "RWIS_Summary", "All_Warnings", "AICon_Change_Log", "Snapshot_Log", "Column_Guide", "TBL_Warning_Classes", "TBL_Taxonomy", "TBL_Severity", "TBL_Assignment_Rules", "TBL_Fix_Guide", "TBL_Roles_List" };
    var bodies = new List<string> {
        sheetXml(sRows, sStyle, colsOf(new double[] { 44, 14, 14, 14, 16, 16, 18, 26, 42, 42 }), freezeView(0, 0), sAttr, sMerge),
        sheetXml(aRows, aStyle, colsOf(aW), freezeView(5, 1), r => r == 0 ? " ht=\"32\" customHeight=\"1\"" : "", "<autoFilter ref=\"A1:" + colL(NC - 1) + aRows.Count + "\" />"),
        chgSheet(CH, chg.OrderByDescending(r => C(r, 0)).ToList()),
        simpleTable(SH, snap, true),
        simpleTable(new string[] { "Column", "Meaning" }, guide, false)
    };
    foreach (string tn in new string[] { "TBL_Warning_Classes", "TBL_Taxonomy", "TBL_Severity", "TBL_Assignment_Rules", "TBL_Fix_Guide", "TBL_Roles_List" }) {
        var t = WB[tn].Where(r => r.Any(x => !string.IsNullOrEmpty(x))).ToList();
        bodies.Add(simpleTable(t[0].Select(x => x ?? "").ToArray(), t.Skip(1).ToList(), true));
    }


    var parts = XlsxWriter.BuildPackage(SHEETS, bodies, XlsxWriter.TabColors, "All_Warnings!$A$1:$" + colL(NC - 1) + "$" + aRows.Count);


    System.IO.Directory.CreateDirectory(reportDir);
    string tmp = wbPath + ".tmp";
    zipWrite(tmp, parts);
    try { System.IO.File.Copy(tmp, wbPath, true); reportPath = wbPath; }
    catch (System.IO.IOException) {
        reportPath = System.IO.Path.Combine(reportDir, safeModel + "_RWIS_Warnings_Report_" + now.ToString("yyyyMMdd_HHmmss") + ".xlsx");
        System.IO.File.Copy(tmp, reportPath, true);
        reportNote += "The report is open in Excel, so this run was saved as a separate copy - close Excel before the next run so the history stays in one file. ";
    }
    try { System.IO.File.Delete(tmp); } catch { }
  } catch (Exception ex) { reportNote += "Report failed: " + ex.Message; }
}
if (firstView != null && firstView.Id != doc.ActiveView.Id) { try { uidoc.RequestViewChange(firstView); } catch { } }

var rwSummary = new System.Text.StringBuilder();
rwSummary.AppendLine("RWIS Warning Manager v7 - " + (opts.ReportOnly ? "Report only (no views, no changes)" : ACTION_NAMES[action]));
if (stopNote.Length > 0) rwSummary.AppendLine("Note: " + stopNote);
rwSummary.AppendLine("Warnings in model: " + allWarnings.Count);
rwSummary.AppendLine("Warnings in scope: " + infos.Count);
rwSummary.AppendLine("Number of warning types: " + groupKeys.Count);
rwSummary.AppendLine("Auto-fixable (planned): " + plans.Count(actionable));
rwSummary.AppendLine("Fixed this run: " + results.Count(r => r != null && actionable(r)));
rwSummary.AppendLine("Colours: Red CRITICAL / Orange HIGH / Yellow MEDIUM / Blue LOW / Magenta = will be deleted / Green = changed by the tool");
rwSummary.AppendLine("Report: " + (reportPath.Length > 0 ? reportPath : "not saved"));
rwSummary.AppendLine("Open now: " + openNow);
rwSummary.AppendLine("Solved by AICon: " + solvedAicon);
rwSummary.AppendLine("Solved outside AICon: " + solvedOut);
rwSummary.AppendLine("Health score: " + health.ToString(System.Globalization.CultureInfo.InvariantCulture));
if (reportNote.Length > 0) rwSummary.AppendLine("Report note: " + reportNote.Trim());
rwSummary.AppendLine();
rwSummary.AppendLine(TypesMarker);
foreach (object rwLine in summary) rwSummary.AppendLine(rwLine.ToString());
return rwSummary.ToString().TrimEnd();
        }
    }
}

