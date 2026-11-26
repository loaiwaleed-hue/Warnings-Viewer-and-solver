# Changelog

## Unreleased
- Repo set up from the AICon routine isolate-warnings-rwis-v7.
- T2: RWIS engine (`Rwis/RwisEngine.cs`) ported from v7 with the same behaviour; pure xlsx / classification / severity code in `Rwis/Pure` with unit tests; RWIS template embedded in the DLL.
- T3: Review Warnings, Solve This View and Warnings Report buttons run the engine (Report = report-only, no views, no model changes). Warnings window still a stub.
