# Changelog

Rule: every `feat`/`fix` commit extends `Unreleased`; the release commit
renames the section to the version number. `release.yml` builds the GitHub
release notes from that version's section (falling back to `Unreleased`)
when a `v*` tag is pushed.

## [Unreleased]

### Fixed
- Control characters (ESC and friends) are shown as `�` instead of going raw
  to the terminal, where a single ESC made it swallow all following output
  (dead screen, blinking cursor, no errors — looked like a total freeze).

## [0.9.1] - 2026-09-10

### Fixed
- `Ctrl+/` on the Russian layout (`Ctrl+Oem2` maps to `ToggleComment`).
- Stale selection anchor no longer widens the commented line range to zero.
- Sidebar spans the full panel height; the tab row starts right of it.

### Added
- Instant preview tab while navigating the sidebar; `Enter` pins it
  (large files are not previewed).

## [0.9.0] - 2026-09-10

### Fixed
- Drafts cover all dirty tabs (untitled tabs get per-tab keys, no collisions);
  writes are atomic (temp file plus rename).
- Recovery restores everything in one run ("Restore all" plus per-draft loop
  into new tabs); CLI-opened buffers are never overwritten.
- Corrupt settings are preserved as `.corrupt` with an in-editor notice
  instead of a silent reset; null-safe normalization.
- Unreadable first CLI file shows an error instead of exiting.
- Broken keybinding lines are skipped per-entry instead of dropping the batch.
- Guarded Run setup/teardown; format-argument resource test.

### Added
- Untitled tabs round-trip through the session (256KB cap).
- `msg.settings.corrupt`, `error.openfile` messages (en/ru).

## [0.8.0] - 2026-09-10

### Added
- Replace confirm shows a before/after preview of the first match.
- Manager rename (`F2`) via the shared file operations.
- Command-line buffer keys: `set encoding/ending/indent`.
- Filter-match highlight in the command palette.
- Whole-frame golden tests (`RenderFrame` works headless).

### Changed
- Startup parsing extracted into testable `Core/Startup`.
- Coverage gate raised to 68% (actual 71%).
- CI actions refreshed via Dependabot (checkout v7, cache/setup-dotnet v6,
  upload/download-artifact v7/v8).

## [0.7.0] - 2026-09-10

### Added
- File tree sidebar: lazy expand/collapse (`Enter`, arrows), git change marks
  (folders aggregate descendants), `F7` create, `F2` rename (`PromptDialog`),
  `Del` armed delete with item count.
- `InputParser`: pure testable stdin machine with a 5000-case fuzz.
- Crash reports (`%TEMP%/TuiEdit/crashes/`) with mouse/event session fallbacks.
- Golden frame tests (`Screen.Snapshot`, `UPDATE_GOLDENS=1`).
- Keybindings live reload (no restart).
- CI: Dependabot, BenchmarkDotNet baseline (`docs/perf-baseline.md`).

### Fixed
- Git background refresh no longer spawn-storms (single in-flight query;
  fixes growing input lag with the sidebar open) and no longer races
  `Dispose` against token reads.
- Quick-open index is a cycle-safe BFS (hidden dirs pruned, denied dirs skipped).
- Tab row no longer crashes on ultra-narrow windows with the panel open.

## [0.6.0] - 2026-09-10

### Added
- Crash reports (`%TEMP%/TuiEdit/crashes/crash-*.log`, capped at 20):
  the fatal message points at the exact file.
- Keybindings live reload: editing `keybindings.json` applies without restart.
- Golden frame tests for dialogs (`Screen.Snapshot`, `UPDATE_GOLDENS=1`).

### Changed
- Stdin machine extracted into a pure testable `InputParser`
  (5000-case fuzz: bursts, SGR mutations, paste).
- Quick-open index is a hand-rolled BFS: hidden dirs pruned, denied dirs
  skipped without aborting, symlink cycles bounded, caps top-down.
- CI: Dependabot, CodeQL, BenchmarkDotNet baseline (`docs/perf-baseline.md`).

### Fixed
- A throwing mouse path parks the mouse for the session instead of killing
  the editor; any failing input event is logged and skipped.

## [0.5.0] - 2026-09-09

### Changed
- Code-quality pass: command dispatch is table-driven (`ICommandDispatcher`
  with exhaustive coverage test); git, clipboard, keybindings and highlight
  are explicit services composed in `Program` (no DI container).
- `TuiEditor` god-object split: combined mark shifts, shared cell/path helpers.
- All code comments in English; assembly API surface is `internal`
  (single-file app, no public consumers).
- Git status/diff refresh is async with supersede-cancellation; syntax
  highlight prefetches in the background with version-checked swap;
  bracketed-paste wait no longer spin-sleeps on Windows.
- Hot paths allocation-free: `Append(char)`, shared no-wrap segment list,
  counter-only wrap segments.
- Tests: `TempDir` fixture, `Integration` traits (fast unit-only leg:
  221 tests in <1s), data-driven cases, async cancellation tests.
- CI: BenchmarkDotNet project (`TuiEdit.Bench`, 8 core benchmarks),
  coverage collection with a 60% line-rate gate (baseline 62.5%).

### Added
- Natural file sorting (`file2` before `file10`) in manager and panel.
- CLI takes several files (`tui-edit a b:10:2`) and a start directory.
- `Go to line` accepts `line:col` (and `$`).
- Quick-open project files (`Alt+O`) with name filter.
- Auto-pairs for brackets/quotes (toggle in Settings).
- Git diff gutter (added/modified line marks, toggle in Settings).
- Command line (`F12`): `set`, `goto`, `find`, `save`, `quit`.

### Fixed
- git subprocess could hang forever without an attached console when
  outside a repo: `.git` pre-check plus a fully bounded spawn with
  kill-on-timeout (shared `GitProcess` runner).

## [0.4.0] - 2026-09-09

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
- Dialog look: dimmed backdrop (truecolor only), top-weighted
  placement, `esc` hint in the title row. All MS Edit/nano
  references removed; standalone wording throughout.
- Command palette (`F5`): live search across menu items, commands
  (copy, encoding/indents via File format, tabs, panes, view toggles)
  and settings with shortcut hints; `Enter` runs the command or steps
  the setting in place. Settings rows now share one `SettingsModel`
  with the settings dialog.
- Fixed palette scrolling: the window no longer jumps and glues the
  highlight to the bottom edge — selection moves first, the list
  scrolls only at the edge.

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
