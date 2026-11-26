# -*- coding: utf-8 -*-
from pyrevit import forms

try:
    from bimradar_warnings_loader import launcher
    launcher(__revit__).Open(__revit__)
except Exception as e:
    forms.alert(str(e), title='BIMRadar')
