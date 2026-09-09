# TuiEdit

A fast keyboard-first TUI text editor.
Pure `System.Console`, no third-party libraries. .NET 10.

See [CHANGELOG.md](CHANGELOG.md) for release history.

## Features

- Editing: undo/redo (per keystroke and per block), `Shift+arrows` selection, cut/copy/paste line (`^K`, `^C`, `^U`/`^V`), duplicate (`^D`), toggle line comment (`Ctrl+/`), matching-bracket highlight and jump (`Alt+]`), move lines (`Alt+↑/↓`), word-wise delete/move, trim trailing whitespace, sort lines.
- Find (`^F`, `F3`/`Shift+F3`): live highlight while typing, wrap-around, `k/N` counter, match case / whole words / regex toggles right in the prompt (`Alt+C/W/R`). Instant whole-document replace (`^H`) in a single undo step, with confirmation past 50 matches. Grep across files (`Ctrl+Shift+F`) with jump-to-hit.
- Syntax highlighting from JSON grammars (C#, Python, JavaScript/TypeScript, JSON, Markdown, PowerShell, XML, INI built in). On first run the grammars are extracted to the `grammars` folder next to `settings.json` — edit them to customize, drop in your own.
- File manager for Open/Save as: drives, `..`, file highlight with sizes, overwrite confirmation in a separate window. `F7` new folder, `F8` delete (with confirmation, recursive), `Ctrl+H` hidden files. Name field and Find/Replace/GoTo prompts share one line editor: selection (`Shift`), word-wise motion (`Ctrl+arrows`), word delete (`Ctrl+BS/Del`), `Alt+←/→` navigation in the manager.
- Format preservation: encoding (UTF-8/BOM/UTF-16), line endings (CRLF/LF/CR) and indent are detected on open and kept on save; all three are switched in the File format dialog (`F9`, shown in the status bar). Read-only files are flagged `[read-only]` and refuse to save.
- `dark`/`light` themes, `en`/`ru` languages, line numbers (`Alt+N`), word wrap (`Alt+Z`), indent guides, whitespace marks (`Alt+.`), ruler column, help screen (`F1`).
- Bookmarks (`F2` toggle, `Shift+F2` next, gutter `●`), indent folding (`Alt+-`), buffer-word completion (`Ctrl+Space`), document stats (`F4`).
- Optional session restore: reopen the previous tabs with cursor positions when started without arguments (off by default, toggle in Settings).
- File panel (`Ctrl+B`): fixed-width sidebar with the current folder, arrows to select, `Enter` to open, `Esc` back to text. Entries are color-coded: dirs, `..`, hidden and executables.
- Tabs: open tab bar (`Ctrl+T` new, `Ctrl+W` close, `Ctrl+PgDn/PgUp` switch, `Alt+1..9,0` jump, `Ctrl+P` list); long rows scroll with the active tab always visible; dirty tabs ask on close, quitting walks through them one by one.
- Split view (`Alt+S`): two or more panes side by side, each with its own tabs; `F6`/`Shift+F6` or `Ctrl+1..9` move focus (tab keys act on the focused pane).
- System clipboard via OSC52 (Windows Terminal): `^C` copies, paste with `Ctrl+V`.

## Hotkeys

```text
^S save (asks for name if new)   Ctrl+Shift+S save as   ^Q quit
File menu — Save all, File format (`F9`)
^F find       F3 next / Shift+F3 prev   ^H replace   ^G go to line
  (in Find: Alt+C match case, Alt+W whole words, Alt+R regex)
^K cut line  ^U/^V paste  ^C copy line  ^D duplicate
Ctrl+/ toggle line comment  Alt+] matching bracket
^C also puts the copy into the system clipboard (Windows Terminal) — paste with Ctrl+V
^Z undo  ^Y redo  ^A select all   Shift+arrows — selection
arrows/Home/End/PgUp/PgDn, Ctrl+arrows — by word, Alt+up/down — move line
Enter — new line, Tab — indent, Shift+Tab — unindent
F10 or Alt+F/E/H — menu (arrows/Enter/Esc, letter hotkey)
F1 — help   Alt+N — line numbers   Alt+Z — word wrap   Ctrl+B — file panel
F9 — file format (encoding / line endings)   F4 — document stats   F5 — command palette (setting search)
F2 — bookmark   Shift+F2 — next bookmark   Alt+- — fold   Ctrl+Space — complete
Ctrl+PgDn — next tab   Ctrl+PgUp — prev (Ctrl+Tab where the terminal passes it)
Ctrl+T — new tab   Ctrl+W — close tab
Alt+1..9,0 — jump to tab   Ctrl+P — tab list
```

Full list: `tui-edit --help` or `F1` in the editor.

## Screenshots

![Demo](docs/demo.gif)

| Editor | Find | Menu |
|---|---|---|
| ![Editor](docs/shots/editor.png) | ![Find](docs/shots/find.png) | ![Menu](docs/shots/menu.png) |

| Modal | Manager | Settings |
|---|---|---|
| ![Modal](docs/shots/unsaved.png) | ![Manager](docs/shots/picker.png) | ![Settings](docs/shots/settings.png) |

| Recovery | Panel |
|---|---|
| ![Recovery](docs/shots/restore.png) | ![Panel](docs/shots/sidebar.png) |

| Tabs | Split |
|---|---|
| ![Tabs](docs/shots/tabs.png) | ![Split](docs/shots/split.png) |

![Help](docs/shots/help.png)

## Build & run

```powershell
dotnet run                          # Debug run
dotnet test                         # xUnit tests (TuiEdit.Tests)
dotnet build -c Release              # build to bin/Release/net10.0/
tui-edit [file]                 # open a file (or an empty document)
tui-edit file:120               # open at line 120 (:$ goes to the end)
tui-edit --help | --version
```

> If the exe is locked by a running editor (`MSB3026`) — quit it (`^Q`) and rebuild.

## Config

`settings.json` next to the exe (portable mode), otherwise `%APPDATA%\TuiEdit\settings.json`:

```json
{
  "Theme": "dark",
  "Language": "en",
  "SearchMatchCase": true,
  "SearchWholeWord": false,
  "ShowLineNumbers": true,
  "ShowIndentGuides": true,
  "WordWrap": false,
  "RestoreSession": false
}
```

Editable from the settings dialog in the File menu, applied and saved immediately.

## Mouse

Off by default (`Mouse` in settings or settings.json): terminals
handle mouse reporting inconsistently, and a flood of motion events used
to starve keyboard input. Levels: `Off` (native terminal selection),
`Basic` (clicks + wheel), `Drag` (button-held motion), `Motion` (all motion).
When on: left-click positions the cursor, drag selects text (copies on
release when `CopyOnSelect` is set, right-click copies a live selection),
wheel scrolls, buttons/menus/dialogs/list rows highlight
on hover and activate on click, wheel scrolls scrollable lists.

## Keybindings

`keybindings.json` next to `settings.json` (a commented example is created
on first run): `"Command": "key"` overrides the default, `null` unbinds it.
Notation is `Ctrl`/`Alt`/`Shift` + key, case-insensitive:

```jsonc
{
  "Save": "Ctrl+Shift+S", // replaces Ctrl+S
  "SaveAll": "Ctrl+T", // commands without a default key become bindable
  "GoToLine": null // Ctrl+G will do nothing
}
```

Key names: letters, digits, `F1`–`F24`, `Up`/`Down`/`Left`/`Right`,
`Home`/`End`, `PageUp`/`PageDown`, `Space`, `Tab`, `Enter`, `Esc`,
punctuation (`.`, `/`, `-`, …) or raw `ConsoleKey` names (`OemPeriod`).
A printable key requires `Ctrl` or `Alt` (bare letters would break typing);
unknown commands and bad entries are ignored. On conflict the later entry
wins. Menu hints follow your bindings automatically.

## Backups

With `BackupOnSave` on, every save keeps a versioned copy of the previous
content in the `backups` folder next to `settings.json` — never next to your
files. Up to 5 recent copies per file are kept, copies older than 7 days are
pruned on startup. Saves are atomic (temp file + rename), so an interrupted
write never leaves a truncated file.

Unsaved work is auto-drafted every 30 seconds; on a crash the editor dumps
all modified tabs to drafts before exiting, and offers them back via
the Recovery dialog on next start. Set `TUIEDIT_DEBUG=1` for a full stack
trace on crash (otherwise only a one-line message is printed).

Files over 16 MB open only after confirmation — highlighting a giant file
can take seconds.

## Custom themes

Like `schemes` in Windows Terminal: add a `Themes` array — each scheme has
a `Name`, a `Base` (`dark`/`light`, defaults to `dark`) and `Colors`
(role → `#rrggbb`). Unset roles come from the base; a name matching
a built-in overrides it. Role names are the `Theme` record fields
(`EditorBg`, `EditorFg`, `CurLineBg`, `SelBg`, `MatchBg`, `StatusBg`,
`ModalBg`, `ButtonSelBg`, `PickerDirFg`, `AccentFg`, …). The file tolerates comments,
trailing commas and any key case:

```json
{
  "Theme": "3024 Night",
  "Themes": [
    {
      // only overrides — the rest comes from "dark"
      "Name": "3024 Night",
      "Base": "dark",
      "Colors": {
        "EditorBg": "#090300",
        "EditorFg": "#a5a2a2",
        "CurLineBg": "#4a4543",
        "SelBg": "#4a4543",
        "SelFg": "#a5a2a2",
        "MatchBg": "#cdab53",
        "MatchFg": "#090300",
        "StatusBg": "#a16a94",
        "StatusFg": "#090300",
        "ButtonSelBg": "#01a252",
        "ButtonSelFg": "#090300",
        "PickerDirFg": "#01a0e4",
        "PickerExeFg": "#01a252"
      }
    }
  ]
}
```

Custom names appear in the settings dialog theme row next to the built-ins:
`dark`, `light`, `3024 Night (dark)`, `Paper (light)`
(the bracket says whether the theme is dark or light).

## Syntax grammars

Highlighting rules are JSON plugins (VS Code TextMate-style, simplified).
On first run the built-in grammars are extracted to the `grammars` folder
next to `settings.json` (portable mode: next to the exe) — edit them in
place to customize; any `*.json` you drop there is picked up, matching by
`Name` or `Extensions` overrides a built-in. If the folder is missing or
unreadable, the embedded copies are used as a fallback.
`"Grammar": "auto"` picks by file extension; a language name forces it.

```json
{
  "Name": "C#",
  "Extensions": [".cs"],
  "LineComment": "//",
  "IgnoreCase": false,
  "Rules": [
    { "Scope": "comment", "Begin": "/\\*", "End": "\\*/" },
    { "Scope": "comment", "Match": "//.*$" },
    { "Scope": "string", "Match": "\"(?:\\\\.|[^\"\\\\])*\"?" },
    { "Scope": "keyword", "Match": "\\b(?:if|else|for|while|return)\\b" },
    { "Scope": "number", "Match": "\\b\\d[\\d_]*(?:\\.\\d+)?\\b" },
    { "Scope": "type", "Match": "\\b[A-Z][\\w]*" }
  ]
}
```

Rules run in order (earlier wins ties); otherwise the earliest match wins.
`Match` is single-line, `Begin`/`End` spans lines (block comments,
triple-quoted strings). Scopes: `keyword`, `string`, `comment`, `number`,
`type` — anything else is plain text. `LineComment` drives `Ctrl+/`
(toggle line comment); languages without one (JSON, XML, Markdown) report it.
Colors come from the theme roles
`SynKeywordFg`, `SynStringFg`, `SynCommentFg`, `SynNumberFg`, `SynTypeFg`,
so custom themes recolor syntax too. Bad patterns are skipped, matching
times out safely.

## Structure

```text
Program.cs            entry, --help/--version, editor startup
Core/TextBuffer.cs    buffer: lines, undo/redo, find/replace, encodings
Core/LineField.cs     single-line field: text, cursor, selection (prompts, picker)
Core/CliArgs.cs       CLI file:line parsing
Core/Completion.cs    buffer-word completion candidates
Core/Grep.cs          file search: recursive, hidden/binary skipped
Core/Folding.cs       indent folding ranges
Core/DocTab.cs        tab: buffer + view state (cursor, scroll, selection)
Core/Pane.cs          split pane: own tabs, active tab, tab scroll
Core/Grammar.cs       syntax grammars: seeded folder, JSON plugins + registry
Core/BracketMatcher.cs bracket pairs (skips strings/comments via highlighter)
Core/SyntaxHighlighter.cs tokenizer (match/begin-end) with cache
Core/WordMotion.cs    word-wise motion (VS Code style)
Core/TabStops.cs      tabs + WordWrap (soft-wrap segments)
Core/EditorCommand.cs editor commands
Input/KeyMap.cs       keys → commands
Input/InputReader.cs  input + bracketed paste
Ui/TuiEditor.cs       engine: render, menu, dialog outcomes
Ui/Dialog.cs          window base: frame, centering, loop (inheritance)
Ui/ModalDialog.cs     popups over ModalState
Ui/FileDialog.cs      manager over FilePickerState
Ui/SettingsDialog.cs  settings over SettingsDialogState
Ui/FormatDialog.cs    file format (encoding / line endings)
Ui/HelpDialog.cs      help: titled sections, scroll
Ui/Screen.cs          frame diff-buffer (no flicker)
Ui/FilePicker.cs      file manager (pure model)
Ui/Modal.cs           modal popups (pure model)
Ui/SettingsDialogState.cs settings dialog state (pure model)
Ui/SidebarState.cs    file panel model: listing, highlight, scroll (pure model)
Config/               AppSettings + SettingsStore (JSON)
Config/BackupStore.cs versioned backups next to settings.json (5 per file, 7 days)
Config/ThemeScheme.cs   custom themes: schemes, hex, catalog
Resources/            strings.ru/en.json (key parity required)
TuiEdit.Tests/        xUnit tests (dotnet test): buffer, find, dialogs, resources
```

Help (`F1`) is also a dialog (`HelpDialog` over `Dialog`):
«File / Find / Edit / Navigation / Menu / View / Manager» sections,
arrow-key scroll. The About window holds only the name, version,
release date, author and license — no key hints.

Frames for `docs/` are rendered by a separate tool next to the project:

```powershell
dotnet run --project ../TuiEdit.Shots -- docs/shots_new
```

