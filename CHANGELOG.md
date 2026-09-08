# Changelog

Rule: every `feat`/`fix` commit extends `Unreleased`; the release commit
renames the section to the version number. `release.yml` builds the GitHub
release notes from the `Unreleased` section when a `v*` tag is pushed.

## [Unreleased]

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

Simple TUI text editor in the style of MS Edit / nano.
Pure `System.Console`, no third-party libraries, .NET 10.

- Tabs, split view, file panel, file manager (mkdir/delete, hidden files, sizes).
- Syntax highlighting from JSON grammars
  (C#, Python, JS/TS, JSON, Markdown, PowerShell, XML, INI).
- Find/replace with live highlight, regex, grep across files.
- Bookmarks, indent folding, bracket pairs, toggle comment, buffer completion.
- Themes (dark/light/custom), en/ru, session restore, versioned backups.
