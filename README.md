# GFno Launcher

A dark, extremely minimalist, heavily animated **Windows desktop game launcher** — built with
**C# / WPF (.NET 8)** and the **MVVM** pattern. Point it at your games (an `.exe` **or** a Steam
URL) and it pulls the name and logo straight out of the executable, then launches with a
PlayStation-5-style carousel.

![theme](https://img.shields.io/badge/theme-PS5--style-0b0e14) ![platform](https://img.shields.io/badge/platform-Windows-0070d1) ![framework](https://img.shields.io/badge/.NET-8-512bd4)

---

## Features

- **Minimalist PS5-style UI** — no play button, no clutter. Just a slowly zooming
  (**Ken Burns**) background, the selected game's title floating just above a horizontal
  logo carousel, and a soft fade into the dark at the bottom edge.
- **Auto game detection** — picking an `.exe` reads `FileVersionInfo.FileDescription` for the
  title (falling back to the file name) and extracts the embedded icon with
  `System.Drawing.Icon.ExtractAssociatedIcon`, storing it as a PNG in
  `%LocalAppData%\GameLauncher\Icons`. Both fields stay fully editable.
- **Artwork from a URL** — paste a direct image link into the **Logo** or **Background** field
  and press **Download**; the file is fetched asynchronously and stored under
  `%LocalAppData%\GameLauncher\Images`. Invalid links and network failures are reported.
- **Launch your way** — press **Enter** or **double-click** a logo. `←` / `→` and the mouse
  wheel browse the library; a single click selects.
- **Buttery animations** — springy scale-up of the selected tile, a pulsing glow behind it,
  hover lift + glow, cross-fading backgrounds and a slide-up title on every change.
- **Automatic pre-launch cleanup** of `%TEMP%`, `C:\Windows\Temp` and an optional per-game
  cache folder, with a modal progress overlay and a **Cancel** button.
- **Persistent library** stored as JSON in `%LocalAppData%\GameLauncher\library.json`.
- **Administrator manifest** (`requireAdministrator`) so Windows Temp files can be cleaned.
- **Installer + portable build** published automatically on every `v*` tag.

---

## Requirements

| Requirement | Version |
| --- | --- |
| Windows | 10 / 11 (64-bit) |
| .NET SDK | **8.0** or newer |
| Visual Studio | 2022 17.8+ (optional, any editor works) |

> The app requests administrator rights through `app.manifest`. Start Visual Studio (or your
> terminal) **as administrator** when you run it locally, otherwise Windows will refuse to
> start the process.

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

### First run

1. Press **Add**.
2. Press **Browse…** next to **Launch Target** and pick a game executable.
   - The **Game Title** is filled from the executable's file description.
   - The **Logo Image** is filled with the icon embedded in the executable.
   - Either value can be overwritten by hand; pick a Steam URL
     (`steam://rungameid/12345`) instead if you prefer.
3. Optionally set a **Background Image** and a **Custom Game Cache Folder**.
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
refreshes them while anything you typed yourself is preserved.

### Pre-launch cleanup

Pressing **Enter** / double-clicking runs `TempCleanerService` before the game starts:

1. Deletes every file and folder inside `%TEMP%` (`Path.GetTempPath()`).
2. Deletes every file and folder inside `C:\Windows\Temp`.
3. Deletes the contents of the game's **Custom Game Cache Folder** (when configured).

Every entry is deleted inside its own `try/catch`, so locked or protected files are simply
skipped — the launcher never crashes because of a temp file.

### Launching

```csharp
var startInfo = new ProcessStartInfo { UseShellExecute = true, FileName = target };
Process.Start(startInfo);
```

`UseShellExecute = true` lets Windows resolve `steam://` URLs through the registered protocol
handler. For `.exe` targets the working directory is set to the executable's folder, and a
missing file produces a friendly error dialog.

---

## Project structure

```
GameLauncher/
├── GameLauncher.csproj          # .NET 8 / WPF / WinForms / app icon / app.manifest
├── app.manifest                 # requireAdministrator
├── setup.iss                    # Inno Setup installer script
├── Assets/
│   ├── icon.ico                 # application icon (embedded in the .exe)
│   └── icon.png                 # source artwork for the icon / window icon
├── App.xaml / App.xaml.cs       # styles, converters, composition root
├── MainWindow.xaml(.cs)         # dashboard: Ken Burns background, carousel, animations
├── Models/                      # GameItem, GameTarget
├── Services/                    # library persistence, temp cleaner, launcher,
│                                #   game info extraction, dialogs abstraction
├── ViewModels/                  # ObservableObject, RelayCommand(+Async), MainViewModel,
│                                #   GameEditorViewModel
├── Views/                       # DialogService, GameEditorWindow
└── Converters/                  # path → image, bool ↔ visibility
```

---

## Release pipeline

The workflow lives in [`.github/workflows/release.yml`](.github/workflows/release.yml). It runs
on `windows-latest` whenever a tag matching `v*` is pushed, then:

1. checks out the code and installs the .NET 8 SDK,
2. publishes a **self-contained single-file** Windows executable
   (`dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`),
3. zips it as **`GameLauncher.zip`** (portable),
4. installs **Inno Setup 7**, compiles **`setup.iss`** into **`GameLauncherSetup.exe`**,
5. creates a GitHub release with **both** files attached
   (`softprops/action-gh-release@v2`, `contents: write`).

### Trigger a release

```powershell
git commit -am "Release v1.2.0"
git tag v1.2.0
git push origin main
git push origin v1.2.0
```

`GameLauncherSetup.exe` installs the launcher for all users (start-menu shortcut, optional
desktop shortcut) and requests elevation because the launcher cleans system temp folders.

### Building the installer locally

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
& "C:\Program Files\Inno Setup 7\ISCC.exe" setup.iss
.\GameLauncherSetup.exe
```

---

## Notes

- **Keyboard** — `←` / `→` browse, `Enter` launches, double-click launches.
- Deleting a game asks for confirmation first.
- Backgrounds and logos are referenced by path; missing artwork falls back to the built-in
  gradient and a placeholder glyph.
- The library survives restarts; a corrupted `library.json` is renamed to `.bak` instead of
  crashing the app.
