# Changelog

Rule: every `feat`/`fix` commit extends `Unreleased`; the release commit
renames the section to the version number. `release.yml` builds the GitHub
release notes from that version's section (falling back to `Unreleased`)
when a `v*` tag is pushed.

## [Unreleased]

### Added
- Filter through shell (`Ctrl+R`, Edit menu): runs the selection rows (or the
  current row) through any command (`sort`, `jq`, ...), replacing them with the
  output in a single undo entry. Timeouts, missing binaries, and nonzero exits
  keep the text and show a message.
- Jumplist (`Alt+Left`/`Alt+Right`): back/forward across grep hits, goto jumps
  and picked files (30 entries, consecutive duplicates drop, new jumps truncate
  the forward branch).
- Custom status bar formats (`StatusFormatLeft`/`StatusFormatRight` in settings):
  `$(...)` verbs for every block (`pos`, `sel`, `file`, `encoding`, `git`,
  `tab`...), `$(opt:Name)` for any option, `$(bind:Command)` for its hotkey.
  Defaults render exactly the old bar.
- Time-undo (`F12`: `earlier`/`later` with steps or ages like `5m`, `30s`):
  undo snapshots carry timestamps, so recent edits roll back by age and
  redos walk forward again; plain counts still work (`earlier 3`).
- Quick-open `ft:` filter (`Alt+O`): `ft:cs,txt` narrows by extension while
  the rest still matches names as a substring.
- Prompt history and Tab-completion: `Up`/`Down` recall per-prompt history
  (find, replace, goto, command line, ...); `Tab` completes `F12` verbs and
  `set` keys or buffer words in find prompts.
- Surround (`Ctrl+L` add, `Alt+L` change, `Alt+J` delete, Edit menu): wraps
  the selection (or the word under the cursor) in a prompted pair, swaps or
  removes it — each in a single undo entry.
- Light registers (`Ctrl+X` then `a-z`/`0-9`, palette): every yank fills the
  unnamed register plus `0` (last yank); an explicit register pins one yank
  or paste, one-shot.
- Startup dashboard: plain launches show the version, recent files
  (`1-9`/`Enter` to open) and hints; any printable key types straight
  through into the fresh buffer.
- Posting-style tabs: no separators, the active tab is an inverted block and
  inactive tabs stay dim; dividers touching the focused pane use the accent
  color. Modified, read-only and git-dirty states render as inverted pills
  (`[*]`, `[read-only]`) in the status bar.
- Posting (dark) theme: posting's galaxy palette (purple primary, pink
  accent, mint/gold syntax) as a fifth built-in, cyclable in settings.

### Fixed
- Cursor motion stays flat deep in files: `EnsureVisible` walked from row 0
  every frame (233 us and 922 KB at row 5500), now it walks relatively from
  `_top` (~44 us, ~21 KB at any depth); empty fold sets skip enumeration.
- Scroll repaints cost one console write: `FlushAnsi` batches the whole diff
  (inline CUP addressing) instead of ~600 `SetCursorPosition`/`Write` calls
  per full-viewport frame.
- Quiet input paints 1:1: wakeups with nothing else pending skip the 40 ms
  gate (single keypresses track key repeat like microsoft/edit); floods still
  throttle to ~25fps, so input always drains first.
- Clipboard export falls back past OSC 52: terminal detection covers common
  emulators, then platform tools (`clip`, `pbcopy`, `wl-copy`/`xclip`/`xsel`),
  then internal-only; oversized texts skip OSC 52 for the tools directly.
- Files that fail strict UTF-8 validation open as Latin1 (byte-preserving)
  instead of mojibake, round-trip on save, and `set encoding latin1` works.
- Frames flip atomically via synchronized output (DECSET 2026): full repaints
  no longer tear mid-frame on supporting terminals (Windows Terminal does);
  others ignore the markers. Gated on VT like other DEC sequences, with the
  end marker in a `finally`.
- Mouse-touching test classes share an xUnit collection: the static
  `InputReader.MouseLevel` flipped under parallel classes is gone as a flake source.

## [0.9.3] - 2026-09-11

### Fixed
- Draft tests are hermetic: they used the real shared drafts dir, so a live
  app session (or a killed run) polluted counts and flaked the suite ~1/5.
  Editors now accept an injected store (`SetDraftStore`) and tests use temp
  dirs — proven immune with stray drafts planted.
- Wire-speed runs paste as one undo: printable bursts with sub-20ms gaps
  (terminal paste without bracketed markers) skip AutoPair from the 4th char
  and merge undo entries, with instant echo preserved (nothing is held).
- Double-click selects the word under the cursor (500ms same-cell window).
- Transient console read failures retry with backoff instead of killing the
  session: a dying sibling process on the same console can fail a single read
  (dotnet#88697), and a resize can surface ERROR_PIPE_NOT_CONNECTED. Permanent
  failures still exit, but now with a crash-log entry instead of silence.
- DEC reporting sequences are gated on VT support: without it `?1000`/`?2004`/
  `?1004` enables (and disables) print as garbage instead of working.
- Dead-key presses (Oem + null char, no Ctrl) are dropped in the parser, so
  combining keys never reach bindings, fields, or the buffer on any layout.
- AltGr (Alt+Ctrl with a printable char) inserts the char instead of dying as
  unbound: `€`/`@` on European layouts now type instead of doing nothing.
- Console codepages are forced to UTF-8 while running (restored on exit):
  conhost translates keyboard records through the input codepage, and a
  non-UTF8 one mangles non-ASCII key releases into garbage storms.
- Single mouse source on Windows Terminal: with `WT_SESSION` set, SGR reports
  cover clicks/wheel/motion, so the conhost mouse bit stays off — every click
  no longer arrives twice and motion no longer floods the queue. Legacy
  conhost keeps conhost records (its only source). The active source is
  logged as `mouse-source=` when diagnostics are on.

## [0.9.2] - 2026-09-11

### Fixed
- Control characters (ESC and friends) are shown as `�` instead of going raw
  to the terminal, where a single ESC made it swallow all following output
  (dead screen, blinking cursor, no errors — looked like a total freeze).
- Held `Ctrl+V` no longer outruns the frame: pending paste repeats merge into
  a single insertion (one undo step), so nothing keeps pasting after release.
- Opening files (or a directory) no longer pops the restore-drafts picker:
  stale drafts stay saved and are still offered on plain launches.
- Held keys no longer stack frames: each wakeup drains all pending input
  before the next render (one frame per burst instead of one per event).
- Killing the window (X button, taskkill) restores terminal modes via
  a `ProcessExit` hook instead of leaving paste/mouse reporting stuck on.
- `TUIEDIT_INPUT_LOG=<path>` captures raw input diagnostics for
  "keys do nothing" reports (announced on stderr while active).
- Mouse clicks fire on release, not on press: a press arms the dialog button
  or menu item, releasing elsewhere cancels (misclicks are recoverable).
- Quitting no longer leaks queued input: pending typeahead and held-key
  repeats are discarded during teardown instead of spilling into the shell.
- Render is throttled to ~25fps while input always drains: any flood costs
  mutations, never a frame backlog. Slow frames and dispatched commands are
  traced to `TUIEDIT_INPUT_LOG` when diagnostics are on.
- Input log lines carry the process id, plus session start/stop markers:
  overlapping instances sharing one log file are told apart, clean quits proven.
- Input log marks every loop iteration and every painted frame, so a hang's
  tail shows exactly where the main loop stalled (read, handler, or render).
- Bracketed paste has an absolute 30s bound: a terminator lost on the wire
  used to hang the app forever (~2% CPU, zero errors, hammered keys fed the
  trap because every char restarted the silence timeout). Timeouts are logged.
- Input log covers conhost mouse events too (they bypass the parser, so a
  motion flood used to look like ghost loop iterations with no events).
- Menu separators are a mouse dead zone: hovering one no longer jumps the
  highlight to the first item, pressing or releasing on one keeps the menu
  open instead of closing or firing.
- Regex search highlight is frame-budgeted: a catastrophic pattern used to
  cost up to 500ms per visible row every frame (8s+ per keystroke, zero
  errors logged); rows now share a 100ms budget with a 25ms per-row cap.
- An unconsumable console record no longer hangs input forever: a zero-type
  slot Peek reports but Read cannot take used to spin both read loops with
  zero errors logged (caught live via dotnet-stack + dotnet-dump); after
  1000 failed takes the queue is flushed once and the recovery is logged
  as `stuck-flush`.
- The mouse drain loop no longer skips silently forever: stale motion and
  junk used to `continue` without bound, so an endless supply (sensor
  jitter, synthetic storm) never let the main loop cycle — no render, no
  keys, zero logs. After 4096 silent skips Read falls through to the
  key-wait path (which eats the flood waiting for the next key); trips are
  logged as `drain-flood`. The sibling `IsKeyPending` loop has the same
  bound now. Flood lines carry per-branch counters and the queue-head type
  (`motion=/junk=/takenull=/head=`), so the next live report identifies
  the spinning record instead of guessing.
- Junk input drains in bulk: every Cyrillic press leaves a storm of key-up
  residue in the conhost queue (live log: `junk=4096`, heads like `1/0/U+00B0`),
  and one-by-one takes cost ~300ms per keystroke, starving keys behind the
  storm. Leading junk runs now drop up to 128 records per syscall (peek,
  stop before the first key-down/mouse, dequeue exactly the junk prefix —
  order-safe, phantom-guarded), so keys surface in milliseconds.
- Phantom console records are detected, not just failures: `ReadConsoleInput`
  can report success without dequeuing a zero-type slot (proven by two dumps
  20s apart with the fail counter stuck at 0 while spinning).   `Take` now
  re-peeks after every successful read and counts an unmoved head as a
  failure, so the flush breaker actually fires; record comparison covers
  the full 16-byte union. The fail counter resets only on verified
  consumption — resetting on raw success capped phantom streaks at 1, so
  the breaker never fired (86 silent `drain-flood` cycles with zero
  `stuck-flush` in one live session).

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
