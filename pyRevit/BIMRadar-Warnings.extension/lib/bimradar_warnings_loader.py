# -*- coding: utf-8 -*-
"""Shared loader for the BIMRadar Warnings Manager pyRevit buttons.

IronPython 2.7 compatible. Usage in a button script:

    from bimradar_warnings_loader import launcher
    launcher(__revit__).Open(__revit__)
"""
import os
import clr

_loaded = {}


class LoaderError(Exception):
    pass


def _extension_root():
    root = os.path.dirname(os.path.abspath(__file__))
    while root and not root.lower().endswith('.extension'):
        parent = os.path.dirname(root)
        if parent == root:
            raise LoaderError("Could not locate the .extension folder from " + os.path.abspath(__file__))
        root = parent
    return root


def _tfm(version):
    if version <= 2024:
        return 'net48'
    if version <= 2026:
        return 'net8'
    return 'net10'


def _already_loaded():
    """#88: reuse the copy already loaded in this Revit session (pyRevit runs each
    click in a fresh engine), so the open Warnings Manager window is shared."""
    from System import AppDomain
    for a in AppDomain.CurrentDomain.GetAssemblies():
        try:
            if a.GetName().Name == 'BIMRadar.Warnings':
                return a
        except Exception:
            pass
    return None


def launcher(uiapp):
    """Return the BIMRadar.Warnings.Launcher class for the running Revit.

    uiapp is the __revit__ object passed in by the calling script.
    Raises LoaderError if the DLL is missing.
    """
    version = int(uiapp.Application.VersionNumber)
    tfm = _tfm(version)
    dll = os.path.join(_extension_root(), 'bin', tfm, 'BIMRadar.Warnings.dll')
    if not os.path.exists(dll):
        raise LoaderError(
            "BIMRadar.Warnings.dll not found for Revit " + str(version) + ":\n" + dll +
            "\n\nUse the pyRevit zip built by CI (it contains bin/" + tfm + ").")
    if dll not in _loaded:
        asm = _already_loaded()
        if asm is None:
            # #88: load from bytes, not from the file path. A DLL that came from a zip
            # downloaded / mailed / on a network share carries the 'blocked' mark, and
            # .NET Framework (Revit 2022-2024) refuses LoadFrom with the CAS
            # 'loadFromRemoteSources' error. Loading the bytes avoids that and also
            # leaves the DLL file unlocked, so updates do not need Revit closed.
            from System.IO import File
            from System.Reflection import Assembly
            asm = Assembly.Load(File.ReadAllBytes(dll))
        clr.AddReference(asm)
        _loaded[dll] = True
    from BIMRadar.Warnings import Launcher
    return Launcher
