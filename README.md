# Game Launcher

A dark, PlayStation-5-inspired **Windows desktop game launcher** built with **C# / WPF (.NET 8)** and the **MVVM** pattern.

Add your games once (an `.exe` **or** a Steam URL), browse them in a smooth carousel, and press **PLAY** — the launcher
automatically cleans your temporary files before every start.

![theme](https://img.shields.io/badge/theme-PS5--style-0b0e14) ![platform](https://img.shields.io/badge/platform-Windows-0070d1) ![framework](https://img.shields.io/badge/.NET-8-512bd4)

---

## Features

- **PS5-style UI** — dark theme, full-screen background artwork that cross-fades when you switch games, and a
  horizontally scrolling carousel of game logos (the selected logo scales up and brightens, the others stay dimmed).
- **Add / Edit / Delete games** with title, launch target, logo, background and an optional per-game cache folder.
- **Flexible launch targets** — either a path to an `.exe` file or a URI such as `steam://rungameid/12345`.
- **Automatic pre-launch cleanup** of `%TEMP%`, `C:\Windows\Temp` and (optionally) a custom game cache folder, with a
  visible loading indicator and graceful handling of locked files.
- **Persistent library** stored as JSON in `%LocalAppData%\GameLauncher\library.json`.
- **Administrator manifest** (`requireAdministrator`) so Windows Temp files can be cleaned.
- **Automated release pipeline** that publishes a self-contained single-file `.exe` on every `v*` tag.

---

## Requirements

| Requirement | Version |
| --- | --- |
| Windows | 10 / 11 (64-bit) |
| .NET SDK | **8.0** or newer |
| Visual Studio | 2022 17.8+ (optional, any editor works) |

> The app requests administrator rights through `app.manifest`. Start Visual Studio (or your terminal) **as
> administrator** when you run it locally, otherwise Windows will refuse to start the process.

---

## Run locally

```powershell
# from the repository root
dotnet run
```

Or build a release binary:

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
.\publish\GameLauncher.exe
```

### First run

1. Press **Add**.
2. Fill in **Game Title**.
3. Enter a **Launch Target**:
   - an executable path, e.g. `C:\Games\Demo\demo.exe` (use **Browse...**), or
   - a Steam URL, e.g. `steam://rungameid/12345`.
4. Optionally pick a **Logo Image** (`.ico` / `.png` / `.jpg`) and a **Background Image** (`.png` / `.jpg`).
5. Optionally pick a **Custom Game Cache Folder** whose contents should be deleted before every launch.
6. Press **Save**, select the game in the carousel and press **PLAY**.

---

## How it works

### Pre-launch cleanup

Pressing **PLAY** runs `TempCleanerService` before the game is started:

1. Deletes every file and folder inside `%TEMP%` (`Path.GetTempPath()`).
2. Deletes every file and folder inside `C:\Windows\Temp`.
3. Deletes the contents of the game's **Custom Game Cache Folder** (when configured).

Every entry is deleted inside its own `try/catch`, so files that are locked or protected are simply skipped — the
launcher never crashes because of a temp file. While the cleanup runs, a modal overlay shows a spinner and the message
**“Cleaning Temp files…”**, with a **Cancel** button.

### Launching

```csharp
var startInfo = new ProcessStartInfo { UseShellExecute = true, FileName = target };
Process.Start(startInfo);
```

`UseShellExecute = true` lets Windows resolve `steam://` URLs through the registered protocol handler. For `.exe`
targets the working directory is set to the executable's folder, and a missing file produces a friendly error dialog.

### Storage

The library is serialised to JSON:

```
%LocalAppData%\GameLauncher\library.json
```

If that file is corrupted it is renamed to `library.json.bak` and the launcher starts with an empty library instead of
crashing.

---

## Project structure

```
GameLauncher/
├── GameLauncher.csproj          # .NET 8 / WPF / WinForms (folder picker) / app.manifest
├── app.manifest                 # requireAdministrator
├── App.xaml / App.xaml.cs       # styles, converters, composition root
├── MainWindow.xaml(.cs)         # PS5-style dashboard (carousel, background fade, overlay)
├── Models/
│   ├── GameItem.cs              # library entry
│   └── GameTarget.cs            # .exe vs. URI detection & validation
├── Services/
│   ├── IGameLibraryService.cs   # JSON persistence (AppData)
│   ├── GameLibraryService.cs
│   ├── ITempCleanerService.cs   # %TEMP% / C:\Windows\Temp cleanup
│   ├── TempCleanerService.cs
│   ├── IGameLauncherService.cs  # Process.Start wrapper
│   ├── GameLauncherService.cs
│   └── IDialogService.cs        # dialogs abstraction (testable MVVM)
├── ViewModels/
│   ├── ObservableObject.cs      # INotifyPropertyChanged base
│   ├── RelayCommand.cs          # RelayCommand + AsyncRelayCommand
│   ├── MainViewModel.cs         # library, selection, play pipeline
│   └── GameEditorViewModel.cs   # add/edit dialog
├── Views/
│   ├── DialogService.cs         # WPF dialogs (OpenFileDialog / FolderBrowserDialog / MessageBox)
│   └── GameEditorWindow.xaml(.cs)
└── Converters/                  # path → image, bool → visibility, …
```

The MVVM layer never touches UI types directly: all dialogs go through `IDialogService`, and the view models expose
plain data + `ICommand`s.

---

## Automated release pipeline

The workflow lives in [`.github/workflows/release.yml`](.github/workflows/release.yml).

It runs on `windows-latest` whenever a tag matching `v*` is pushed and:

1. checks out the code,
2. installs the .NET 8 SDK (`actions/setup-dotnet@v4`),
3. publishes a **self-contained single-file** Windows executable
   (`dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`),
4. zips the published files,
5. creates a GitHub release and attaches the ZIP (`softprops/action-gh-release@v2`).

The job has `permissions: contents: write`.

### Trigger a release

```powershell
git commit -am "Release v1.0.0"
git tag v1.0.0
git push origin main
git push origin v1.0.0
```

The workflow creates the **v1.0.0** release with `GameLauncher.zip` attached. Bump the tag (e.g. `v1.0.1`) to ship a new
build.

---

## Notes

- Keyboard: `←` / `→` move through the carousel, the **PLAY** button launches the selected game.
- Deleting a game asks for confirmation first.
- Backgrounds and logos are referenced by path — keep the image files where they are, or the launcher falls back to
  its default artwork.
