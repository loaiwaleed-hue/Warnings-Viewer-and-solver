# BIMRadar Warnings Manager – project context

Standing rules for every ticket. Read before implementing anything.

## Product
- Port of the owner's AICon routine **isolate-warnings-rwis-v7** ("RWIS Warning Manager v7", `reference-source/Routine.cs` + `routine.json`) to a Revit add-in.
- Ribbon: tab **BIMRadar** › panel **Health** (red). The tab is shared with the other BIMRadar tools (Clash Viewer: Review panel; QA/QC: Check / Fix panels). Never set a tab title other than `BIMRadar`.
- Health panel buttons (owner decision 2026-11-26):
  - **Warnings**: opens the Warnings window (below).
  - **Review Warnings**: v7 action 1, one WRN 3D view per warning type, coloured by severity, elements that would be deleted in magenta. Read-only on elements.
  - **Solve This View**: v7 action 2, solve the warning type of the active WRN 3D view.
  - **Warnings Report**: update the Excel report only.
- Warnings window (modeless, ExternalEvent like Clash Viewer):
  - warning types grouped by severity with count, RWIS class and planned fix;
  - click a type → its WRN view, plus meaning / causes / fix from TBL_Fix_Guide;
  - Solve this type, Next type (v7 action 3), Solve all… (v7 action 4, with a warning);
  - filters: severity and text; options Unpin to fix and Don't ask before fixing.
- **First version = exact v7 behaviour** (owner: improvements after his test):
  - same fixes and the same confirmations: magenta = delete, green = keep, Delete / Keep / Delete all;
  - room-boundary-only fixes are applied without asking;
  - Excel report `Desktop\RWIS Reports\<model>_RWIS_Warnings_Report.xlsx` with the same sheets, styles and red deleted rows;
  - one named transaction per run (one Ctrl+Z).

## Data
- `data/RWIS_Template.xlsx` is the RWIS master tables (classes, taxonomy, severity, assignment rules, fix guide, stages, status, roles, column dictionary). The owner approved them as public.
- Embed the template in the DLL as a resource. Nothing is read from the AICon folder.
- **No project data in this public repo**: no model names, element IDs or warning rows from real models. TBL_Warnings_Master, TBL_Snapshot_Log and the summary values ship empty. Tests use synthetic data only.

## Runtime
- Revit 2022–2027: net48 (2022–2024), net8.0-windows (2025–2026), net10.0-windows (2027). CI builds all three (`RevitVersion=2022/2025/2027`, Nice3point.Revit.Api packages).
- Revit API: check every member in every year (https://rvtdocs.com/, https://www.revitapidocs.com/). Version differences go in `Compat.cs` only. **Never use `ElementId.IntegerValue`** (removed in 2026): use `Compat.IdValue` / `Compat.Eid`. v7 uses IntegerValue, so replace it while porting.
- pyRevit loader (IronPython 2.7, no f-strings): picks bin/net48|net8|net10 and loads the DLL from bytes. It reuses an assembly that is already loaded.
- Standalone add-in: `IExternalApplication` creates tab BIMRadar › Health. It stays hidden if pyRevit really loads the extension (see Clash Viewer #98).
- Settings folder: `%AppData%\BIMRadar\Warnings`.

## Process
- License MIT, public repo `loaiwaleed-hue/Warnings-Viewer-and-solver`. User-facing name: **BIMRadar Warnings Manager**. Internal names: assembly / namespace `BIMRadar.Warnings`, extension folder `BIMRadar-Warnings.extension`.
- Tracker: GitHub Issues with labels needs-triage, needs-info, ready-for-agent, ready-for-human, wontfix.
- Work happens on branch `dev`; CI publishes unzipped test builds to `test-build-dev`. Claude installs each test build into the owner's pyRevit Extensions folder (the owner wants the pyRevit extension, not zips).
- Nothing is merged to main, tagged or released without the owner's test and OK. The owner publishes releases himself.
- The sandbox cannot compile: CI (windows-latest) is the build check.
