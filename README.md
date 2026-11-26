# BIMRadar Warnings Manager

Finds, explains and fixes Revit warnings with the **RWIS** (Revit Warning Intelligence System) tables. Part of the **BIMRadar** ribbon tab, on the **Health** panel.

- Groups the model's warnings by type and classifies them by severity (CRITICAL / HIGH / MEDIUM / LOW) using 100 warning classes.
- Review: one 3D view per warning type, coloured by severity, with elements that a fix would delete in magenta. Changes nothing.
- Solve one type, step through the types, or solve all. Before any element is deleted it is shown in magenta, with the kept one in green, and you choose Delete / Keep / Delete all.
- Excel report per model in `Desktop\RWIS Reports`.

Revit 2022–2027. Install as a pyRevit extension or as a standalone add-in (see `docs/INSTALL.md` once released).

License: MIT.
