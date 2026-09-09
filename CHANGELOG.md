# Changelog

Rule: every `feat`/`fix` commit extends `Unreleased`; the release commit
renames the section to the version number. `release.yml` builds the GitHub
release notes from that version's section (falling back to `Unreleased`)
when a `v*` tag is pushed.

## [Unreleased]

### Added
- Mouse input keeps button and modifier data (SGR bits and conhost
  `ControlKeyState`); middle/right presses are still ignored downstream.
- Coalesced wheel ticks: one `MouseInput` carries a `Count`, so a fast
  spin scrolls proportionally instead of queuing one render per tick.

### Fixed
- Mixed stdin chunks no longer eat input: the reader stops at the first
  complete mouse/paste sequence, defers the rest, and drops nothing
  silently (trailing keypresses/paste survive a click).
- ANSI motion events are coalesced to the latest position (same
  backpressure the Windows path already had).

### Added
- Mouse levels instead of a single toggle: Off (native terminal
  selection), Basic (clicks+wheel), Drag (+button-held motion),
  Motion (all motion). The old `EnableMouse` flag migrates to Basic.
- Focus tracking with DEC-mode restore: on focus-in the active mouse,
  focus and bracketed-paste modes are re-emitted (Windows Terminal /
  ConPTY silently reset them).
- Single `EditorLayout` source for Render and mouse hit-testing
  (no more mirrored geometry).
- Click focuses panes and switches tabs; wheel outside modal/menu
  scrolls the active pane (gutter/sidebar/statusbar included);
  backdrop click cancels a modal like Esc.
- Mouse text selection: press-drag-release in the viewport
  (Drag/Motion levels); release copies to clipboard when
  `CopyOnSelect` is set, otherwise the selection stays for keyboard
  ops. Right-click copies a live selection; activation stays on
  press, so a drag-release can never fire a control.

## [0.3.0] - 2026-09-08

### Added
- Crash handler: on an unhandled exception the terminal is restored,
  all modified tabs are dumped to drafts, and a one-line message is printed
  (exit code 1). Full stack trace only with `TUIEDIT_DEBUG=1`.
- Confirmation dialog when opening files over 16 MB (highlighting
  a giant file can take seconds). Safe default is "No".
- Linux job in CI (build + test on `ubuntu-latest` alongside Windows).
- Git branch and dirty flag in the status bar (`⎇ main*`; untracked files
  don't count, outside a repo the segment is hidden).
- `TuiEditor` split into partials by area (Cursor/Search/Files/Render/Input/
  Modal) plus a pure `StatusBar.BuildRight`; no behavior change.
- Customizable keybindings (`keybindings.json` next to settings):
  per-command override, `null` unbinds, menu hints follow automatically.
- Mouse v1 (SGR 1006): left-click positions the cursor in the active pane,
  wheel moves it ±3 lines; ignored over dialogs and menus.
- Mouse on Windows via console input API (conhost/Windows Terminal):
  `ReadKey` never delivers mouse events, so the queue is polled directly
  (Unix keeps the SGR byte path).
- Mouse clicks on modal buttons and the top menu (bar, dropdown);
  clicks elsewhere dismiss the menu, modals swallow outside clicks.
- Mouse hover highlight on modal buttons, list rows and dropdown items
  (keyboard selection takes over on any keypress); wheel scrolls
  scrollable modal lists.
- Fixed modal staying open after mouse click (closed state wasn't
  propagated like in the keyboard path) and the top bar not opening
  the menu on click.
- Fixed input starvation with mouse on Windows (motion-event flood
  starved keys; motion is now one event per read).
- Mouse clicks in option dialogs (settings, file format: select + step)
  and on find/replace toggles; release updates hover position (terminals
  often don't report bare hover motion).
- Fixed UI freeze under mouse motion on Windows (motion-event flood
  starved keys behind full renders; stale motions are now eaten without
  rendering, only the freshest is returned); dropped ?1002 (drag stream
  with no consumer).
- Mouse is now opt-in and off by default (`EnableMouse`, 10th settings
  row): no terminal mode changes, no mouse events unless enabled.
- Fixed periodic UI freezes (git status ran synchronously in every
  render on TTL expiry: ~100ms on a small repo, seconds on a big one).
  Git now refreshes on a background thread (single `status -sb` spawn),
  the status bar reads the last known value.
- Fixed input starvation with mouse on Windows: key readiness is probed
  via the queue (`PeekConsoleInput`), bare `KeyAvailable` also fires on
  mouse records and `ReadKey` would block/swallow keystrokes over them.

### Fixed
- Atomic save: content is written to a temp file in the same folder,
  then renamed over the target — an interrupted write no longer leaves
  a truncated file.

### Changed
- Strict build: `TreatWarningsAsErrors` + `AnalysisMode=Recommended` +
  `EnforceCodeStyleInBuild` in both projects, new `.editorconfig`.
- Faster highlight recompute after edits: early exit once the multiline
  state reconverges (edit at row 0 of a 10k-line C# file went from
  264 ms to 0.5 ms per keystroke); grammar regexes are now compiled
  (cold highlight 177 ms → ~110 ms, +~4 ms one-time load).

## [0.2.0] - 2026-09-08

Simple keyboard-first TUI text editor.
Pure `System.Console`, no third-party libraries, .NET 10.

- Tabs, split view, file panel, file manager (mkdir/delete, hidden files, sizes).
- Syntax highlighting from JSON grammars
  (C#, Python, JS/TS, JSON, Markdown, PowerShell, XML, INI).
- Find/replace with live highlight, regex, grep across files.
- Bookmarks, indent folding, bracket pairs, toggle comment, buffer completion.
- Themes (dark/light/custom), en/ru, session restore, versioned backups.
