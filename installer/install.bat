@echo off
setlocal EnableDelayedExpansion
rem BIMRadar Warnings Manager - standalone installer (per user, per Revit year 2022-2027).
set "SRC=%~dp0"
set "ADDINS=%AppData%\Autodesk\Revit\Addins"
set "PYCFG=%AppData%\pyRevit\pyRevit_config.ini"
set "COUNT=0"
set "LIST="

for %%Y in (2022 2023 2024 2025 2026 2027) do (
  if exist "%ADDINS%\%%Y" (
    set "TFM=net48"
    if %%Y GEQ 2025 set "TFM=net8"
    if %%Y GEQ 2027 set "TFM=net10"
    if not exist "%SRC%!TFM!\BIMRadar.Warnings.dll" (
      echo Missing "%SRC%!TFM!\BIMRadar.Warnings.dll" - skipping Revit %%Y
    ) else (
      if not exist "%ADDINS%\%%Y\BIMRadar.Warnings" mkdir "%ADDINS%\%%Y\BIMRadar.Warnings"
      copy /Y "%SRC%BIMRadar.Warnings.addin" "%ADDINS%\%%Y\" >nul
      copy /Y "%SRC%!TFM!\BIMRadar.Warnings.dll" "%ADDINS%\%%Y\BIMRadar.Warnings\" >nul
      if exist "%SRC%!TFM!\BIMRadar.Warnings.pdb" copy /Y "%SRC%!TFM!\BIMRadar.Warnings.pdb" "%ADDINS%\%%Y\BIMRadar.Warnings\" >nul
      powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%ADDINS%\%%Y\BIMRadar.Warnings' -Recurse | Unblock-File; Unblock-File -LiteralPath '%ADDINS%\%%Y\BIMRadar.Warnings.addin'" >nul 2>&1
      echo Installed for Revit %%Y ^(!TFM!^)
      set /a COUNT+=1
      set "LIST=!LIST! %%Y"
    )
  )
)

echo.
if exist "%PYCFG%" (
  findstr /i /r /c:"^userextensions.*Warnings" "%PYCFG%" >nul 2>&1
  if not errorlevel 1 (
    echo WARNING: pyRevit loads a BIMRadar Warnings folder ^(Settings ^> Custom Extension Directories^).
    echo If it holds the Warnings Manager extension, this add-in stays hidden to avoid two Health panels.
    echo.
  )
)

echo ---- Summary ----
if "!COUNT!"=="0" (
  echo No Revit 2022-2027 Addins folder found under !ADDINS!. Nothing installed.
) else (
  echo Installed for !COUNT! Revit version^(s^):!LIST!
  echo Restart Revit.
)
pause
