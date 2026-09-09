namespace TuiEdit;

/// <summary>TuiEditor: tabs, panes, session, save/open and file flows.</summary>
internal sealed partial class TuiEditor
{
    /// <summary>Opens a new empty tab (never duplicates a clean untitled one).</summary>
    internal void NewTab()
    {
        if (_buf.FilePath is null && !_buf.IsModified)
            return;
        SaveTabState();
        _docs.Add(new DocTab(new TextBuffer(null)));
        _active = _docs.Count - 1;
        LoadTabState();
    }

    /// <summary>Restores tabs from the previous session (skips missing files).</summary>
    internal int RestoreSessionTabs()
    {
        if (!_settings.RestoreSession || _settings.SessionTabs.Count == 0)
            return 0;
        int n = 0;
        foreach (SessionTab t in _settings.SessionTabs)
        {
            if (string.IsNullOrWhiteSpace(t.Path) || !File.Exists(t.Path))
                continue;
            try
            {
                if (n == 0 && _docs.Count == 1 && _buf.FilePath is null && !_buf.IsModified)
                    _buf.Open(t.Path);
                else
                {
                    SaveTabState();
                    _docs.Add(new DocTab(new TextBuffer(t.Path)));
                    _active = _docs.Count - 1;
                    LoadTabState();
                }
                _row = Math.Clamp(t.Row, 0, _buf.Count - 1);
                _col = Math.Clamp(t.Col, 0, _buf.GetLine(_row).Length);
                TrackCol();
                SaveTabState();
                TouchRecent(t.Path);
                n++;
            }
            catch
            {
            }
        }
        return n;
    }

    /// <summary>Remembers open files with cursors for the next start.</summary>
    internal void SaveSessionTabs()
    {
        if (!_settings.RestoreSession)
            return;
        SaveTabState();
        var tabs = new List<SessionTab>();
        foreach (Pane p in _panes)
        {
            foreach (DocTab t in p.Docs)
            {
                if (t.Buf.FilePath is null || !File.Exists(t.Buf.FilePath))
                    continue;
                int row = Math.Clamp(t.Row, 0, t.Buf.Count - 1);
                tabs.Add(new SessionTab(t.Buf.FilePath, row, Math.Max(0, t.Col)));
                if (tabs.Count >= AppSettings.MaxSessionTabs)
                    break;
            }
            if (tabs.Count >= AppSettings.MaxSessionTabs)
                break;
        }
        _settings.SessionTabs = tabs;
        _store.Save(_settings);
    }

    /// <summary>Jumps to a 1-based line and column (0 leaves that axis alone).</summary>
    internal void GoToPosition(int line, int col)
    {
        if (_buf.Count == 0)
            return;
        if (line > 0)
            _row = Math.Clamp(line - 1, 0, _buf.Count - 1);
        _col = col > 0
            ? Math.Clamp(col - 1, 0, _buf.GetLine(_row).Length)
            : Math.Min(_col, _buf.GetLine(_row).Length);
        UnfoldPath();
        TrackCol();
    }

    /// <summary>Opens a startup file in a new tab (CLI list).</summary>
    internal void OpenStartupFile(string path, int line, int col)
    {
        try
        {
            SaveTabState();
            _docs.Add(new DocTab(new TextBuffer(path)));
            _active = _docs.Count - 1;
            LoadTabState();
            if (line > 0)
                GoToPosition(line, col);
            TouchRecent(path);
            SaveTabState();
        }
        catch
        {
        }
    }

    /// <summary>Opens the file panel with a root (starts with a folder).</summary>
    internal void OpenSidebarRoot(string dir)
    {
        try
        {
            _sidebar = new SidebarState(Path.GetFullPath(dir));
            _sidebarFocus = true;
        }
        catch
        {
        }
    }

    /// <summary>Switches tabs (wraps around).</summary>
    internal void SwitchTab(int index)
    {
        if (_docs.Count == 0)
            return;
        _mouseDrag = false; // Focus moved - stops the drag
        SaveTabState();
        _active = ((index % _docs.Count) + _docs.Count) % _docs.Count;
        LoadTabState();
    }

    /// <summary>Closes the active tab (a dirty tab goes through a dialog).</summary>
    internal void CloseTab()
    {
        if (_buf.IsModified)
        {
            _pending = PendingOp.CloseTab;
            _dialog = new ModalDialog(ModalState.UnsavedQuit(_loc, DirtyLabel()), ApplyModalOutcome);
            return;
        }
        CloseTabNow();
    }

    /// <summary>Gets the dirty buffer name for the prompt.</summary>
    private string DirtyLabel() =>
        _buf.FilePath is null ? _loc["status.untitled"] : Path.GetFileName(_buf.FilePath);

    /// <summary>Closes the active tab unconditionally (the last tab in a pane clears the pane).</summary>
    internal void CloseTabNow()
    {
        if (_docs.Count <= 1)
        {
            if (_panes.Count > 1)
            {
                _panes.RemoveAt(_pane);
                _pane = Math.Min(_pane, _panes.Count - 1);
                LoadTabState();
                return;
            }
            ClearDoc();
            return;
        }
        _docs.RemoveAt(_active);
        _active = Math.Min(_active, _docs.Count - 1);
        LoadTabState();
    }

    private void ListTabs()
    {
        _dialog = new ModalDialog(
            ModalState.Tabs(_loc, _docs.Select(d => d.TabTitle(_loc)).ToList()),
            ApplyModalOutcome);
    }

    private bool AnyModified() => _panes.Any(p => p.Docs.Any(d => d.Buf.IsModified));

    /// <summary>Activates the first dirty tab (for the quit loop).</summary>
    private void ActivateFirstModified()
    {
        for (int pi = 0; pi < _panes.Count; pi++)
            for (int i = 0; i < _panes[pi].Docs.Count; i++)
                if (_panes[pi].Docs[i].Buf.IsModified)
                {
                    SaveTabState();
                    _pane = pi;
                    _active = i;
                    LoadTabState();
                    return;
                }
    }

    /// <summary>Splits the view: adds a new pane on the right and focuses it.</summary>
    internal void SplitPane()
    {
        SaveTabState();
        _panes.Add(new Pane(new DocTab(new TextBuffer(null))));
        _pane = _panes.Count - 1;
        LoadTabState();
    }

    /// <summary>Focuses a pane (wraps around).</summary>
    internal void SwitchPane(int index)
    {
        if (_panes.Count == 0)
            return;
        _mouseDrag = false; // Focus moved - stops the drag
        SaveTabState();
        _pane = ((index % _panes.Count) + _panes.Count) % _panes.Count;
        LoadTabState();
    }

    /// <summary>Computes pane widths: splits evenly, gives the remainder to the left.</summary>
    internal static int[] PaneWidths(int total, int count)
    {
        if (count <= 0)
            return [];
        int[] r = new int[count];
        int each = total / count, rest = total % count;
        for (int i = 0; i < count; i++)
            r[i] = each + (i < rest ? 1 : 0);
        return r;
    }

    /// <summary>Marks a file as recent and saves settings.</summary>
    private void TouchRecent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        _settings.TouchRecent(path);
        _store.Save(_settings);
    }

    /// <summary>Saves all named dirty tabs across panes.</summary>
    internal void SaveAll()
    {
        SaveTabState();
        int savedPane = _pane, savedActive = _active;
        int saved = 0, skipped = 0;
        string? error = null;
        try
        {
            for (int i = 0; i < _panes.Count; i++)
            {
                _pane = i;
                for (int d = 0; d < _panes[i].Docs.Count; d++)
                {
                    _active = d;
                    LoadTabState();
                    if (!_buf.IsModified)
                        continue;
                    if (_buf.FilePath is null)
                    {
                        skipped++;
                        continue;
                    }
                    try
                    {
                        _buf.Save(backup: Backups);
                        TouchRecent(_buf.FilePath);
                        _drafts.Delete(_buf.FilePath);
                        saved++;
                    }
                    catch (Exception ex)
                    {
                        error = $"{_loc["error.save"]}: {DisplayError(ex)}";
                    }
                }
            }
        }
        finally
        {
            _pane = savedPane;
            _active = savedActive;
            LoadTabState();
        }
        SetMessage(error ?? _loc.Format("msg.savedall", saved, skipped));
    }

    private void Save()
    {
        switch (_buf.FilePath)
        {
            case null:
                SaveAs();
                return;
            default:
                try
                {
                    _buf.Save(backup: Backups);
                    TouchRecent(_buf.FilePath);
                    _drafts.Delete(_buf.FilePath);
                    SetMessage(_loc.Format("msg.saved", _buf.FilePath));
                }
                catch (Exception ex) { SetMessage($"{_loc["error.save"]}: {DisplayError(ex)}"); }
                return;
        }
    }

    private void SaveAs()
    {
        string? path = PickSavePath(
            _buf.FilePath is null ? _loc["status.untitled"] : Path.GetFileName(_buf.FilePath));
        if (path is null) { SetMessage(_loc["msg.cancelled"]); return; }
        if (File.Exists(path))
        {
            _overwritePath = path;
            _dialog = new ModalDialog(ModalState.Overwrite(_loc, Path.GetFileName(path)), ApplyModalOutcome);
            return;
        }
        try
        {
            _buf.Save(path, Backups);
            TouchRecent(_buf.FilePath);
            _drafts.Delete(_buf.FilePath);
            SetMessage(_loc.Format("msg.saved", _buf.FilePath));
        }
        catch (Exception ex) { SetMessage($"{_loc["error.save"]}: {DisplayError(ex)}"); }
    }

    private void TryQuit()
    {
        if (!AnyModified())
        {
            _quitRequested = true;
            return;
        }
        // Red Save / Don't save / Cancel popup.
        _pending = PendingOp.Quit;
        _dialog = new ModalDialog(ModalState.UnsavedQuit(_loc, DirtyLabel()), ApplyModalOutcome);
    }

    private void DoOpen()
    {
        string? path = RunPicker(PickerMode.Open, StartDir(), string.Empty);
        if (path is null)
        {
            SetMessage(_loc["msg.cancelled"]);
            return;
        }
        OpenPicked(path);
    }

    /// <summary>Opens the selected path: immediately or via the unsaved-changes dialog.</summary>
    private void OpenPicked(string path)
    {
        if (!_buf.IsModified)
        {
            LoadFile(path);
            return;
        }
        _pending = PendingOp.Open;
        _pendingPath = path;
        _dialog = new ModalDialog(ModalState.UnsavedQuit(_loc, DirtyLabel()), ApplyModalOutcome);
    }

    /// <summary>Toggles the file panel: opens/closes/refocuses (roots at the file folder).</summary>
    private void ToggleSidebar()
    {
        if (_sidebar is null)
        {
            string root = _buf.FilePath is null
                ? Directory.GetCurrentDirectory()
                : Path.GetDirectoryName(Path.GetFullPath(_buf.FilePath)) ?? Directory.GetCurrentDirectory();
            _sidebar = new SidebarState(root);
            _sidebarFocus = true;
            return;
        }
        if (_sidebarFocus)
        {
            _sidebar = null;
            _sidebarFocus = false;
            return;
        }
        _sidebarFocus = true;
    }

    /// <summary>Handles a key while the panel has focus.</summary>
    private void HandleSidebarKey(ConsoleKeyInfo k)
    {
        if (_sidebar is null)
        {
            _sidebarFocus = false;
            return;
        }
        bool ctrl = (k.Modifiers & ConsoleModifiers.Control) != 0;
        if (ctrl && k.Key == ConsoleKey.B) { ToggleSidebar(); return; }
        if (ctrl || (k.Modifiers & ConsoleModifiers.Alt) != 0)
            return;
        switch (k.Key)
        {
            case ConsoleKey.Escape: _sidebarFocus = false; return;
            case ConsoleKey.UpArrow: _sidebar.MoveHighlight(-1, TextHeight()); return;
            case ConsoleKey.DownArrow: _sidebar.MoveHighlight(1, TextHeight()); return;
            case ConsoleKey.Home: _sidebar.MoveHighlight(int.MinValue / 2, TextHeight()); return;
            case ConsoleKey.End: _sidebar.MoveHighlight(int.MaxValue / 2, TextHeight()); return;
            case ConsoleKey.PageUp: _sidebar.MoveHighlight(-Math.Max(1, TextHeight() - 1), TextHeight()); return;
            case ConsoleKey.PageDown: _sidebar.MoveHighlight(Math.Max(1, TextHeight() - 1), TextHeight()); return;
            case ConsoleKey.Enter:
                if (_sidebar.EnterSelected())
                    return;
                string? path = _sidebar.SelectedPath;
                if (path is null)
                    return;
                _sidebarFocus = false;
                OpenPicked(path);
                return;
        }
    }

    private void DoRecent()
    {
        _settings.PruneRecent();
        _store.Save(_settings);
        if (_settings.RecentFiles.Count == 0)
        {
            SetMessage(_loc["msg.recent.empty"]);
            return;
        }
        _recentPaths.Clear();
        _recentPaths.AddRange(_settings.RecentFiles);
        _dialog = new ModalDialog(ModalState.Recent(_loc, _recentPaths), ApplyModalOutcome);
    }

    private void OpenRecentPick(int button)
    {
        if (_recentPaths.Count == 0)
            return;
        string path = _recentPaths[Math.Clamp(button, 0, _recentPaths.Count - 1)];
        _recentPaths.Clear();
        OpenPicked(path);
    }

    private void ClearDoc()
    {
        _buf.Clear();
        ResetCursor();
        SetMessage(_loc["msg.newdoc"]);
    }

    /// <summary>
    /// Discards changes: rolls back to the last save (rereads the file,
    /// clears untitled buffers). Without rollback the quit loop returns to the tab again.
    /// </summary>
    private void DiscardBuffer()
    {
        try
        {
            if (_buf.FilePath is not null && File.Exists(_buf.FilePath))
                _buf.Open(_buf.FilePath);
            else
                _buf.Clear();
        }
        catch
        {
            _buf.Clear();
        }
        _sel.Clear();
        ClampCursor();
    }

    private void ResetCursor()
    {
        _row = _col = _desiredCol = _top = _left = _topSeg = 0;
    }

    private void LoadFile(string path, bool force = false)
    {
        bool existed = File.Exists(path);
        if (!force && existed && GuardLargeFile(path))
            return;
        try
        {
            _buf.Open(path);
            ResetCursor();
            TouchRecent(path);
            SetMessage(existed ? _loc.Format("msg.opened", path) : _loc.Format("msg.newfile", path));
        }
        catch (Exception ex)
        {
            _pending = PendingOp.None;
            _dialog = new ModalDialog(ModalState.Error(_loc, _loc["error.open"], DisplayError(ex)), ApplyModalOutcome);
        }
    }

    /// <summary>Checks the size: defers large files to a dialog, stashing the path as pending.</summary>
    private bool GuardLargeFile(string path)
    {
        long bytes;
        try { bytes = new FileInfo(path).Length; }
        catch { return false; } // Size unknown - lets Open show any error
        if (bytes <= LargeFileBytes)
            return false;
        _pending = PendingOp.Open;
        _pendingPath = path;
        long mb = bytes >> 20;
        _dialog = new ModalDialog(
            ModalState.LargeFile(_loc, Path.GetFileName(path), mb, LargeFileBytes >> 20),
            ApplyModalOutcome);
        return true;
    }

    private string StartDir()
    {
        try
        {
            if (_buf.FilePath is not null)
            {
                string? d = Path.GetDirectoryName(Path.GetFullPath(_buf.FilePath));
                if (!string.IsNullOrEmpty(d) && Directory.Exists(d))
                    return d;
            }
        }
        catch
        {
        }
        try
        {
            return Directory.GetCurrentDirectory();
        }
        catch
        {
            return OperatingSystem.IsWindows() ? "C:\\" : "/";
        }
    }
}
