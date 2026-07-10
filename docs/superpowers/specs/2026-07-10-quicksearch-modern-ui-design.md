# QuickSearch Modern UI Design

## Product and job

QuickSearch is a Windows productivity launcher for one repeated task: turn a copied email alias into a confirmed local folder path and open it quickly. The interface must feel calm and trustworthy because the user verifies paths before opening them.

## Visual direction: Path Beam

The signature element is a horizontal path beam that visually connects the editable alias to the resolved folder. It expresses the product's real data flow rather than adding decoration.

### Tokens

- Fog canvas: `#F3F6FB`
- Paper surface: `#FFFFFF`
- Ink text: `#162033`
- Slate secondary text: `#667085`
- Route blue: `#2F6BFF`
- Signal cyan: `#36C5E5`
- Error text is reserved for actionable failures: `#D92D20`
- Typography: `Segoe UI Variable Display` for titles, `Segoe UI Variable Text` for body and controls, with `Segoe UI` fallback.

### Launcher layout

```text
┌ QuickSearch                         Everything ● Ready   ⚙ ┐
│ Copy an email alias, then confirm the folder path          │
│                                                            │
│ [ Alias / customer name                                  ] │
│       alias chip ───── route beam ───── folder/path card    │
│ [ Real folder name                                  Search ]│
│                                                            │
│  Results                                                   │
│  ▌Folder name                                 Drive badge  │
│   C:\…\full\path                                           │
│                                                            │
│  Esc Hide                    Ctrl+Alt+F       Open folder → │
└────────────────────────────────────────────────────────────┘
```

- Use a 700×600 compact window, 16 px surface radius, 10 px control radius, disciplined 8/12/16/24 spacing, and one restrained shadow around the main surface.
- Search results are white cards with a selected blue rail; full paths remain readable and wrap when necessary.
- Status uses a small semantic dot and direct Chinese guidance, not vague alerts.
- The primary action is the only solid blue button. Secondary actions are quiet outline or text buttons.

### Settings layout

- Use the same header and surface system.
- Separate General and Mapping sections with section labels and cards, not heavy tabs.
- Show Everything health as a compact status pill.
- Mapping rows provide clear Alias and Folder path columns, a selected state, and explicit add/delete actions.
- Cancel discards pending changes; Save changes is the only primary action.

## Interaction and accessibility

- Preserve complete keyboard operation: Enter search/open, Escape hide/cancel, visible focus rings, and readable shortcut hints.
- Use short 120–160 ms color/opacity transitions only where WPF supports them reliably; no continuous animation or acrylic transparency.
- Keep contrast suitable for light mode and avoid color-only status communication.
- Windows 10/11 compatibility takes precedence over ornamental effects.

## Self-review

The initial idea used generic independent cards. It was revised to make the alias-to-folder route beam the single product-specific signature. Gradients are limited to this route signal and primary focus treatment; all surrounding surfaces remain quiet and functional.

