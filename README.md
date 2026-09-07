# TuiEdit

A simple TUI text editor in the style of Microsoft Edit / nano.
Pure `System.Console`, no third-party libraries. .NET 10.

## Features

- Editing: undo/redo (per keystroke and per block), `Shift+arrows` selection, cut/copy/paste line (`^K`, `^C`, `^U`/`^V`), duplicate (`^D`), move lines (`Alt+↑/↓`), word-wise delete/move.
- Find (`^F`, `F3`/`Shift+F3`): live highlight while typing, wrap-around, `k/N` counter, «match case» and «whole word» options. Instant whole-document replace (`^H`) in a single undo step.
- File manager for Open/Save as: drives, `..`, file highlight, overwrite confirmation in a separate window. Name field: selection (`Shift`), word-wise motion (`Ctrl+arrows`), `Alt+←/→` navigation.
- Format preservation: encoding (UTF-8/BOM/UTF-16), line endings (CRLF/LF/CR) and indent are detected on open and kept on save.
- `dark`/`light` themes, `en`/`ru` languages, line numbers (`Alt+N`), word wrap (`Alt+Z`), help screen (`F1`).
- File panel (`Ctrl+B`): fixed-width sidebar with the current folder, arrows to select, `Enter` to open, `Esc` back to text.
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

## Structure

```text
Program.cs            entry, --help/--version, editor startup
Core/TextBuffer.cs    buffer: lines, undo/redo, find/replace, encodings
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
