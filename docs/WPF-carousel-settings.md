# Native WPF carousel/settings update

C#/.NET 10 and WPF only. No web frontend or architectural split.

## Navigation

- Left/Right, mouse wheel, or click select games. Enter/double-click launches.
- Loop mode uses a recycled, bounded window of tiles. More than five games wraps in the adjacent direction; five or fewer clamp at the ends. A QuarticEase translation and CubicEase scale transition move the selection to the center. Tile colors are preserved.
- Fixed Anchor places the selection at two slot widths from the left; navigation clamps at the ends.
- Edge tiles are clipped at their centers and faded with an opacity mask in Loop mode.
- Title is removed from the old centered heading and appears with a vertical indicator below/beside the selected tile area.

## Settings

The bottom-left gear opens a native modal. Save persists `%LOCALAPPDATA%\GameLauncher\settings.json` atomically with a backup. Cancel leaves preferences unchanged. Artwork deletion is a separate confirmed action and cannot be undone by Cancel.

Fullscreen hides the header; F11 restores it. Close-to-tray falls back to quit if no tray icon can be created. Pre-launch cleanup defaults off and only cleans the existing app-owned Cache directory. Artwork clearing only deletes immediate image files from the app's Images/Icons folders with PathSafety validation. It never recursively deletes arbitrary paths. Library entries and external artwork are retained.

## Steam

This branch's v2.0.0 baseline had no scanner. The new top-right scan reads SteamPath and local libraryfolders.vdf/appmanifest files, skips existing matching Steam targets and imports installed entries through the existing library service. Downloads use:

- `https://cdn.cloudflare.steamstatic.com/steam/apps/{appid}/library_600x900.jpg`
- `https://cdn.cloudflare.steamstatic.com/steam/apps/{appid}/library_hero.jpg`

The existing bounded/validated ImageDownloader is reused. Failed/offline artwork downloads do not discard the game; users can choose artwork in the editor. Scan cancellation uses the existing operation overlay Cancel button.

## Validation and versioning

Tests cover wrap direction/indexing, settings persistence/backup, manifest parsing/CDN names and an STA WPF carousel animation smoke test. Full interactive Windows testing and live Steam CDN access are not available in this sandbox.

The requested v2.1.0 tag already points to an older release and must not be overwritten. This update uses v2.1.2. Existing release tags and PR #20 remain untouched.
