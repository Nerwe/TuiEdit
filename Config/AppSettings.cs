namespace TuiEdit;

/// <summary>
/// Represents user settings (stored in JSON, see <see cref="SettingsStore"/>).
/// </summary>
internal sealed class AppSettings
{
    /// <summary>Gets or sets the theme: dark | light | a custom one from Themes.</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>Gets or sets the highlighting grammar: auto (by extension) or a language name.</summary>
    public string Grammar { get; set; } = "auto";

    /// <summary>Gets or sets the language: en | ru.</summary>
    public string Language { get; set; } = "en";

    /// <summary>Gets or sets a value that indicates whether search/replace respects case.</summary>
    public bool SearchMatchCase { get; set; } = true;

    /// <summary>Gets or sets a value that indicates whether search/replace matches whole words only.</summary>
    public bool SearchWholeWord { get; set; }

    /// <summary>Gets or sets a value that indicates whether search/replace uses a regular expression.</summary>
    public bool SearchUseRegex { get; set; }

    /// <summary>Gets or sets a value that indicates whether line numbers (gutter) are shown.</summary>
    public bool ShowLineNumbers { get; set; } = true;

    /// <summary>Gets or sets a value that indicates whether vertical guides show at indent levels.</summary>
    public bool ShowIndentGuides { get; set; } = true;

    /// <summary>Gets or sets a value that indicates whether whitespace and tabs show as dots and arrows.</summary>
    public bool ShowWhitespace { get; set; }

    /// <summary>Gets or sets the highlighted ruler column (0 disables it).</summary>
    public int RulerColumn { get; set; }

    /// <summary>Gets or sets the status bar left format ($(msg), $(pos), $(sel), $(file), $(opt:name), $(bind:Command)).</summary>
    public string StatusFormatLeft { get; set; } = " $(msg)$(pos)$(sel)";

    /// <summary>Gets or sets the status bar right format ($(encoding), $(ending), $(indent), $(file), $(git), $(tab), $(pane)).</summary>
    public string StatusFormatRight { get; set; } = " $(encoding) | $(ending) | $(indent) | $(file)$(git)$(tab)$(pane)";

    /// <summary>Gets or sets a value that indicates whether long lines wrap softly.</summary>
    public bool WordWrap { get; set; }

    /// <summary>Gets or sets a value that indicates whether a .bak copy is created on save.</summary>
    public bool BackupOnSave { get; set; }

    /// <summary>Gets or sets a value that indicates whether tabs from the previous session open at startup.</summary>
    public bool RestoreSession { get; set; }

    /// <summary>Gets or sets the mouse capture level (off by default — not all terminals behave correctly).</summary>
    public MouseLevel Mouse { get; set; }

    /// <summary>Gets or sets a value that indicates whether mouse selection copies to the buffer on release.</summary>
    public bool CopyOnSelect { get; set; } = true;

    /// <summary>Gets or sets a value that indicates whether brackets and quotes auto-pair while typing.</summary>
    public bool AutoPairs { get; set; } = true;

    /// <summary>Gets or sets a value that indicates whether Git marks appear in the gutter (added/modified).</summary>
    public bool GitGutter { get; set; } = true;

    /// <summary>Gets or sets the legacy mouse flag (pre-levels): for migration in <see cref="Normalize"/> only.</summary>
    public bool EnableMouse { get; set; }

    /// <summary>Gets or sets the previous session tabs (path plus cursor).</summary>
    public List<SessionTab> SessionTabs { get; set; } = new();

    /// <summary>Gets or sets recent files (newest first).</summary>
    public List<string> RecentFiles { get; set; } = new();

    /// <summary>Gets or sets custom themes (see ThemeScheme).</summary>
    public List<ThemeScheme> Themes { get; set; } = new();

    /// <summary>Specifies the maximum number of recent files.</summary>
    public const int MaxRecentFiles = 20;

    /// <summary>Specifies the maximum number of session tabs.</summary>
    public const int MaxSessionTabs = 20;

    /// <summary>Specifies the maximum total characters of an untitled tab kept in the session.</summary>
    public const int MaxUntitledSessionChars = 256 * 1024;

    /// <summary>Marks a file as recent (moves to top, removes duplicates, trims).</summary>
    public void TouchRecent(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            return;
        }
        StringComparison cmp = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        RecentFiles.RemoveAll(p => string.Equals(p, full, cmp));
        RecentFiles.Insert(0, full);
        if (RecentFiles.Count > MaxRecentFiles)
            RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
    }

    /// <summary>Removes nonexistent files from the recent list.</summary>
    public void PruneRecent() => RecentFiles.RemoveAll(p => !File.Exists(p));
    /// <summary>Normalizes settings to valid values (tolerates nulls from corrupt JSON).</summary>
    public void Normalize()
    {
        Themes ??= new();
        SessionTabs ??= new();
        RecentFiles ??= new();
        if (!ThemeCatalog.Contains(this, Theme))
            Theme = "dark";
        Themes.RemoveAll(s => string.IsNullOrWhiteSpace(s.Name));
        foreach (ThemeScheme s in Themes)
            s.Colors ??= new();
        if (string.IsNullOrWhiteSpace(Grammar))
            Grammar = "auto";
        Language = Loc.Normalize(Language);
        if (!Enum.IsDefined(Mouse))
            Mouse = MouseLevel.Off;
        if (EnableMouse && Mouse == MouseLevel.Off)
            Mouse = MouseLevel.Basic; // Migrates from the legacy flag
        EnableMouse = false;
    }
}

/// <summary>
/// Represents a previous session tab: path and cursor position; untitled tabs
/// (empty path) carry a content snapshot instead.
/// </summary>
internal sealed record SessionTab(string Path, int Row, int Col, List<string>? Lines = null);
