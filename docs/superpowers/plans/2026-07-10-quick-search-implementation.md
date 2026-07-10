# QuickSearch Implementation Plan

> **For agentic workers:** Implement each task with test-first development, report exact verification commands, and do not add `Co-Authored-By` trailers.

**Goal:** Build a Windows 10/11 tray utility that maps copied email aliases to deeply nested local folders and opens the confirmed target in Explorer.

**Architecture:** A cross-platform `QuickSearch.Core` library owns alias normalization, mapping persistence, search result ranking, and use-case orchestration. A `QuickSearch.Windows` WPF executable owns Windows-only clipboard, Everything SDK, global hotkey, tray, startup, single-instance, Explorer, and UI behavior. Configuration is local JSON under `%LOCALAPPDATA%\QuickSearch`.

**Tech Stack:** .NET 8, C# 12, WPF, xUnit, System.Text.Json, Everything 1.4 x64 SDK, Windows Forms `NotifyIcon`, Inno Setup.

## Global Constraints

- Target Windows 10/11 x64 and .NET 8; no network or email-provider integration.
- Default global shortcut is `Ctrl+Alt+F`; start-with-Windows defaults to enabled.
- The app is single-instance, closes to tray, and always confirms a saved mapping before opening it.
- Alias lookup trims outer whitespace, collapses internal whitespace, and is case-insensitive for Latin text.
- One normalized alias maps to one folder; multiple aliases may map to the same folder.
- Search only directories via Everything, request at most 100 candidates, rank exact name before prefix before substring, and show at most 20.
- Never fall back to recursive full-disk scanning when Everything is unavailable.
- Store only settings and alias-to-path mappings; never store email bodies or upload local data.
- Do not append `Co-Authored-By` trailers to commits.

---

### Task 1: Core Domain, Mapping Store, and Ranking

Create `QuickSearch.sln`, `src/QuickSearch.Core`, and `tests/QuickSearch.Core.Tests`. Define `FolderMapping`, `FolderSearchResult`, `AppSettings`, `AppConfiguration`, `IMappingStore`, and `IFolderSearch`. Implement alias normalization, exact normalized lookup, automatic create/overwrite, multiple aliases per path, deterministic ranking, and JSON persistence using temp-file atomic replacement. A corrupt JSON file must be moved to `config.corrupt-<UTC timestamp>.json`, then defaults returned. Use TDD and commit the task.

Required tests: whitespace/case normalization; lookup; overwrite; multiple aliases to one path; exact/prefix/substring ranking with stable path tie-break; result limit 20; round-trip persistence; atomic replacement; corrupt-file recovery.

### Task 2: Windows Platform Integrations

Create `src/QuickSearch.Windows` targeting `net8.0-windows` with WPF and Windows Forms enabled plus `EnableWindowsTargeting=true`. Implement adapters for Everything 1.4 x64 SDK, clipboard, `RegisterHotKey`, Explorer launch, HKCU Run startup, named-mutex single instance, and tray icon. Keep native declarations isolated. Everything queries must use `folder:` with literal-safe input, request full paths, cap at 100, expose explicit unavailable/not-ready/query-failed states, and never scan disks. Add testable query construction and adapter boundary tests before production code. Commit the task.

### Task 3: Launcher and Settings UI

Implement a compact launcher window and settings window with MVVM. Hotkey activation reads editable clipboard text. A valid saved mapping shows alias, folder name, and full path; Enter opens it, Escape hides. A missing or stale mapping shows the editable alias plus a real-folder-name field, debounces Everything search, displays up to 20 full-path candidates, and saves/overwrites the mapping when the selected folder is opened. Implement settings for shortcut registration, startup toggle, Everything health, and mapping edit/delete. Closing windows hides them; only tray Exit terminates. Add view-model tests first and commit the task.

### Task 4: Packaging, Documentation, and End-to-End Verification

Add official Everything x64 SDK DLL integration instructions and third-party MIT notice without bundling the Everything client. Add an Inno Setup script for a self-contained `win-x64` publish, shortcut creation, upgrade behavior, and uninstall that removes the HKCU Run entry but preserves `%LOCALAPPDATA%\QuickSearch`. Add Chinese README instructions for installing normal (non-Lite) Everything 1.4, building, testing, packaging, first launch, and troubleshooting. Run all feasible tests/builds on the current host, document Windows-only verification commands, and commit the task.

Acceptance scenarios: saved mapping confirmation; first-time mapping and persistence; duplicate names across drives; stale path repair; empty clipboard; Everything stopped; shortcut conflict; restart persistence; close-to-tray; explicit exit; Windows login startup. Performance targets on an indexed Windows machine are launcher visible within 300 ms and initial results within 500 ms.
