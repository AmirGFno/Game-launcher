# GFno Launcher

A dark, minimalist, animated **Windows desktop game launcher** — built with **C# / WPF on
.NET 10** and the **MVVM** pattern. Point it at your games (an `.exe` **or** a Steam URL) and
it pulls the name and logo straight out of the executable, then launches with a
PlayStation-5-inspired carousel.

![theme](https://img.shields.io/badge/theme-PS5--style-0b0e14) ![platform](https://img.shields.io/badge/platform-Windows-0070d1) ![framework](https://img.shields.io/badge/.NET-10-512bd4)

> **v2.0.0** — stable release. The backend has been safety-hardened and modernised
> (Phase 1–2 of the roadmap). The React + WebView2 frontend (Phase 3–6) is the next increment.

---

## Features

- **Minimalist PS5-style UI** — a slowly zooming (**Ken Burns**) background, the selected
  game's title floating above a horizontal logo carousel, and a soft fade into the dark.
- **Auto game detection** — picking an `.exe` reads `FileVersionInfo.FileDescription` for the
  title (falling back to the file name) and extracts the embedded icon, storing it as a PNG in
  `%LocalAppData%\GameLauncher\Icons`. Both fields stay fully editable.
- **Artwork from a URL** — paste a direct image link into **Logo** or **Background** and press
  **Download**; the file is fetched asynchronously and stored under
  `%LocalAppData%\GameLauncher\Images`.
- **Launch your way** — press **Enter** or **double-click** a logo. `←` / `→` and the mouse
  wheel browse the library; a single click selects.
- **Buttery animations** — scale-up of the selected tile (a pure `RenderTransform`, so it never
  reflows the row), a pulsing glow tinted with the logo's **dominant colour**, cross-fading
  backgrounds and a slide-up title.
- **Bounded artwork cache** — logos and wallpapers are decoded on a worker thread, downscaled
  while decoding (max **1920px** backgrounds, **256px** logos) and cached with a **256 MB**
  budget and write-time/size invalidation. Every decoded bitmap is frozen before it is bound.
- **Off-thread colour sampling** — the dominant logo colour is extracted on a background
  thread, cached on the game and persisted in `library.json`, so the same image is never
  sampled twice.
- **Safe artwork downloads** — 25 MB response cap, streamed to a temporary file, real-image
  validation (dimensions included) and automatic deletion of incomplete files.
- **Safe, optional cache cleanup** — cleanup only ever touches the app's own cache folder
  (`%LocalAppData%\GameLauncher\Cache`), shows a preview, asks for confirmation and is
  cancellable. `%TEMP%`, `C:\Windows\Temp` and per-game folders are **never** deleted.
- **Atomic library persistence** — `library.json` is written to a temp file and swapped in
  atomically, with a `.bak` copy; saves are serialised so concurrent writes cannot corrupt it.
- **Restricted launch targets** — only an explicit allowlist of protocols (`steam`, Epic,
  Ubisoft, Origin, Battle.net, GOG Galaxy) plus `.exe` paths. Arbitrary URI schemes are
  rejected.
- **Runs as a standard user** — no UAC prompt, `PrivilegesRequired=lowest` installer.
- **Single instance**, **system tray**, **WebP support**, **reorder mode**, **installer +
  portable build** on every `v*` tag.

---

## Requirements

| Requirement | Version |
| --- | --- |
| Windows | 10 / 11 (64-bit) |
| .NET SDK | **10.0** or newer |
| Visual Studio | 2022 17.14+ / VS Code (optional) |

> The launcher runs with **standard user** rights. Nothing it does requires elevation, so the
> manifest requests `asInvoker` and the installer uses `lowest` privileges. A game that
> itself needs elevation will trigger Windows' own prompt when it is launched.

---

## Run locally

```powershell
# from the repository root
dotnet run
```

Or produce the same binaries the release pipeline produces:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
.\publish\GFnoLauncher.exe
```

### Tests

```powershell
dotnet test tests/GFnoLauncher.Tests/GFnoLauncher.Tests.csproj -c Release
```

The suite covers launch-target validation, atomic library persistence, safe cleanup
(path canonicalisation, protected-location rejection, cancellation), image validation and
metadata preservation.

### First run

1. Press **Add**.
2. Press **Browse…** next to **Launch Target** and pick a game executable.
   - **Game Title** is filled from the executable's file description.
   - **Logo Image** is filled with the icon embedded in the executable.
   - Either value can be overwritten by hand; pick a Steam URL
     (`steam://rungameid/12345`) instead if you prefer.
3. Optionally set a **Background Image**.
4. Press **Save**, then **Enter** (or double-click the logo) to play.

For the **Logo Image** / **Background Image** fields you can either **Browse** for a local file
or paste a direct image URL and press **Download** — the image is stored under
`%LocalAppData%\GameLauncher\Images`.

---

## How it works

### Auto-extracting game info

`Services/GameInfoExtractor.cs`:

- **Name** — `FileVersionInfo.GetVersionInfo(exePath).FileDescription`, falling back to the
  file name without extension.
- **Logo** — `Icon.ExtractAssociatedIcon(exePath)` → `Bitmap` → WPF `BitmapSource` →
  `PngBitmapEncoder` → `%LocalAppData%\GameLauncher\Icons\<guid>.png`.

The editor remembers which values came from the executable, so choosing a different `.exe`
refreshes them while anything you typed yourself is preserved. Saving an edit **clones** the
existing `GameItem`, so `LastPlayed`, `LaunchCount`, favourites, categories and the cached
dominant colour always survive.

### Launching

`Models/GameTarget.cs` validates the target against an explicit allowlist before anything is
started:

```csharp
var startInfo = new ProcessStartInfo { UseShellExecute = true, FileName = target };
Process.Start(startInfo);
```

`UseShellExecute = true` lets Windows resolve `steam://` URLs through the registered protocol
handler. For `.exe` targets the working directory is set to the executable's folder, and a
missing file produces a friendly error dialog. The launch path itself is deliberately
lightweight: no cleanup, no artificial delay.

### Safe cleanup (optional)

`Services/TempCleanerService.cs` only ever cleans `%LocalAppData%\GameLauncher\Cache`:

- every candidate is expanded with `Path.GetFullPath` and must lie **strictly inside** the app
  cache root;
- drive roots, junctions / symbolic links and protected system / personal folders
  (`PathSafety.cs`) are rejected;
- the UI shows a **preview** (item count + size), asks for confirmation, and the operation is
  cancellable.

The legacy **Custom Game Cache Folder** field is kept for backward compatibility but is never
deleted automatically.

### Persistence

`Services/GameLibraryService.cs`:

1. serialize to `library.json.tmp` and flush,
2. atomically `File.Replace` it over `library.json`, keeping the previous file as `.bak`,
3. serialise writers with a `SemaphoreSlim` so fire-and-forget saves cannot interleave.

A corrupted file is moved to `library.json.corrupt-<timestamp>` (never deleted), so the
library is always recoverable. The format is unchanged, so existing libraries load as-is.

---

## Project structure

```
GameLauncher/
├── GameLauncher.csproj          # .NET 10 / WPF / WinForms / app icon / app.manifest
├── app.manifest                 # asInvoker (standard user)
├── setup.iss                    # Inno Setup installer (lowest privileges)
├── Assets/
│   ├── icon.ico                 # application icon (embedded in the .exe)
│   └── icon.png                 # source artwork for the icon / window icon
├── App.xaml / App.xaml.cs       # styles, converters, composition root
├── MainWindow.xaml(.cs)         # dashboard: Ken Burns background, carousel, animations
├── Models/                      # GameItem, GameTarget (protocol allowlist)
├── Services/                    # library persistence, safe cache cleaner, launcher,
│                                #   game info extraction, image download, dialogs,
│                                #   dominant-colour extraction, path safety
├── Controls/                    # AsyncImage (background-thread, cached artwork)
├── ViewModels/                  # ObservableObject, RelayCommand(+Async), MainViewModel,
│                                #   GameEditorViewModel
├── Views/                       # DialogService, GameEditorWindow
├── Converters/                  # ImageLoader (bounded cache)
└── tests/
    └── GFnoLauncher.Tests/      # xUnit: targets, persistence, cleanup, images
```

---

## Release pipeline

Two workflows live in [`.github/workflows`](.github/workflows):

- **`ci.yml`** — on every push to `main` and every pull request: builds the desktop app and
  runs the test suite on `windows-latest` with the .NET 10 SDK.
- **`release.yml`** — on every `v*` tag: runs the tests, publishes a **self-contained
  single-file** Windows executable (`dotnet publish -c Release -r win-x64 --self-contained
  true -p:PublishSingleFile=true`), verifies the publish output, zips it as
  **`GFnoLauncher-<version>-win-x64-portable.zip`**, installs **Inno Setup 7** and compiles
  **`setup.iss`** into **`GFnoLauncher-Setup-<version>.exe`**, generates a
  **`SHA256SUMS.txt`** for both packages, then attaches all three to a GitHub release.
  The `<Version>` in `GameLauncher.csproj` is cross-checked against the tag, so a mismatched
  tag fails the release instead of publishing the wrong file names.

### Trigger a release

```powershell
git commit -am "Release v2.0.0"
git tag v2.0.0
git push origin main
git push origin v2.0.0
```

### Building the installer locally

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
& "C:\Program Files\Inno Setup 7\ISCC.exe" setup.iss
.\GFnoLauncher-Setup.exe
```

---

## Roadmap (v2)

Phase 1–2 are complete. The remaining phases are incremental:

1. **Audit & preservation** — done.
2. **Backend safety & modernisation** — done (safe cleanup, no admin, .NET 10, atomic
   persistence, bounded cache, download validation, target allowlist, editor metadata, tests).
3. **Frontend architecture** — React + TypeScript + Vite frontend hosted in WebView2, with a
   typed, allowlisted message bridge and packaged local assets.
4. **UI redesign** — dashboard/hero, carousel, library (grid + search + sort + favourites),
   game details, editor, settings.
5. **Performance features** — quick launch, per-game launch profiles, optional startup
   preparation, lightweight process monitoring, real recommendations.
6. **Final testing & release** — migration tests, portable + installer verification,
   resolution/DPI checks.

---

## Notes

- **Keyboard** — `←` / `→` browse, `Enter` launches, double-click launches. With the **↔**
  reorder button active, `←` / `→` move the selected game instead and `Esc` exits the mode.
- Deleting a game asks for confirmation first.
- Backgrounds and logos are referenced by path; missing artwork falls back to the built-in
  gradient and a placeholder glyph.
- The library lives in `%LocalAppData%\GameLauncher\library.json` and is compatible with
  every previous release.
