# Changelog

## Unreleased
- Repo set up from the AICon routine isolate-warnings-rwis-v7.
- T2: RWIS engine (`Rwis/RwisEngine.cs`) ported from v7 with the same behaviour; pure xlsx / classification / severity code in `Rwis/Pure` with unit tests; RWIS template embedded in the DLL.
- T3: Review Warnings, Solve This View and Warnings Report buttons run the engine (Report = report-only, no views, no model changes). Warnings window still a stub.
- T4: Warnings window (modeless, ExternalEvent, single instance): warning types by severity with count / class / planned fix, TBL_Fix_Guide details, WRN view on selection, Solve this type, Next type, Solve all..., Open report, filters (severity, text, Unpin to fix, Don't ask before fixing). Engine got a read-only `Scan` and a one-type option (`GroupKey`); the ribbon buttons behave as before.
