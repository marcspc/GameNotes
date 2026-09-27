# Game Notes — Playnite addon (v1.0.0, no GitHub yet)

A `.NET/C#` plugin (GenericPlugin, `net462`, WPF) for Playnite 10.60.
Per-game notes, fully offline, with a rich-text editor, images stored as
files, tables, and a mini-Excel style formula engine.

## 1. Get a ready-built `.pext` with no local install (recommended)

A Playnite WPF plugin can only be built with Windows tooling, and this
project couldn't be compiled where it was generated (Linux, no access to
nuget.org). The easiest way to get a working `.pext` without installing
Visual Studio is to let **GitHub build it for you, for free**, using the
workflow already included at `.github/workflows/build.yml`:

1. Create a new, empty repository in your GitHub account (can be private).
2. Upload the contents of this `GameNotes/` folder to that repository (drag
   files on the GitHub web UI, or `git init && git add . && git commit &&
   git push`).
3. Go to the **Actions** tab of your repository. A workflow called
   "Build Game Notes .pext" should already be running (or click
   "Run workflow" if it didn't start automatically).
4. Once it finishes (green checkmark, 1-2 minutes), open that run and scroll
   to "Artifacts": you'll find `GameNotes.pext` ready to download.
5. That file is what you double-click to install, with Playnite open.

You don't need Windows, Visual Studio, or NuGet on your own machine for this
path — everything runs on GitHub's servers.

## 2. Alternative: build it yourself locally

1. Install the **.NET Framework 4.6.2 Developer Pack** if you don't have it
   (Playnite desktop runs on .NET Framework, not modern .NET).
2. Open `GameNotes.csproj` with Visual Studio 2022, **or** from a terminal:
   ```
   dotnet restore
   dotnet build -c Release
   ```
   This automatically downloads `PlayniteSDK`, `Newtonsoft.Json` and
   `System.ValueTuple` from NuGet.
3. In `bin/Release/net462/`, you should have `GameNotes.dll` +
   `extension.yaml` + `Newtonsoft.Json.dll`.

## 3. Packaging as `.pext` by hand

(Only needed if you built it yourself in step 2 — if you used GitHub
Actions, the `.pext` is already packaged for you.)

A `.pext` is just a `.zip` renamed, with `extension.yaml` at the root:

```
GameNotes.pext
├── extension.yaml
├── GameNotes.dll
└── Newtonsoft.Json.dll
```

```bash
cd bin/Release/net462
zip -r ../../../GameNotes.pext extension.yaml GameNotes.dll Newtonsoft.Json.dll
```

## 4. Installation / uninstallation

- Double-click `GameNotes.pext` while Playnite is open, or copy it to
  `%AppData%/Playnite/Extensions/GameNotes/`.
- Uninstalling from Playnite only removes the `.dll`; notes live in
  `%AppData%/Playnite/ExtensionsData/{plugin-id}/GameNotes/` and are **never
  touched** on uninstall.

## 5. Two real limitations of the Playnite API (read before testing)

- **A button next to "More" and a native "Notes" tab in the game view:**
  Playnite's public SDK doesn't let a plugin insert a new tab into the game
  details panel — that's controlled by the active theme, not a generically
  supported extension point. What the SDK does offer, and what's used here:
  a **game context menu item** ("Notes") and a **sidebar button** that opens
  the notes for the selected game. Both share the exact same storage (a
  single `NotesStorageService`), so there's no split into separate,
  inconsistent systems — just two entry points instead of three. If you
  later want a real in-panel tab, that would require a compatible Playnite
  theme or a new extension point from Playnite itself; noted here so it
  isn't forgotten.
- **Formulas:** the engine (`Formula/FormulaEngine.cs`) is deliberately
  small: `+ - * /`, `SUM`, `AVERAGE`, `MIN`, `MAX`, cell references and
  ranges (`B2`, `B2:B10`). It has a recursion-depth guard so circular
  references can't hang the app, but it doesn't do "smart" cycle detection —
  it's the simplest approach that avoids ever freezing the addon.

## 6. Storage format

```
%AppData%/Playnite/ExtensionsData/{plugin-id}/GameNotes/
└── Game-{GameId}/
    ├── notes.json          # index: titles, dates, ids
    ├── note-{NoteId}.html  # each note's content
    └── images/
        ├── {imageId}.png   # images as standalone files
        └── ...
```

- `note-*.html` is **not** "internet HTML": it's a custom, well-formed XML
  dialect (`<gamenote><body>...</body></gamenote>`) that only this plugin
  and `UI/HtmlDocConverter.cs` understand. It can be opened in a browser for
  manual inspection, but isn't guaranteed to look pretty there — its only
  job is to be the save format.
- Copying the whole `GameNotes/` folder = a complete backup.
- `notes.json` is small (metadata only), so opening Playnite with a large
  library doesn't force reading every note's content. Images are loaded
  lazily: only when `HtmlDocConverter.FromHtml` opens that specific note.

## 7. How GitHub sync fits into Phase 2 (already designed, not built)

All sync code goes through the `Services/ISyncProvider.cs` interface. In v1
the only implementation is `LocalOnlySyncProvider`, which makes absolutely
no network calls (this holds "by construction": there's no code path
toward the internet).

For Phase 2, all that's needed is:
1. Write `GitHubSyncProvider : ISyncProvider` (browser OAuth for
   authentication, upload/download of `notes.json` + `note-*.html` +
   `images/*`, comparing the saved hash in `NoteMeta.LastSyncedHash` to
   detect conflicts, optional encryption before upload).
2. In `GameNotesPlugin`, choose at runtime between `LocalOnlySyncProvider`
   and `GitHubSyncProvider` based on `GameNotesSettings.GitHubSyncEnabled`.
3. Add a status indicator to the UI (the 5 `SyncStatus` states are already
   defined in `ISyncProvider.cs`) and a conflict-resolution window showing
   both versions.

`NotesStorageService`, the editor, and the storage format wouldn't need to
change at all — that's why `NoteMeta` already includes (unused) the
`LastSyncedHash` and `LastSyncedUtc` fields, and `GameNotesSettings` already
has (disabled in the UI) the GitHub and encryption fields.

## 8. What this v1 covers

- Offline notes: create, edit, rename, delete, per game (by stable
  `Game.Id`, not by name), hidden when multiple games are selected.
- Rich text editor: bold, italic, strikethrough, headings, bulleted/numbered
  lists, links.
- Images: paste with Ctrl+V or insert from a file, stored as separate files
  (never Base64), limited visually to the editor width, orphan cleanup on
  delete.
- Tables with formulas (`SUM`, `AVERAGE`, `MIN`, `MAX`, cell refs and
  ranges), errors shown inline instead of crashing.
- Local storage: atomic writes, corrupted notes/index never break the rest
  of the addon, folder copy = backup.
- Confirmation dialogs before destructive actions, autosave with a status
  indicator.
- No network calls anywhere in v1: nothing is ever uploaded without
  explicit consent, because there's no upload path at all yet.

Not covered (by design, reserved for Phase 2): GitHub sync itself, conflict
resolution UI, end-to-end encryption, incremental sync.

## 9. Things worth testing before calling it done

- Paste a very large image (>4000px) and confirm the editor's `MaxWidth=480`
  doesn't make the saved HTML file heavy (the image is stored as-is when
  pasted; I can add automatic resizing on save if you want).
- Create a table with a formula that depends on a cell later turned into
  non-numeric text, to see the `#ERROR` message live.
- Rename a note several times and confirm the internal `Id` (visible in
  `notes.json`) never changes.
