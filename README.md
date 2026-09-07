# TuiEdit

A simple TUI text editor in the style of Microsoft Edit / nano.
Pure `System.Console`, no third-party libraries. .NET 10.

## Features

- Editing: undo/redo (per keystroke and per block), `Shift+arrows` selection, cut/copy/paste line (`^K`, `^C`, `^U`/`^V`), duplicate (`^D`), move lines (`Alt+↑/↓`), word-wise delete/move.
- Find (`^F`, `F3`/`Shift+F3`): live highlight while typing, wrap-around, `k/N` counter, «match case» and «whole word» options. Instant whole-document replace (`^H`) in a single undo step.
- File manager for Open/Save as: drives, `..`, file highlight, overwrite confirmation in a separate window. Name field: selection (`Shift`), word-wise motion (`Ctrl+arrows`), `Alt+←/→` navigation.
- Format preservation: encoding (UTF-8/BOM/UTF-16), line endings (CRLF/LF/CR) and indent are detected on open and kept on save.
- `dark`/`light` themes, `en`/`ru` languages, line numbers (`Alt+N`), word wrap (`Alt+Z`), help screen (`F1`).
- File panel (`Ctrl+B`): fixed-width sidebar with the current folder, arrows to select, `Enter` to open, `Esc` back to text. Entries are color-coded: dirs, `..`, hidden and executables.
- Tabs: open tab bar (`Ctrl+T` new, `Ctrl+W` close, `Ctrl+PgDn/PgUp` switch, `Alt+1..9,0` jump, `Ctrl+P` list); long rows scroll with the active tab always visible; dirty tabs ask on close, quitting walks through them one by one.
- Split view (`Alt+S`): two or more panes side by side, each with its own tabs; `F6`/`Shift+F6` or `Ctrl+1..9` move focus (tab keys act on the focused pane).
- System clipboard via OSC52 (Windows Terminal): `^C` copies, paste with `Ctrl+V`.

## Hotkeys

```text
^S save (asks for name if new)   Ctrl+Shift+S save as   ^Q quit
^F find       F3 next / Shift+F3 prev   ^H replace   ^G go to line
^K cut line  ^U/^V paste  ^C copy line  ^D duplicate
^C also puts the copy into the system clipboard (Windows Terminal) — paste with Ctrl+V
^Z undo  ^Y redo  ^A select all   Shift+arrows — selection
arrows/Home/End/PgUp/PgDn, Ctrl+arrows — by word, Alt+up/down — move line
Enter — new line, Tab — indent, Shift+Tab — unindent
F10 or Alt+F/E/H — menu (arrows/Enter/Esc, letter hotkey)
F1 — help   Alt+N — line numbers   Alt+Z — word wrap   Ctrl+B — file panel
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
tui-edit [file]                     # open a file (or an empty document)
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
  "WordWrap": false
}
```

Editable from the settings dialog in the File menu, applied and saved immediately.

## Custom themes

Like `schemes` in Windows Terminal: add a `Themes` array — each scheme has
a `Name`, a `Base` (`dark`/`light`, defaults to `dark`) and `Colors`
(role → `#rrggbb`). Unset roles come from the base; a name matching
a built-in overrides it. Role names are the `Theme` record fields
(`EditorBg`, `EditorFg`, `CurLineBg`, `SelBg`, `MatchBg`, `StatusBg`,
`ModalBg`, `ButtonSelBg`, `PickerDirFg`, …). The file tolerates comments,
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

## Structure

```text
Program.cs            entry, --help/--version, editor startup
Core/TextBuffer.cs    buffer: lines, undo/redo, find/replace, encodings
Core/DocTab.cs        tab: buffer + view state (cursor, scroll, selection)
Core/Pane.cs          split pane: own tabs, active tab, tab scroll
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
Ui/HelpDialog.cs      help: titled sections, scroll
Ui/Screen.cs          frame diff-buffer (no flicker)
Ui/FilePicker.cs      file manager (pure model)
Ui/Modal.cs           modal popups (pure model)
Ui/SettingsDialogState.cs settings dialog state (pure model)
Ui/SidebarState.cs    file panel model: listing, highlight, scroll (pure model)
Config/               AppSettings + SettingsStore (JSON)
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

