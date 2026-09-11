using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace TuiEdit;

internal sealed partial class TuiEditor
{
    private readonly List<Pane> _panes = new(); // Split panes, _panes[_pane] is active
    private int _pane;
    /// <summary>Gets the tabs of the active pane.</summary>
    private List<DocTab> _docs => _panes[_pane].Docs;
    /// <summary>Gets or sets the active tab of the active pane.</summary>
    private int _active { get => _panes[_pane].Active; set => _panes[_pane].Active = value; }
    /// <summary>Gets or sets the tab-row scroll of the active pane.</summary>
    private int _tabLeft { get => _panes[_pane].TabLeft; set => _panes[_pane].TabLeft = value; }
    /// <summary>Gets the buffer of the active tab.</summary>
    private TextBuffer _buf => _docs[_active].Buf;
    private int _row;
    private int _col;
    private int _desiredCol; // Column memory for Up/Down
    private int _top;        // First visible row
    private int _left;       // First visible column
    private int _topSeg;     // Segment of the first row on wrap (0 otherwise)
    private string _message = string.Empty;
    private DateTime _messageUntil = DateTime.MinValue;
    private readonly List<string> _clipboard = new();
    private string _lastSearch = string.Empty;
    private string _lastReplace = string.Empty;
    /// <summary>Stores the live highlight term while typing in the prompt (null disables).</summary>
    internal string? _liveSearch;
    private bool _quitRequested;
    private readonly TextSelection _sel = new();
    private DocTab? _previewTab; // single sidebar preview tab (null — pinned or gone)
    private MenuState? _menu;   // null means the menu bar is closed
    private SidebarState? _sidebar; // null means the file panel is closed (SidebarState.Width wide)
    private bool _sidebarFocus;     // Input goes to the panel, not the text
    private Dialog? _dialog;    // null means no dialog (modal/manager/settings/help)
    private PendingOp _pending = PendingOp.None;
    private string _pendingPath = string.Empty;
    private string _overwritePath = string.Empty; // Path from the overwrite modal
    private string _pendingReplaceTerm = string.Empty; // Replacement from the confirm modal
    private string _pendingReplaceRep = string.Empty;
    private string _pendingCompletePrefix = string.Empty; // Prefix from the completion popup
    private readonly List<GrepHit> _grepHits = new();
    private int _pendingGrepRow;
    private readonly List<string> _recentPaths = new(); // Paths from the recent modal
    private readonly DraftStore _drafts = new(DraftStore.DefaultDir());
    private readonly BackupStore _backups;
    private DateTime _lastDraftAt = DateTime.MinValue;
    private readonly List<(string key, DocDraft draft)> _restoreDrafts = new();
    private readonly List<int> _menuX = new(); // Menu x-coordinates in the bar (from render)
    private readonly Screen _screen = new(); // Frame plus diff output (no flicker)
    private int _mouseX; // Last mouse position (for hover)
    private int _mouseY;
    private bool _mouseActive; // Mouse was the last input - hover instead of selection highlight
    private bool _mouseDrag; // Drag with held left button (press was in text)
    private int _mousePane; // Drag start pane (focus stays mid-drag)
    private int _mouseRow; // Cursor at press time (anchor of the future drag)
    private int _mouseCol;
    /// <summary>Press armed one of its buttons; release on the same button activates (Jumbee-style).</summary>
    private Dialog? _armedDialog;
    private int _armedButton = -1;
    /// <summary>Press armed a menu item; release activates the item under the cursor (allows sliding).</summary>
    private MenuState? _armedMenu;
    // Input reads via static InputReader.Read (stateless).
    /// <summary>Lookahead stashed by burst coalescing: processed before blocking on the console.</summary>
    private InputEvent? _heldEvent;
    /// <summary>Minimum time between frames: input always drains, paint caps at ~25fps (flood-proofing).</summary>
    internal const int RenderThrottleMs = 40;
    /// <summary>Frames slower than this are reported to the input log (diagnostics only).</summary>
    internal const int SlowFrameMs = 50;
    private DateTime _lastRenderAt = DateTime.MinValue;
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private string? _keyBindingsPath;
    private DateTime _keyBindingsMtime = DateTime.MinValue;
    private readonly CommandDispatcher _dispatcher;
    private readonly IGitService _git;
    private readonly ISystemClipboard _clipboardSvc;
    private readonly KeyBindingTable _keys;
    private Loc _loc;
    private Theme _theme;

    /// <summary>Gets the version from the assembly (csproj Version); falls back for manual builds.</summary>
    internal static string AppVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.9.0";

    /// <summary>Specifies the deferred action after the "unsaved changes" dialog.</summary>
    private enum PendingOp { None, Quit, Open, CloseTab }

    public TuiEditor(
        TextBuffer buf,
        AppSettings settings,
        SettingsStore store,
        IGitService? git = null,
        ISystemClipboard? clipboard = null,
        KeyBindingTable? keys = null)
    {
        ArgumentNullException.ThrowIfNull(buf);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);
        _panes.Add(new Pane(new DocTab(buf)));
        _settings = settings;
        _store = store;
        _git = git ?? GitService.Shared;
        _clipboardSvc = clipboard ?? SystemClipboardService.Shared;
        _keys = keys ?? KeyMap.Current;
        _backups = new BackupStore(BackupStore.DefaultDir(store.Path));
        _loc = Loc.Load(settings.Language);
        _theme = ThemeCatalog.Resolve(settings, settings.Theme);
        _dispatcher = new CommandDispatcher(BuildCommandMap());
    }

    /// <summary>Command dispatcher (every <see cref="EditorCommand"/> except None must resolve).</summary>
    internal ICommandDispatcher Dispatcher => _dispatcher;

    /// <summary>
    /// Skips the restore-drafts picker at startup: explicit files (or a start directory)
    /// on the command line mean work, not recovery. Drafts are kept and still offered
    /// on plain launches. Set by the composition root; tests set it directly.
    /// </summary>
    internal bool SuppressRestoreDialog { get; set; }

    /// <summary>Tracks an external keybindings file for live reload (no restart needed).</summary>
    /// <param name="path">The keybindings.json path (null disables tracking).</param>
    internal void TrackKeyBindings(string? path)
    {
        _keyBindingsPath = path;
        _keyBindingsMtime = ReadMtime(path);
    }

    /// <summary>Reapplies keybindings when the tracked file changed (cheap mtime stat).</summary>
    private void MaybeReloadKeyBindings()
    {
        if (_keyBindingsPath is null)
            return;
        DateTime mtime = ReadMtime(_keyBindingsPath);
        if (mtime == _keyBindingsMtime)
            return;
        _keyBindingsMtime = mtime;
        Dictionary<string, string?> raw;
        try
        {
            raw = KeyBindings.Load(_keyBindingsPath);
        }
        catch
        {
            return;
        }
        try
        {
            KeyMap.SetOverrides(raw);
            _keys.ApplyOverrides(raw);
        }
        catch
        {
        }
    }

    private static DateTime ReadMtime(string? path)
    {
        try
        {
            return path is null ? DateTime.MinValue : File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private BackupStore? Backups => _settings.BackupOnSave ? _backups : null;

    /// <summary>Gets the tab count of the active pane.</summary>
    internal int TabCount => _docs.Count;

    /// <summary>Gets the active tab index of the active pane.</summary>
    internal int ActiveTab => _active;

    /// <summary>Gets the pane count.</summary>
    internal int PaneCount => _panes.Count;

    /// <summary>Gets the active pane index.</summary>
    internal int ActivePane => _pane;

    /// <summary>Saves the active tab view into the model.</summary>
    internal void SaveTabState()
    {
        DocTab t = _docs[_active];
        t.Row = _row; t.Col = _col; t.DesiredCol = _desiredCol;
        t.Top = _top; t.Left = _left; t.TopSeg = _topSeg;
        t.SelActive = _sel.HasSelection(_row, _col);
        if (t.SelActive) { t.AnchorRow = _sel.AnchorRow; t.AnchorCol = _sel.AnchorCol; }
    }

    /// <summary>Loads the active tab view from the model.</summary>
    internal void LoadTabState()
    {
        DocTab t = _docs[_active];
        _row = t.Row; _col = t.Col; _desiredCol = t.DesiredCol;
        _top = t.Top; _left = t.Left; _topSeg = t.TopSeg;
        ClampCursor();
        _sel.Clear();
        if (t.SelActive)
            _sel.Start(t.AnchorRow, t.AnchorCol);
    }

    private void ApplySettings()
    {
        _loc = Loc.Load(_settings.Language);
        _theme = ThemeCatalog.Resolve(_settings, _settings.Theme);
        ApplyMouseSetting();
    }

    /// <summary>Applies the mouse level live: input, SGR, focus, and console flags.</summary>
    private void ApplyMouseSetting()
    {
        InputReader.MouseLevel = _settings.Mouse;
        if (_settings.Mouse == MouseLevel.Off)
        {
            Terminal.DisableMouse();
            Terminal.DisableFocusTracking();
        }
        else
        {
            Terminal.SetMouseLevel(_settings.Mouse);
            Terminal.TryEnableFocusTracking(); // Needs DEC mode restore on focus-in
        }
        Terminal.ApplyMouseInput(_settings.Mouse != MouseLevel.Off);
    }

    private string DisplayError(Exception ex) => ex switch
    {
        InvalidOperationException { Message: "NoFileName" } => _loc["error.nofilename"],
        InvalidOperationException { Message: "ReadOnly" } => _loc["error.readonly"],
        _ => ex.Message,
    };

    public void Run()
    {
        // Setup is best-effort too: a throwing console must not mask startup
        // (the loop below guards itself; teardown guards itself in finally).
        InputLog.Announce(starting: true);
        InputLog.Session("start");
        try { Console.TreatControlCAsInput = true; } catch { }
        try { Console.CursorVisible = false; } catch { }
        try { _screen.TrueColor = Terminal.TryEnableVirtualTerminal(); } catch { }
        try { Terminal.TryEnableRawInput(); } catch { }

        try
        {
            try { Console.Write("\x1b[?2004h"); } catch (IOException) { }
            ApplyMouseSetting();
            MaybeRestore();
            if (_docs.Count == 1 && _buf.FilePath is null && !_buf.IsModified)
                SetMessage(_loc["msg.hint"]);
            Render();
            while (!_quitRequested)
            {
                InputEvent ev;
                if (_heldEvent is not null)
                {
                    ev = _heldEvent;
                    _heldEvent = null;
                }
                else
                {
                    try
                    {
                        ev = InputReader.Read();
                    }
                    catch (InvalidOperationException)
                    {
                        return;
                    }
                }
                try
                {
                    HandleInput(ev);
                    // One frame per wakeup, not per event: drain everything pending
                    // (held keys, paste bursts) before the next render.
                    InputReader.DrainPending(HandleInput);
                    AutoDraft();
                    MaybeReloadKeyBindings();
                    RenderThrottled();
                }
                catch (Exception ex)
                {
                    // One bad event must not kill the session: log and keep editing.
                    // (Read blocks on the console, so a repeatedly throwing state
                    // still waits for keys instead of hot-spinning.)
                    CrashLog.Write("event", ex);
                    SetMessage(_loc["error.eventfailed"]);
                }
            }
            SaveSessionTabs();
        }
        finally
        {
            // Every step guarded: teardown must never mask the original error
            // nor leave raw input/mouse mode behind.
            InputLog.Session("stop");
            try { Terminal.DiscardPendingInput(); } catch { }
            try { Console.Write("\x1b[?2004l"); } catch { }
            try { Terminal.DisableMouse(); } catch { }
            try { Terminal.DisableFocusTracking(); } catch { }
            try { Terminal.RestoreInput(); } catch { }
            try { Console.ResetColor(); } catch { }
            try { Console.Clear(); } catch { }
            try { Console.CursorVisible = true; } catch { }
        }
    }

    /// <summary>Specifies that files over the limit open only via confirmation (freezes highlighting).</summary>
    internal const long LargeFileBytes = 16 << 20;

    internal enum UnsavedAction { Save, Discard, Cancel }

    internal const int ReplaceConfirmThreshold = 50;

    private void SetMessage(string m)
    {
        _message = m;
        _messageUntil = DateTime.Now.AddSeconds(4);
    }

    /// <summary>Shows a startup notice (corrupt settings and the like).</summary>
    /// <param name="message">The localized message to show.</param>
    internal void Notify(string message) => SetMessage(message);

}
