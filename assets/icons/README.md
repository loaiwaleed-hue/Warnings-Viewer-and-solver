# BIMRadar icons

Shared style rules for the BIMRadar Revit ribbon tab. Other BIMRadar repos reuse these rules.
The icons are original drawings, not copies of Autodesk icons.

## Style

- Full colour, BIM symbol language, transparent background.
- Base: a grey isometric model cube, or the object itself (e.g. a viewpoint, a list), plus ONE coloured badge for the action.
- Outline: `#1e293b` (light theme) / `#e2e8f0` (dark theme, for the Revit dark UI).
- Master size: `viewBox="0 0 32 32"`, exported at 32 px (large button) and 16 px (small button).
- Keep shapes bold and simple: they must read at 16 px. Where the 32 px design is muddy at 16 px there is a simplified 16 px master (`clash-viewer-16.svg`, `clash-viewer.dark-16.svg`).

### Badge colours

| Meaning | Colour |
|---|---|
| Clash | red `#ef4444` |
| Issue | orange `#f97316` |
| Health / check | green `#16a34a` |
| Warning | yellow `#facc15` |
| Fix | orange `#ea580c` |
| Review / navigation | blue `#2563eb` |
| Audit | slate `#475569` |

## Icons

| Icon | Panel |
|---|---|
| `clash-viewer` | Review |
| `clear-overrides` | Review |
| `issues` | Review |
| `viewpoints` | Review |
| `next` | Review (not used in the ribbon) |
| `previous` | Review (not used in the ribbon) |
| `model-health` | Health |
| `warnings` | Health |
| `qaqc-check` | Check |
| `fix` | Fix |
| `audit` | Audit |

## Files

- `<name>.svg`, `<name>.dark.svg`: 32 px masters (light / dark).
- `clash-viewer-16.svg`, `clash-viewer.dark-16.svg`: simplified 16 px masters (clash-viewer only).
- `png/<name>-32.png`, `<name>-16.png`, `<name>.dark-32.png`, `<name>.dark-16.png`: exports.

## Re-export

Run from this folder (needs `rsvg-convert`, from librsvg):

```sh
for f in clash-viewer next previous issues viewpoints model-health warnings qaqc-check fix audit clear-overrides; do
  for s in "" .dark; do
    rsvg-convert -w 32 -h 32 $f$s.svg -o png/$f$s-32.png
    src=$f$s.svg; [ -f $f$s-16.svg ] && src=$f$s-16.svg
    rsvg-convert -w 16 -h 16 $src -o png/$f$s-16.png
  done
done
```

Then copy the light PNGs to `src/BIMRadar.ClashNavigator/Resources/` as `ClashViewer32.png`, `ClashViewer16.png` (`ClearOverrides32.png`, `ClearOverrides16.png` likewise; the next / previous icons are not used in the ribbon, so they are not copied). For pyRevit buttons, copy the 32 px light PNG to each pushbutton's `icon.png` and the dark 32 px PNG to `icon.dark.png`.
