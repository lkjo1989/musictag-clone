# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**MusicTagClone** is a WinForms audio-tag editor (音乐标签). It edits metadata (tags, cover art, lyrics) for audio files and sources metadata from online music services (网易云音乐, QQ音乐, 酷狗音乐, 酷我音乐, iTunes, Last.fm, MusicBrainz, Discogs).

Project home: <https://github.com/lkjo1989/musictag-clone>

**Important:** All UI text, comments, and log messages are Simplified Chinese. New code should match this style.

## Build & test

Requires the .NET 10 SDK (`dotnet --version` → 10.0.100). The solution uses the new XML solution format.

```bash
dotnet build MusicTagClone.slnx            # builds both target frameworks
dotnet test tests/MusicTagClone.Tests/MusicTagClone.Tests.csproj
```

- The app project targets **both** `net10.0-windows` (WinForms) and `net461` (classic .NET Framework). Building the solution compiles both; `dotnet build MusicTagClone.csproj -f net10.0-windows` builds one.
- Run a single test class: `dotnet test ... --filter "FullyQualifiedName~FileScannerServiceTests"`.
- Skip the UI-automation tests: `--filter "FullyQualifiedName!~GUI"`.
- Tests use xUnit + Moq + FlaUI (UI Automation). Several service tests (`*ApiTests`, `CoverService*Tests`, `NeteaseLyricApiTests`) hit **live network APIs** and need internet. The `GUI/MainFormTests.cs` tests launch the built exe via FlaUI and additionally need a Release build plus real audio samples under `D:\binary\testfile\` — note their hardcoded `AppPath` points at `src\MusicTagClone.App\...` which no longer exists; the app project is `src\MusicTagClone`, so those GUI tests will not run as-is.
- The build emits ~120–180 nullable-analysis warnings (CS86xx) — not treated as errors.

## Architecture

Programs and layout:

- `src/MusicTagClone/Program.cs` — entry point. Registers `CodePagesEncodingProvider` (needed for GBK/Big5 etc. on .NET 6+), builds the DI container (`Microsoft.Extensions.DependencyInjection`), hooks global exception handlers, and enforces **single-instance** via `WM_COPYDATA` (magic `0x4D546167`) so a second launch forwards file args to the running window. All services are registered here (singletons); `MainForm` and dialogs are transient.
- `Forms/` — WinForms windows. `MainForm.cs` (~2500 lines) orchestrates everything; its `MainForm.Designer.cs` builds the whole UI **programmatically** (no `.resx`). Other forms are per-feature dialogs (settings, search, auto-match progress, encoding fix, filename relation, lyrics, tag history, about, column select).
- `Controls/TagEditPanel.cs` — left sidebar (fixed 325px, `Dock.Left`): tag fields, per-field encoding-fix buttons, lyrics editor, cover preview. Driven by `MainForm`.
- `Services/` — all business logic behind `Interfaces/` contracts (see below). `Models/` holds data objects plus static "catalog" helpers that serialize user-configurable lists (search fields, tag sources) as JSON in settings.
- `Win32/` — P/Invoke interop (folder picker, file-open dialog, native methods). `Utils/M4aTagFixer.cs`, `ChineseUtils/ChineseConverter.cs`, `Services/KrcDecrypt.cs` / `QrcDecrypt.cs` are format-specific helpers.
- `tests/MusicTagClone.Tests/` — xUnit; mirrors `Models/`, `Services/`, plus `GUI/`.

Main window layout (from `MainForm.Designer.cs`): top `MenuStrip` + `ToolStrip` (FontAwesome icons, grouped/colored via `IconHelper`), left `TagEditPanel`, center `fileListView` (ListView, Details, 22 columns; `ListViewItem.Tag` holds the `MusicFile`), bottom filter bar with status/info labels and a hidden progress bar. Menus/toolbar items are enabled/disabled by a single `RefreshMenuStates()` based on whether a file is selected.

### The tag read/write path — read this first

The most important cross-file design decision is in `Services/TagService.cs`:

- **Reading** uses MediaInfo on **both** targets — `MediaInfo.Wrapper.Core` on `net10.0-windows` and `MediaInfo.Wrapper` (+ `MediaInfo.Native`) on `net461` — which is much more forgiving of malformed/nonstandard MP4 containers (like VLC/FFmpeg) than TagLibSharp. MediaInfo can't return covers/lyrics, so they're back-filled via TagLibSharp, and TagLibSharp is also the fallback when MediaInfo can't parse a file. This path lives behind `#if NET6_0_OR_GREATER || NETFRAMEWORK` — when editing `TagService`, remember both targets compile this code. Two net461 packaging gotchas: (1) `MediaInfo.Native` must be a **direct** package reference — its `build\MediaInfo.Native.targets` copies native DLLs into output `x64\`/`x86\` subdirs, but NuGet only imports `build\` assets for direct references (transitive ones are skipped); (2) `MediaInfo.Wrapper.dll` stays at the output root (excluded in `MoveDepsToLibs`) because the wrapper resolves `MediaInfo.dll` relative to its own `Assembly.Location`.
- **Writing** is always TagLibSharp. If TagLibSharp can't open an M4A/MP4 (malformed container), `Utils/M4aTagFixer.cs` rebuilds a standard `ilst` box (and fixes parent box sizes) so the write succeeds.
- `IFileScannerService`/`FileScannerService` wraps `ITagService` to scan directories and hydrate `MusicFile` objects; sorting/filtering/rename/delete also live there. Supported extensions are enumerated in both `TagService` and `FileScannerService`.

### Online metadata pipeline

`SearchResult` is the shared result shape for covers, lyrics, and combined tags. `SearchCondition` builds the query from a `MusicFile` (title/artist/album order configurable). Flow:

- `CoverService` and `LyricService` each implement one source-family per method and select a per-source `HttpClient` via `IHttpClientFactory` (`"default"` client registered in `Program.cs`), honoring per-source proxy settings (`ProxySourceSettings` JSON + `ProxyUrl`).
- `WebSearchService.AutoMatchTagsAsync` (single-file) and `AutoMatchService` (batch, `SemaphoreSlim`-bounded threads) iterate the user-enabled sources per category — configured by `TagSourceCatalog`/`SearchConditionCatalog` in `Models/`, persisted as JSON in settings, with numeric legacy keys mapped back to names — score results by title/artist/album match and pick the best. `AutoMatchOptions` models the per-field write modes (save to tag / file / both, overwrite) and also persists as JSON.
- `SearchCondition`/`AutoMatchOptions`/`TagSourceCatalog`/`SearchConditionCatalog` all embed **legacy-compatibility parsing** of old settings shapes — keep that tolerance when changing them.

### Persistence: SQLite + a content-addressed image cache

Two services share `MusicTagClone.db` (SQLite) in the app directory:

- `SettingsService` — flat `Settings(Key, Value)` table; typed accessors (`Get<T>()`/`Set<T>()` via `CallerMemberName`), defaults in a `Defaults` dictionary, `Load`/`Save`/`ResetToDefaults`. Add new settings here + in the `ISettingsService` interface + in `Defaults`.
- `TagHistoryService` — `tagshistory` table; serial format `"{prefix}-{counter}"` (prefix increments per session), max 5 records per file, text incl. lyrics in SQLite, covers stored out-of-band. `ClearAll` also wipes the history cover dir.

`IImageCache`/`ImageCacheService` manages two **content-addressed** (sha256 + magic-byte extension) cover-cache dirs under the app dir, which is a common source of subtle bugs:

- `cache\history\` — covers referenced by `tagshistory` rows; **never** auto-cleaned. Reference-set is injected from `TagHistoryService` into `ImageCacheService` in `Program.cs` to break a circular dependency (`SetReferencedCoverPathsProvider`).
- `cache\img\` — URL-download performance cache with a separate `index.db` (`url_cache` table); swept at startup by size-limit LRU + 7-day orphans. `IImageCache.GetOrDownloadAsync` takes a caller-provided `fetcher` so the cache never touches the network.

### Other notable subsystems

- `ChineseUtils/ChineseConverter.cs` — simplified↔traditional conversion using decompressed flat lookup tables (CJK U+4E00–U+9FA5) plus lexeme dictionaries; `S2T_Lexemes`/`T2S_Lexemes` source is generated data.
- `Services/KrcDecrypt.cs` (Kugou, XOR+zlib) and `QrcDecrypt.cs` (QQ, 3DES+zlib) convert encrypted `.krc`/`.qrc` lyrics to LRC.
- `EncodingFixForm` fixes mojibake by re-decoding a field under many codepages (GBK, Big5, Shift-JIS, …) and previewing.
- Logging (`LoggerService`) writes daily-rotating `log/log-YYYY-MM-DD.log`, level-gated; used heavily for diagnostics across services.
