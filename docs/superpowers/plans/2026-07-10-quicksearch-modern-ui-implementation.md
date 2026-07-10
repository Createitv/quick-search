# QuickSearch Modern UI Implementation Plan

**Goal:** Apply the approved Path Beam visual system to the completed MVVM launcher and settings flows without changing their tested behavior.

## Tasks

1. Create `Themes/ModernTheme.xaml` with palette brushes, typography, spacing, focus, button, input, card, list, badge, and status styles; merge it from `App.xaml`.
2. Recompose `MainWindow.xaml` around a modern header, alias-to-folder path beam, status panel, card result list, keyboard hints, and a single primary Open action while retaining all existing bindings/commands.
3. Recompose `SettingsWindow.xaml` into General and Mapping surfaces, add health/status pills and accessible editable mapping rows, while preserving pending Save/Cancel semantics.
4. Integrate the approved QuickSearch icon assets when present; otherwise keep the current icon wiring unchanged and do not block functionality.
5. Verify XAML compilation, focus/keyboard bindings, minimum-size layout, Release build, full tests, and screenshot/render inspection where the current macOS host permits it.

## Constraints

- WPF, Windows 10/11 x64, light theme, Segoe UI Variable with Segoe UI fallback.
- No external font/runtime UI dependency, acrylic, custom window chrome, or behavior changes.
- Preserve unrelated icon-design work and the empty untracked `rea.md`.
- No `Co-Authored-By` commit trailers.
