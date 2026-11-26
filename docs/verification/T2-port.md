# T2 - port of AICon routine isolate-warnings-rwis-v7

Source: `reference-source/Routine.cs` (1078 lines) and `reference-source/routine.json`.
Nothing here was compiled or run (the sandbox has no .NET and no Revit). CI (windows-latest) is the first build check. The port was checked by a line diff of `Routine.cs` against `RwisEngine.cs` (`diff -w`): everything not listed under "Deliberate changes" below is byte-for-byte the v7 code (only the indentation differs).

## Files

| File | Content |
|---|---|
| `src/BIMRadar.Warnings/Rwis/RwisEngine.cs` | `RwisEngine.Run(UIApplication, RwisOptions)` = the v7 script body, local lambdas kept. Also `IsWrnView(View)` and the embedded-template loader. |
| `src/BIMRadar.Warnings/Rwis/RwisOptions.cs` | Inputs of routine.json (+ `ReportOnly`, see changes). |
| `src/BIMRadar.Warnings/Rwis/Pure/XlsxIO.cs` | unzip, zipWrite, readXlsx, `C`, `toNum` (no Revit types). |
| `src/BIMRadar.Warnings/Rwis/Pure/XlsxWriter.cs` | colL, xe, cellX, colsOf, freezeView, sheetXml and the package assembly (content types, rels, workbook, styles). |
| `src/BIMRadar.Warnings/Rwis/Pure/XlsxStyles.cs` | The `STYLES` string of v7, generated from line 1 of Routine.cs by script (not retyped). |
| `src/BIMRadar.Warnings/Rwis/Pure/RwisClassifier.cs` | ALIASES + `classify` (TBL_Warning_Classes keyword match, longest keyword wins). |
| `src/BIMRadar.Warnings/Rwis/Pure/RwisSeverity.cs` | `sevRank`, severity filter parsing, action parsing, severity order. |
| `src/BIMRadar.Warnings/Rwis/Pure/RwisText.cs` | Text normalisation, `hashOf`, `clean`, `trunc`, and `RwisGrouping.OrderKeys` (the groupKeys ordering). |
| `src/BIMRadar.Warnings/BIMRadar.Warnings.csproj` | + embedded resource `BIMRadar.Warnings.RWIS_Template.xlsx` (from `data/`). |
| `src/BIMRadar.Warnings/Compat.cs` | `Eid` / `IdValue` now cache their reflection lookups (same results). |
| `tests/BIMRadar.Warnings.Tests/*` | csproj links `Rwis\Pure\*.cs` and copies the template; `RwisPureTests.cs` (synthetic data only). |

## Section map (Routine.cs "// ===== X =====" headers)

| Routine.cs section (lines) | New location |
|---|---|
| line 1 `STYLES` | `Pure/XlsxStyles.cs` `XlsxStyles.Styles` |
| INPUTS (3-83): inputs, `sevRank`, severity set, TOL, `touched`, `recDel`, colours, `get/sid/nm/deg/inGroup/pinBlock/pinNote/withUnpin/isCat/isSketchLine/isSepLine/is3D`, `confirmDelete` (magenta/green + Delete / Keep / Delete all) | `RwisEngine.Run`, section INPUTS. Inputs now read from `RwisOptions`; `sevRank` = `RwisSeverity.Rank`; severity set from `opts.Severities` (parsing of the routine.json text: `RwisSeverity.Parse`). `confirmDelete` unchanged. |
| GEOMETRY HELPERS (85-117) | `RwisEngine.Run`, section GEOMETRY HELPERS, unchanged |
| FIX HANDLERS (119-372): `joinPairs`, `unjoinPairs`, `removeContained`, `fixDuplicateValue`, `fixIdentical`, `fixOverlap` (incl. room-boundary auto-fix), `snapSketchLine`, `fixOffAxis`, `fixTag`, `fixUnplaced`, `fixEmptyGroup`, `dispatch`, `actionable` | `RwisEngine.Run`, section FIX HANDLERS; only the 4 `IntegerValue` uses changed |
| RWIS MASTER WORKBOOK: zip / xlsx helpers (374-465): `unzip`, `CRCT`, `zipWrite`, `NS`, `readXlsx`, `C`, `toNum` | `Pure/XlsxIO.cs` (`Unzip`, `ZipWrite`, `ReadXlsx`, `C`, `ToNum`); the engine keeps local `Func` aliases of the same names |
| LOAD (or seed) THE MASTER WORKBOOK (467-533): report path, existing workbook, template seed, `CLS`, `TAX`, ALIASES, `classify`, `hashOf`, `elInfo` | `RwisEngine.Run`, section LOAD; template from the embedded resource (`LoadTemplateBytes`); `classify` = `RwisClassifier.Classify`; `hashOf` = `RwisText.HashOf`; `CLS`, `TAX`, `elInfo` unchanged |
| COLLECT ALL WARNINGS (534-557): snapshot, planned fix per warning (preview) | `RwisEngine.Run`, section COLLECT, unchanged apart from `RwisText.Normalize` |
| WARNINGS IN SCOPE (558-583): filter, `infos`, groups by text ordered by severity | `RwisEngine.Run`, section WARNINGS IN SCOPE; ordering = `RwisGrouping.OrderKeys` |
| 3D VIEWS (584-650): WRN views, section box, isolate, colours, `makeView`, `roomOnly`, `groupAuto` | `RwisEngine.Run`, section 3D VIEWS, unchanged (`clean`, `trunc` = `RwisText.Clean/Trunc`) |
| DIALOG TEXT (651-688): `groupText`, `groupDetails`, `solveGroup` | `RwisEngine.Run`, section DIALOG TEXT, unchanged |
| RUN THE ACTION (689-758): actions 1-4 | `RwisEngine.Run`, section RUN THE ACTION; wrapped in one named Transaction (see changes) |
| SUMMARY (760-770): per-type summary lines | `RwisEngine.Run`, section SUMMARY, unchanged |
| RWIS REPORT WORKBOOK: update + save (772-888, 913-1021, 1047-1058) | `RwisEngine.Run`, section RWIS REPORT WORKBOOK, unchanged (sheets, styles, red deleted rows, history merge by hash, health score, Excel-open fallback copy) |
| XLSX WRITER (889-912, 1022-1046) | `Pure/XlsxWriter.cs` (`ColL`, `Xe`, `CellX`, `ColsOf`, `FreezeView`, `SheetXml`, `BuildPackage`); `sevStyle`, `statStyle`, `simpleTable`, `chgSheet` and the sheet content stay in the engine |
| final `return new Dictionary` (1062-1078) | `RwisEngine.Run` returns the same values as text (`"key: value"` lines, then `Warning types:` and the per-type lines) |

## Deliberate changes

| # | Change | Reason |
|---|---|---|
| 1 | Globals `doc`, `uidoc` come from `uiapp.ActiveUIDocument`; `input.*` from `RwisOptions` (parameter is named `opts`, not `o`, because the script already uses a local `o`). | Needed to run outside AICon. Defaults equal routine.json (action 1, all severities, nothing ticked). |
| 2 | `ElementId.IntegerValue` -> `Compat.IdValue(...)` (sid, isCat, removeContained, fixDuplicateValue, fixIdentical, hashOf). `isCat` compares as `long`. No `new ElementId(int)` exists in v7. | `IntegerValue` is removed in Revit 2026; project rule. Values are identical, so warning hashes (stored in existing reports) are unchanged. |
| 3 | Template: `%AppData%\AICon\routines\...\RWIS_Template.xlsx` -> embedded resource `BIMRadar.Warnings.RWIS_Template.xlsx`, read from memory (`XlsxIO.Unzip(byte[])`), no temp file. | CONTEXT.md: nothing is read from the AICon folder. |
| 4 | Pure code moved out of the script into `Rwis/Pure`: classify, severity rank / parsing, hash, clean / trunc, group ordering, xlsx read / write helpers, package assembly. The engine keeps local `Func` aliases so the rest of the code is untouched. `HashOf` creates an MD5 per call instead of sharing one instance. | Unit tests without Revit. Same results. |
| 5 | `RwisText.Normalize` replaces three copies of `ToLowerInvariant().Replace("\"","").Replace("'","")`. | Single definition; tested. |
| 6 | One named Transaction `"RWIS Warnings - <action>"` is started before RUN THE ACTION and committed right after it (rolled back and re-thrown on an exception). If Commit does not return `Committed`, a note is added to the summary. | v7 had no Transaction in the script: AICon wrapped the whole run in one (`readOnly:false`; "One Ctrl+Z undoes a run"). The report is written after the commit, so a slow Excel write no longer keeps the transaction open. Every model change (views, overrides, fixes, renames) is still inside the one transaction. |
| 7 | `RwisOptions.ReportOnly` (new, not in routine.json): no transaction, forces `skipViews`, and skips the "mark views of vanished warnings DONE" loop of action 1 (it renames views). The report run label is "Report only - no views, no changes". | Needed for the Warnings Report button (T3). With `ReportOnly = false` nothing differs from v7. |
| 8 | The action-2 message when the active view is not a WRN view names the new buttons ("Review Warnings" / "Solve This View") instead of "1. Review". | The ribbon has no action picker. Same condition. |
| 9 | Return value: the Dictionary becomes text (`Run` returns `string`). Same fields in the same order; the count line is labelled "Number of warning types". The per-type lines follow a line `Warning types:` (`RwisEngine.TypesMarker`) so Launcher can show them under "Show details". | The Revit add-in shows a TaskDialog, not an AICon result. |
| 10 | `RwisEngine.IsWrnView(View)` added (same test as v7 `keyOf`: Description starts with `RWIS|`). | Launcher.SolveView checks before doing the expensive collection. |
| 11 | `Compat.IdValue` / `Eid`: reflection lookups cached in static fields. | v7 calls IdValue thousands of times per run. Same results. |
| 12 | `XlsxIO.WriteZip`: `BinaryWriter(..., leaveOpen: true)` and `Flush()` before reading `fs.Position`; `UnzipFile` / `ZipBytes` added. | So the same code serves FileStream and MemoryStream (tests). The bytes written are identical. |

Nothing else was changed: the fixes, the planned-fix texts, the confirmations (magenta delete / green keep, Delete / Keep / Delete all), room-boundary auto-fixes, pin handling (`Unpin to fix`), WRN view naming, the Excel sheets and styles are v7.

## Revit API check (2022-2027)

Members used by the engine, all present in every year: `Document.GetWarnings`, `FailureMessage.GetDescriptionText / GetFailingElements / GetAdditionalElements`, `OverrideGraphicSettings` (SetProjection/CutLineColor, SetProjection/CutLineWeight, SetSurface/CutForegroundPatternId/Color), `View.SetElementOverrides / GetElementOverrides`, `UIDocument.ShowElements / RefreshActiveView / RequestViewChange`, `Selection.SetElementIds`, `View3D.CreateIsometric`, `View3D.IsSectionBoxActive / SetSectionBox`, `View.IsolateElementsTemporary / IsInTemporaryViewMode / DisableTemporaryViewMode`, `View.ViewTemplateId / DisplayStyle / DetailLevel`, `JoinGeometryUtils`, `ElementTransformUtils.RotateElement / MoveElement`, `CurveElement.SetGeometryCurve(curve, bool)`, `Element.GetDependentElements`, `SpatialElementTag.HasLeader`, `RoomTag.Room`, `SpaceTag.Space`, `Group.GetMemberIds`, `Element.Pinned`, `Transaction.Start / Commit / RollBack / HasStarted`, `TaskDialog` (CommandLinks, VerificationText, WasVerificationChecked). The only version-dependent members (`ElementId.IntegerValue` / `Value`, `ElementId(int|long)`) go through Compat. BCL: no `string.Contains(char)`, `Path.GetRelativePath`, ranges or index-from-end are used; the zip code uses `System.IO.Compression.DeflateStream` (referenced in the net48 csproj).

## Tests (tests/BIMRadar.Warnings.Tests/RwisPureTests.cs)

Synthetic data only. Classification of invented Revit-style texts against the real template (copied to the test output): longest keyword, quotes / case, aliases, unclassified, invalid severity fallback, every template keyword classifies itself. Severity rank / order / option parsing / action parsing. Group ordering. Hash stability (expected value computed independently with md5). `Clean` / `Trunc`. Xlsx: write -> read round trip (special characters, unicode, numbers, empty cells), valid zip (opened by `ZipArchive`), 18 package parts, tab colours, filter name, template unzip, non-zip rejected. The template ships without warning rows (asserted).

## Not verified here

- Nothing compiled. The engine is one very long method of local lambdas, as the script was; C# local-name rules were checked by hand (new top-level names are prefixed `rw` or taken from v7). C# 8+ shadowing is allowed for lambdas, and `LangVersion latest` is set.
- `ElementId` `==` / `!=` (used by v7 as `e.GroupId != ElementId.InvalidElementId`) is kept as in v7.
- Behaviour in Revit (views, overrides, deletes, dialogs) cannot be run here; it is the v7 code.
