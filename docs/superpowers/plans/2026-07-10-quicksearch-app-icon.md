# QuickSearch App Icon Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate a light modern technology-style QuickSearch icon and use it consistently in the Windows executable, WPF windows, tray, shortcuts, and installer.

**Architecture:** Keep one transparent PNG as the editable raster source and derive one multi-resolution ICO for every Windows consumer. Embed the ICO into the WPF assembly and executable, load the same embedded resource for the tray, and point Inno Setup at the same file so there is no parallel icon system.

**Tech Stack:** Built-in ImageGen, chroma-key removal helper, ImageMagick 7, .NET 8 WPF, System.Drawing.Icon, Inno Setup 6

## Global Constraints

- Use the approved light-blue folder, translucent magnifier, and minimal speed-light-trail design.
- Use ice cyan, light blue, and white highlights with a modern rounded glass-like finish.
- Keep the background transparent and include no text, letters, logos, realistic scene, floor shadow, or watermark.
- Preserve recognizability at 16×16 and 24×24; the folder and magnifier take priority over decorative detail.
- Do not modify the product name, interface colors, layout, behavior, or copy.
- Preserve unrelated working-tree changes and do not include them in icon commits.

---

### Task 1: Generate and validate the transparent source icon

**Files:**
- Create: `src/QuickSearch.Windows/Assets/QuickSearch-icon.png`
- Temporary: `tmp/imagegen/quicksearch-icon-chroma.png`

**Interfaces:**
- Consumes: approved design in `docs/superpowers/specs/2026-07-10-quicksearch-app-icon-design.md`
- Produces: a square transparent PNG used as the sole source for ICO generation

- [ ] **Step 1: Verify the source asset is absent**

Run:

```bash
test ! -e src/QuickSearch.Windows/Assets/QuickSearch-icon.png
```

Expected: exit 0, proving this task creates a new asset rather than overwriting an existing one.

- [ ] **Step 2: Generate the chroma-key source with built-in ImageGen**

Use this exact prompt:

```text
Use case: logo-brand
Asset type: Windows desktop application icon source, square canvas
Primary request: Create a clean app icon that communicates fast folder search: one rounded light-blue folder as the main subject, a translucent magnifying glass overlapping the lower-right front of the folder, and one or two short speed-light trails behind it.
Scene/backdrop: perfectly flat solid #ff00ff chroma-key background for background removal
Style/medium: polished modern 3D icon, light technology aesthetic, subtle glass material, simplified vector-friendly geometry
Composition/framing: centered, front three-quarter view, strong silhouette, generous padding, readable at 16x16 pixels
Lighting/mood: soft bright studio highlights, calm and efficient
Color palette: ice cyan, pale blue, white highlights; do not use magenta in the subject
Constraints: the background must be one uniform #ff00ff color with no shadow, gradient, texture, reflection, floor plane, or lighting variation; crisp separated edges; no cast shadow; no contact shadow; no text; no letters; no logo; no watermark
Avoid: dark theme, photorealistic office scene, excessive detail, multiple folders, extra symbols, tiny decorations
```

Copy the generated file to:

```text
tmp/imagegen/quicksearch-icon-chroma.png
```

Expected: one square raster image whose border is uniformly `#ff00ff`.

- [ ] **Step 3: Remove the chroma key into the project asset**

Run:

```bash
mkdir -p src/QuickSearch.Windows/Assets tmp/imagegen
python "${CODEX_HOME:-$HOME/.codex}/skills/.system/imagegen/scripts/remove_chroma_key.py" \
  --input tmp/imagegen/quicksearch-icon-chroma.png \
  --out src/QuickSearch.Windows/Assets/QuickSearch-icon.png \
  --auto-key border \
  --soft-matte \
  --transparent-threshold 12 \
  --opaque-threshold 220 \
  --despill
```

Expected: `QuickSearch-icon.png` is written with transparent corners.

- [ ] **Step 4: Validate dimensions, alpha, corner transparency, and subject coverage**

Run:

```bash
python3 - <<'PY'
from PIL import Image

path = "src/QuickSearch.Windows/Assets/QuickSearch-icon.png"
image = Image.open(path).convert("RGBA")
assert image.width == image.height and image.width >= 1024, image.size
alpha = image.getchannel("A")
corners = [alpha.getpixel((0, 0)), alpha.getpixel((image.width - 1, 0)),
           alpha.getpixel((0, image.height - 1)), alpha.getpixel((image.width - 1, image.height - 1))]
assert max(corners) == 0, corners
bbox = alpha.getbbox()
assert bbox is not None
coverage = sum(1 for value in alpha.getdata() if value > 16) / (image.width * image.height)
assert 0.20 <= coverage <= 0.80, coverage
print({"size": image.size, "corners": corners, "bbox": bbox, "coverage": round(coverage, 3)})
PY
```

Expected: the script prints valid square dimensions, four zero-alpha corners, a non-empty bounding box, and coverage between 0.20 and 0.80.

- [ ] **Step 5: Inspect the transparent result visually**

Open `src/QuickSearch.Windows/Assets/QuickSearch-icon.png` with the image viewer.

Expected: recognizable folder and magnifier, no magenta fringe, no text, and no fine detail that disappears at small size. If a thin fringe exists, rerun Step 3 once with `--edge-contract 1` and revalidate.

- [ ] **Step 6: Commit the source asset**

```bash
git add src/QuickSearch.Windows/Assets/QuickSearch-icon.png
git commit -m "design: add QuickSearch icon source"
```

Expected: the commit contains only the transparent PNG.

---

### Task 2: Build and validate the multi-resolution Windows ICO

**Files:**
- Create: `src/QuickSearch.Windows/Assets/QuickSearch.ico`

**Interfaces:**
- Consumes: `QuickSearch-icon.png` from Task 1
- Produces: one ICO containing 16, 20, 24, 32, 40, 48, 64, 128, and 256 pixel layers

- [ ] **Step 1: Verify the ICO does not exist yet**

Run:

```bash
test ! -e src/QuickSearch.Windows/Assets/QuickSearch.ico
```

Expected: exit 0.

- [ ] **Step 2: Generate the ICO with all required sizes**

Run:

```bash
magick src/QuickSearch.Windows/Assets/QuickSearch-icon.png \
  -define icon:auto-resize=256,128,64,48,40,32,24,20,16 \
  src/QuickSearch.Windows/Assets/QuickSearch.ico
```

Expected: ImageMagick writes `QuickSearch.ico`.

- [ ] **Step 3: Validate ICO frame sizes**

Run:

```bash
magick identify -format '%wx%h\n' src/QuickSearch.Windows/Assets/QuickSearch.ico | sort -n -u
```

Expected output:

```text
16x16
20x20
24x24
32x32
40x40
48x48
64x64
128x128
256x256
```

- [ ] **Step 4: Create a small-size contact sheet for visual validation**

Run:

```bash
mkdir -p tmp/icon-review
magick src/QuickSearch.Windows/Assets/QuickSearch.ico \
  -background '#f4f7fb' -alpha background \
  -filter point -resize 256x256 \
  +append tmp/icon-review/quicksearch-icon-sizes.png
```

Expected: the contact sheet clearly shows the folder and magnifier at every embedded size.

- [ ] **Step 5: Commit the ICO**

```bash
git add src/QuickSearch.Windows/Assets/QuickSearch.ico
git commit -m "design: add multi-size Windows icon"
```

Expected: the commit contains only the ICO.

---

### Task 3: Use the icon in WPF, the tray, and Inno Setup

**Files:**
- Modify: `src/QuickSearch.Windows/QuickSearch.Windows.csproj`
- Modify: `src/QuickSearch.Windows/MainWindow.xaml`
- Modify: `src/QuickSearch.Windows/SettingsWindow.xaml`
- Modify: `src/QuickSearch.Windows/MainWindow.xaml.cs`
- Modify: `packaging/QuickSearch.iss`

**Interfaces:**
- Consumes: `Assets/QuickSearch.ico` from Task 2
- Produces: embedded EXE icon, WPF window icons, tray icon, installer icon, and uninstall display icon

- [ ] **Step 1: Verify current icon wiring is incomplete**

Run:

```bash
! rg -n "ApplicationIcon|QuickSearch\.ico|SetupIconFile" \
  src/QuickSearch.Windows/QuickSearch.Windows.csproj \
  src/QuickSearch.Windows/MainWindow.xaml \
  src/QuickSearch.Windows/SettingsWindow.xaml \
  packaging/QuickSearch.iss
rg -n "Drawing\.SystemIcons\.Application" src/QuickSearch.Windows/MainWindow.xaml.cs
```

Expected: the first command succeeds because no icon wiring exists, and the second finds the current default tray icon.

- [ ] **Step 2: Embed the application icon in the project**

Add this property inside the existing `PropertyGroup` in `QuickSearch.Windows.csproj`:

```xml
<ApplicationIcon>Assets\QuickSearch.ico</ApplicationIcon>
```

Add this item group:

```xml
<ItemGroup>
  <Resource Include="Assets\QuickSearch.ico" />
</ItemGroup>
```

Expected: MSBuild embeds the ICO as both the executable icon and a WPF pack resource.

- [ ] **Step 3: Set both WPF window icons**

Add this attribute to the root `Window` in both `MainWindow.xaml` and `SettingsWindow.xaml`:

```xml
Icon="Assets/QuickSearch.ico"
```

Expected: both title bars and taskbar representations use the same icon.

- [ ] **Step 4: Replace the default tray icon**

In `MainWindow.xaml.cs`, replace:

```csharp
Icon = Drawing.SystemIcons.Application,
```

with:

```csharp
Icon = LoadApplicationIcon(),
```

Add this method to `MainWindow`:

```csharp
private static Drawing.Icon LoadApplicationIcon()
{
    var resource = System.Windows.Application.GetResourceStream(
        new Uri("pack://application:,,,/Assets/QuickSearch.ico"))
        ?? throw new InvalidOperationException("QuickSearch icon resource is unavailable.");
    using var icon = new Drawing.Icon(resource.Stream);
    return (Drawing.Icon)icon.Clone();
}
```

Before `_trayIcon.Dispose()` in `Dispose()`, add:

```csharp
_trayIcon.Icon?.Dispose();
```

Expected: the tray owns an independent cloned icon and releases it on application exit.

- [ ] **Step 5: Configure Inno Setup branding**

Add these entries under `[Setup]` in `packaging/QuickSearch.iss`:

```ini
SetupIconFile=..\src\QuickSearch.Windows\Assets\QuickSearch.ico
UninstallDisplayIcon={app}\QuickSearch.exe
```

Expected: the installer executable uses the icon and Windows Apps & Features uses the installed EXE icon.

- [ ] **Step 6: Build the Windows project**

Run:

```bash
/tmp/quicksearch-dotnet8/dotnet build \
  src/QuickSearch.Windows/QuickSearch.Windows.csproj \
  -c Release --verbosity minimal
```

Expected: `Build succeeded`, 0 warnings, 0 errors.

- [ ] **Step 7: Run the full core test suite**

Run:

```bash
dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj \
  -c Release --verbosity minimal
```

Expected: all tests pass with zero failures.

- [ ] **Step 8: Publish and verify the Windows artifacts**

Run:

```bash
/tmp/quicksearch-dotnet8/dotnet publish \
  src/QuickSearch.Windows/QuickSearch.Windows.csproj \
  -c Release -r win-x64 --self-contained true \
  -o artifacts/icon-publish --verbosity minimal
test -f artifacts/icon-publish/QuickSearch.exe
file artifacts/icon-publish/QuickSearch.exe
```

Expected: publish succeeds and `file` reports a Windows PE32+ x86-64 GUI executable.

- [ ] **Step 9: Commit icon integration without unrelated changes**

```bash
git add \
  src/QuickSearch.Windows/QuickSearch.Windows.csproj \
  src/QuickSearch.Windows/MainWindow.xaml \
  src/QuickSearch.Windows/SettingsWindow.xaml \
  src/QuickSearch.Windows/MainWindow.xaml.cs \
  packaging/QuickSearch.iss
git commit -m "feat: apply QuickSearch app icon"
```

Expected: the commit contains only icon consumers.

---

### Task 4: Verify the repository and online Windows package

**Files:**
- Verify: `.github/workflows/windows-release.yml`
- Verify: the latest GitHub Actions run for `feature/quick-search`

**Interfaces:**
- Consumes: committed icon source, ICO, and integrations from Tasks 1–3
- Produces: evidence that the Windows package pipeline still passes with branded artifacts

- [ ] **Step 1: Run fresh local verification**

Run:

```bash
git diff --check
actionlint .github/workflows/windows-release.yml
dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj -c Release --verbosity minimal
/tmp/quicksearch-dotnet8/dotnet build src/QuickSearch.Windows/QuickSearch.Windows.csproj -c Release --verbosity minimal
```

Expected: no whitespace or workflow errors, all tests pass, and WPF build has 0 warnings and 0 errors.

- [ ] **Step 2: Push only the completed icon commits**

```bash
git push origin feature/quick-search
```

Expected: the remote branch advances without force-pushing.

- [ ] **Step 3: Watch the triggered Windows workflow**

Run:

```bash
run_id=$(gh run list --repo Createitv/quick-search \
  --workflow windows-release.yml --limit 1 \
  --json databaseId --jq '.[0].databaseId')
gh run watch "$run_id" --repo Createitv/quick-search --interval 10 --exit-status
```

Expected: tests, self-contained publish, file verification, smoke test, Inno Setup build, and artifact upload all succeed.

- [ ] **Step 4: Record final asset details**

Run:

```bash
shasum -a 256 \
  src/QuickSearch.Windows/Assets/QuickSearch-icon.png \
  src/QuickSearch.Windows/Assets/QuickSearch.ico
git status --short --branch
```

Expected: two SHA-256 values are printed; only pre-existing unrelated working-tree changes remain.
