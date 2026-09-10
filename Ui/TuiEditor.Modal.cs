namespace TuiEdit;

/// <summary>TuiEditor: modal outcomes, pending ops, drafts and restore.</summary>
internal sealed partial class TuiEditor
{
    internal static bool DraftDue(DateTime last, DateTime now) =>
        (now - last).TotalSeconds >= 30;

    /// <summary>
    /// Computes the draft key for a tab: path hash, or per-tab id for untitled.
    /// Keys are always filename-safe (no separators or colons).
    /// </summary>
    private static string DraftKeyFor(DocTab t) =>
        t.Buf.FilePath is { } path ? DraftStore.KeyFor(path) : $"untitled-{t.DraftId}";

    private void AutoDraft()
    {
        if (!DraftDue(_lastDraftAt, DateTime.Now))
            return;
        _lastDraftAt = DateTime.Now;
        try
        {
            SaveTabState(); // Active tab view lives in fields; flush it before the sweep
            foreach (Pane p in _panes)
                foreach (DocTab t in p.Docs)
                {
                    if (!t.Buf.IsModified)
                        continue;
                    try
                    {
                        _drafts.WriteKey(DraftKeyFor(t), t.Buf.FilePath, t.Buf.Lines, t.Row, t.Col);
                    }
                    catch
                    {
                    }
                }
        }
        catch
        {
        }
    }

    /// <summary>Dumps drafts for all modified tabs in an emergency (for the crash handler).
    /// Never throws from the handler: wraps each write and the whole walk in try/catch.</summary>
    internal int EmergencyDump()
    {
        int n = 0;
        try
        {
            SaveTabState(); // Moves the active tab view from fields to the model
            foreach (Pane p in _panes)
                foreach (DocTab t in p.Docs)
                {
                    if (!t.Buf.IsModified)
                        continue;
                    try
                    {
                        _drafts.WriteKey(DraftKeyFor(t), t.Buf.FilePath, t.Buf.Lines, t.Row, t.Col);
                        n++;
                    }
                    catch
                    {
                    }
                }
        }
        catch
        {
        }
        return n;
    }

    private void MaybeRestore()
    {
        List<(string key, DocDraft draft)> all;
        try
        {
            all = _drafts.ReadAll();
        }
        catch
        {
            return;
        }
        if (all.Count == 0)
            return;
        _restoreDrafts.Clear();
        _restoreDrafts.AddRange(all.Take(20));
        ShowRestoreDialog();
    }

    private void ShowRestoreDialog()
    {
        _dialog = new ModalDialog(ModalState.Restore(_loc,
            _restoreDrafts.Select(d => (d.draft.File ?? _loc["status.noname"], d.draft.SavedAt)).ToList(),
            showRestoreAll: true),
            ApplyModalOutcome);
    }

    private void ApplyRestore(int button)
    {
        if (_restoreDrafts.Count == 0)
            return;
        if (button <= 0)
        {
            RestoreAllDrafts();
            return;
        }
        int index = Math.Clamp(button - 1, 0, _restoreDrafts.Count - 1);
        var (key, d) = _restoreDrafts[index];
        _restoreDrafts.RemoveAt(index);
        RestoreOneDraft(key, d);
        if (_restoreDrafts.Count > 0)
            ShowRestoreDialog(); // loop with the remainder, nothing is lost
        else
            SetMessage(_loc["msg.restored"]);
    }

    private void RestoreAllDrafts()
    {
        var all = _restoreDrafts.ToList();
        _restoreDrafts.Clear();
        foreach ((string key, DocDraft d) in all)
            RestoreOneDraft(key, d);
        SetMessage(_loc["msg.restored"]);
    }

    /// <summary>Restores one draft into a new tab (reuses the pristine startup tab).</summary>
    private void RestoreOneDraft(string key, DocDraft d)
    {
        NewTab();
        if (d.File is not null)
        {
            try
            {
                _buf.Open(d.File);
            }
            catch
            {
                _buf.Clear();
            }
        }
        else
        {
            _buf.Clear();
        }
        _buf.RestoreContent(d.Lines);
        ResetCursor();
        _row = Math.Clamp(d.Row, 0, _buf.Count - 1);
        _col = Math.Min(Math.Max(d.Col, 0), _buf.GetLine(_row).Length);
        TrackCol();
        try
        {
            _drafts.DeleteKey(key); // Restored - autosave/save/exit take over from here
        }
        catch
        {
        }
    }

    /// <summary>Applies the closed modal outcome (pending actions live here).</summary>
    private void ApplyModalOutcome(ModalState m, ModalKeyOutcome o)
    {
        if (o.Cancelled)
        {
            _overwritePath = string.Empty;
            _pendingGrepRow = 0;
            _recentPaths.Clear();
            if (_pending != PendingOp.None)
            {
                _pending = PendingOp.None;
                SetMessage(_loc["msg.cancelled"]);
            }
            return;
        }
        switch (m.Kind, o.Button)
        {
            case (ModalKind.UnsavedQuit, var b):
                switch (MapUnsavedButton(b))
                {
                    case UnsavedAction.Save:
                        SaveFlowForPending();
                        return;
                    case UnsavedAction.Discard:
                        _drafts.DeleteKey(DraftKeyFor(_docs[_active]));
                        DiscardBuffer();
                        ApplyPending();
                        return;
                    default:
                        _pending = PendingOp.None;
                        SetMessage(_loc["msg.cancelled"]);
                        return;
                }
            case (ModalKind.Overwrite, 0):
                string overwriteDraftKey = DraftKeyFor(_docs[_active]);
                try
                {
                    _buf.Save(_overwritePath, Backups);
                    TouchRecent(_buf.FilePath);
                    _drafts.DeleteKey(overwriteDraftKey); // path may have changed
                    _drafts.DeleteKey(DraftKeyFor(_docs[_active]));
                    SetMessage(_loc.Format("msg.saved", _buf.FilePath));
                }
                catch (Exception ex)
                {
                    _overwritePath = string.Empty;
                    _pending = PendingOp.None;
                    _dialog = new ModalDialog(ModalState.Error(_loc, _loc["error.save"], DisplayError(ex)), ApplyModalOutcome);
                    return;
                }
                _overwritePath = string.Empty;
                ApplyPending();
                return;
            case (ModalKind.ReplaceConfirm, 0):
                DoReplace(_pendingReplaceTerm, _pendingReplaceRep);
                _pendingReplaceTerm = string.Empty;
                _pendingReplaceRep = string.Empty;
                return;
            case (ModalKind.ReplaceConfirm, _):
                _pendingReplaceTerm = string.Empty;
                _pendingReplaceRep = string.Empty;
                SetMessage(_loc["msg.cancelled"]);
                return;
            case (ModalKind.LargeFile, 0):
                ApplyPending(); // Same PendingOp.Open route, but with force
                return;
            case (ModalKind.LargeFile, _):
                _pending = PendingOp.None;
                SetMessage(_loc["msg.cancelled"]);
                return;
            case (ModalKind.Complete, var b):
                ApplyCompletion(_pendingCompletePrefix, m.Buttons[b].Label);
                _pendingCompletePrefix = string.Empty;
                return;
            case (ModalKind.Grep, var b):
                OpenGrepHit(b);
                return;
            case (ModalKind.Recent, var b):
                OpenRecentPick(b);
                return;
            case (ModalKind.Tabs, var b):
                SwitchTab(b);
                return;
            case (ModalKind.Restore, var b):
                ApplyRestore(b);
                return;
            default:
                _overwritePath = string.Empty;
                if (_pending != PendingOp.None)
                {
                    _pending = PendingOp.None;
                    SetMessage(_loc["msg.cancelled"]);
                }
                return;
        }
    }

    internal static UnsavedAction MapUnsavedButton(int button) => button switch
    {
        0 => UnsavedAction.Save,
        1 => UnsavedAction.Discard,
        _ => UnsavedAction.Cancel,
    };

    /// <summary>Saves from the popup with a deferred action afterwards.</summary>
    private void SaveFlowForPending()
    {
        try
        {
            switch (_buf.FilePath)
            {
                case null:
                    string? path = PickSavePath(_loc["status.untitled"]);
                    if (path is null)
                    {
                        _pending = PendingOp.None;
                        SetMessage(_loc["msg.cancelled"]);
                        return;
                    }
                    if (File.Exists(path))
                    {
                        _overwritePath = path;
                        _dialog = new ModalDialog(ModalState.Overwrite(_loc, Path.GetFileName(path)), ApplyModalOutcome);
                        return;
                    }
                    _buf.Save(path, Backups);
                    break;
                default:
                    _buf.Save(backup: Backups);
                    break;
            }
            TouchRecent(_buf.FilePath);
            _drafts.Delete(_buf.FilePath);
            SetMessage(_loc.Format("msg.saved", _buf.FilePath));
            ApplyPending();
        }
        catch (Exception ex)
        {
            _pending = PendingOp.None;
            _dialog = new ModalDialog(ModalState.Error(_loc, _loc["error.save"], DisplayError(ex)), ApplyModalOutcome);
        }
    }

    private void ApplyPending()
    {
        PendingOp p = _pending;
        string path = _pendingPath;
        _pending = PendingOp.None;
        switch (p)
        {
            case PendingOp.Quit:
                if (AnyModified())
                {
                    ActivateFirstModified();
                    _pending = PendingOp.Quit;
                    _dialog = new ModalDialog(ModalState.UnsavedQuit(_loc, DirtyLabel()), ApplyModalOutcome);
                }
                else
                {
                    _quitRequested = true;
                }
                break;
            case PendingOp.Open:
                LoadFile(path, force: true);
                if (_pendingGrepRow > 0)
                {
                    GoToLineNumber(_pendingGrepRow);
                    _pendingGrepRow = 0;
                }
                break;
            case PendingOp.CloseTab: CloseTabNow(); break;
            default: break;
        }
    }

    private string CurrentMessage =>
        DateTime.Now <= _messageUntil ? _message : string.Empty;
}
