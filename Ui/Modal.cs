namespace TuiEdit;

/// <summary>Specifies the modal popup kind (affects colors).</summary>
internal enum ModalKind
{
    /// <summary>Represents unsaved changes / confirmation (red - danger).</summary>
    UnsavedQuit,
    /// <summary>Represents the about box (blue).</summary>
    About,
    /// <summary>Represents an error (red).</summary>
    Error,
    /// <summary>Represents a file overwrite (red).</summary>
    Overwrite,
    /// <summary>Represents recent files (blue, buttons as a list).</summary>
    Recent,
    /// <summary>Represents the tab list (blue, buttons as a list).</summary>
    Tabs,
    /// <summary>Represents draft restore (blue, buttons as a list).</summary>
    Restore,
    /// <summary>Represents bulk replace (red).</summary>
    ReplaceConfirm,
    /// <summary>Represents autocomplete (blue, buttons as a list).</summary>
    Complete,
    /// <summary>Represents file search results (blue, buttons as a list).</summary>
    Grep,
    /// <summary>Represents a large file open confirmation (red).</summary>
    LargeFile,
}

/// <summary>Represents a popup button: label and hotkey letter (without Enter). '\0' means no hotkey.</summary>
internal sealed record ModalButton(string Label, char Hotkey);

internal readonly record struct ModalKeyOutcome(bool Done, bool Cancelled, int Button)
{
    /// <summary>Gets an outcome that keeps the popup open (key swallowed).</summary>
    public static readonly ModalKeyOutcome Open = new(false, false, -1);

    /// <summary>Creates an outcome closed via Esc.</summary>
    public static ModalKeyOutcome Cancel() => new(true, true, -1);

    public static ModalKeyOutcome Press(int index) => new(true, false, index);
}

/// <summary>Represents a modal popup: titled frame, focus trap (swallows all input), Esc cancels, buttons activate via Enter, arrows, and single-key hotkeys.</summary>
internal sealed class ModalState
{
    public ModalKind Kind { get; }

    public string Title { get; }

    public List<string> Lines { get; }

    /// <summary>Gets the buttons left to right.</summary>
    public List<ModalButton> Buttons { get; }

    public int Selected { get; private set; }

    /// <summary>Gets the first visible button (vertical list scroll).</summary>
    public int ButtonTop { get; private set; }

    /// <summary>Gets how many buttons are visible at once (vertical list scroll).</summary>
    public int MaxVisibleButtons { get; }

    public bool Danger => Kind is ModalKind.UnsavedQuit or ModalKind.Error or ModalKind.Overwrite or ModalKind.ReplaceConfirm or ModalKind.LargeFile;

    public string Hint { get; }

    private ModalState(ModalKind kind, string title, List<string> lines, List<ModalButton> buttons, int selected, string hint, int maxVisibleButtons = int.MaxValue)
    {
        Kind = kind;
        Title = title;
        Lines = lines;
        Buttons = buttons;
        Selected = Math.Clamp(selected, 0, buttons.Count - 1);
        Hint = hint;
        MaxVisibleButtons = Math.Max(1, maxVisibleButtons);
        EnsureButtonVisible();
    }

    private void EnsureButtonVisible()
    {
        if (Selected < ButtonTop)
            ButtonTop = Selected;
        else if (Selected >= ButtonTop + MaxVisibleButtons)
            ButtonTop = Selected - MaxVisibleButtons + 1;
    }

    /// <summary>Creates the "unsaved changes" popup: buttons with bracketed hotkeys, no hints.</summary>
    public static ModalState UnsavedQuit(Loc loc, string? file) => new(
        ModalKind.UnsavedQuit,
        loc["modal.unsaved.title"],
        new List<string> { file is null ? loc["modal.unsaved.desc"] : loc.Format("modal.unsaved.descfile", file) },
        new List<ModalButton>
        {
            new(loc["modal.unsaved.save"], 'Y'),
            new(loc["modal.unsaved.discard"], 'N'),
            new(loc["modal.unsaved.cancel"], '\0'),
        },
        selected: 0,
        hint: string.Empty);

    public static ModalState About(Loc loc, string version) => new(
        ModalKind.About,
        loc["modal.about.title"],
        new List<string>
        {
            loc.Format("modal.about.l1", version),
            loc["modal.about.l2"],
            loc["modal.about.date"],
            loc["modal.about.author"],
            loc["modal.about.license"],
        },
        new List<ModalButton> { new(loc["modal.about.ok"], '\0') },
        selected: 0,
        hint: string.Empty);

    /// <summary>Creates the error popup (title and text are already localized by the caller).</summary>
    public static ModalState Error(Loc loc, string title, string message) => new(
        ModalKind.Error,
        title,
        message.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).Take(6).ToList(),
        new List<ModalButton> { new(loc["modal.error.ok"], '\0') },
        selected: 0,
        loc["modal.close.hint"]);

    public static ModalState Overwrite(Loc loc, string fileName) => new(
        ModalKind.Overwrite,
        loc["picker.ow.title"],
        new List<string> { loc["picker.ow.exists"], fileName },
        new List<ModalButton>
        {
            new(loc["picker.ow.yes"], 'Y'),
            new(loc["picker.ow.no"], 'N'),
        },
        selected: 0,
        hint: string.Empty);

    public static ModalState ConfirmReplace(
        Loc loc, string term, int count, (int row, string before, string after)? preview = null) => new(
        ModalKind.ReplaceConfirm,
        loc["modal.replace.title"],
        PreviewLines(loc, term, count, preview),
        new List<ModalButton>
        {
            new(loc["picker.ow.yes"], 'Y'),
            new(loc["picker.ow.no"], 'N'),
        },
        selected: 0,
        hint: string.Empty);

    private static List<string> PreviewLines(
        Loc loc, string term, int count, (int row, string before, string after)? preview)
    {
        var lines = new List<string> { loc.Format("modal.replace.desc", count, term) };
        if (preview is { } p)
        {
            lines.Add($"{p.row + 1}: {p.before}");
            lines.Add($"  → {p.after}");
        }
        return lines;
    }

    /// <summary>Creates the large-file prompt: open anyway? Safe default is "No".</summary>
    public static ModalState LargeFile(Loc loc, string fileName, long megabytes, long limitMegabytes) => new(
        ModalKind.LargeFile,
        loc["modal.largefile.title"],
        new List<string> { loc.Format("modal.largefile.desc", megabytes, limitMegabytes), fileName },
        new List<ModalButton>
        {
            new(loc["picker.ow.yes"], 'Y'),
            new(loc["picker.ow.no"], 'N'),
        },
        selected: 1,
        hint: string.Empty);

    /// <summary>Creates the autocomplete popup: candidate words as a list; empty lists are rejected.</summary>
    public static ModalState Complete(Loc loc, List<string> words)
    {
        if (words.Count == 0)
            throw new ArgumentException("Нет вариантов.", nameof(words));
        return new(
            ModalKind.Complete,
            loc["modal.complete.title"],
            new List<string>(),
            words.Select((w, i) => new ModalButton(w, NumberHotkey(i))).ToList(),
            selected: 0,
            hint: string.Empty,
            maxVisibleButtons: 10);
    }

    /// <summary>Creates the grep results popup: file:line rows; empty lists are rejected.</summary>
    public static ModalState Grep(Loc loc, List<GrepHit> hits)
    {
        if (hits.Count == 0)
            throw new ArgumentException("Нет совпадений.", nameof(hits));
        return new(
            ModalKind.Grep,
            loc["modal.grep.title"],
            new List<string>(),
            hits.Select((h, i) => new ModalButton(
                $"{ShortGrepPath(h.File)}:{h.Row + 1}: {h.Text.Trim()}", NumberHotkey(i))).ToList(),
            selected: 0,
            hint: string.Empty,
            maxVisibleButtons: 10);
    }

    private static string ShortGrepPath(string path) => Shorten(path, 40);

    /// <summary>Creates the recent-files popup: each file is a row button with a 1..9,0 hotkey; shows 5 at once, scrolls the rest; empty lists are rejected.</summary>
    public static ModalState Recent(Loc loc, List<string> files)
    {
        if (files.Count == 0)
            throw new ArgumentException("Нет файлов.", nameof(files));
        return new(
            ModalKind.Recent,
            loc["modal.recent.title"],
            new List<string>(),
            files.Select((f, i) => new ModalButton(NumberedLabel(i, ShortPath(f)), NumberHotkey(i))).ToList(),
            selected: 0,
            hint: string.Empty,
            maxVisibleButtons: 5);
    }

    private static string ShortPath(string path) => Shorten(path, 48);

    /// <summary>Shortens a path from the left, keeping a "..." + tail of <paramref name="max"/>.</summary>
    private static string Shorten(string path, int max) =>
        path.Length <= max ? path : "..." + path[^(max - 3)..];

    /// <summary>Gets the button hotkey by index: 1..9,0, then no hotkey.</summary>
    private static char NumberHotkey(int i) => i < 9 ? (char)('1' + i) : i == 9 ? '0' : '\0';

    private static string NumberedLabel(int i, string text)
    {
        char hot = NumberHotkey(i);
        return hot == '\0' ? $"    {text}" : $"[{hot}] {text}";
    }

    /// <summary>Creates the tab-list popup: each tab is a row button with a 1..9,0 hotkey; shows 5 at once, scrolls the rest; empty lists are rejected.</summary>
    public static ModalState Tabs(Loc loc, List<string> titles)
    {
        if (titles.Count == 0)
            throw new ArgumentException("Нет вкладок.", nameof(titles));
        return new(
            ModalKind.Tabs,
            loc["modal.tabs.title"],
            new List<string>(),
            titles.Select((t, i) => new ModalButton(NumberedLabel(i, t), NumberHotkey(i))).ToList(),
            selected: 0,
            hint: string.Empty,
            maxVisibleButtons: 5);
    }

    /// <summary>Creates the draft-restore popup: each draft is a row button with a 1..9,0 hotkey and save date; shows 5 at once, scrolls the rest; empty lists are rejected.</summary>
    public static ModalState Restore(
        Loc loc, List<(string name, DateTime savedAt)> items, bool showRestoreAll = false)
    {
        if (items.Count == 0)
            throw new ArgumentException("Нет файлов.", nameof(items));
        var buttons = new List<ModalButton>();
        if (showRestoreAll)
            buttons.Add(new ModalButton(NumberedLabel(0, loc.Format("modal.restore.all", items.Count)), NumberHotkey(0)));
        int offset = buttons.Count;
        buttons.AddRange(items.Select((it, i) => new ModalButton(
            NumberedLabel(offset + i, $"{ShortPath(it.name)}  {it.savedAt.ToLocalTime():dd.MM HH:mm}"),
            NumberHotkey(offset + i))));
        return new(
            ModalKind.Restore,
            loc["modal.restore.title"],
            new List<string> { loc["modal.restore.desc"] },
            buttons,
            selected: 0,
            hint: string.Empty,
            maxVisibleButtons: 5);
    }

    /// <summary>Handles a key: Enter presses the highlighted button, Esc cancels, a letter hotkey presses immediately; swallows the rest (focus trap).</summary>
    public ModalKeyOutcome HandleKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
            return ModalKeyOutcome.Cancel();

        if ((key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) != 0)
            return ModalKeyOutcome.Open; // Swallows Ctrl/Alt, keeps the popup open

        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
            case ConsoleKey.UpArrow:
                Selected = (Selected - 1 + Buttons.Count) % Buttons.Count;
                EnsureButtonVisible();
                return ModalKeyOutcome.Open;
            case ConsoleKey.RightArrow:
            case ConsoleKey.DownArrow:
                Selected = (Selected + 1) % Buttons.Count;
                EnsureButtonVisible();
                return ModalKeyOutcome.Open;
            case ConsoleKey.Home:
                Selected = 0;
                EnsureButtonVisible();
                return ModalKeyOutcome.Open;
            case ConsoleKey.End:
                Selected = Buttons.Count - 1;
                EnsureButtonVisible();
                return ModalKeyOutcome.Open;
            case ConsoleKey.Enter:
                return ModalKeyOutcome.Press(Selected);
        }

        char c = char.ToUpperInvariant(key.KeyChar);
        if (c == '\0' || char.IsControl(c))
            return ModalKeyOutcome.Open;

        for (int i = 0; i < Buttons.Count; i++)
            if (MatchesHotkey(c, Buttons[i].Hotkey))
                return ModalKeyOutcome.Press(i);

        return ModalKeyOutcome.Open;
    }

    private static bool MatchesHotkey(char pressed, char hotkey) => char.ToUpperInvariant(hotkey) switch
    {
        '\0' => false,
        // Russian layout: S→Ы, N→Т, Y→Н.
        'S' => pressed is 'S' or 'Ы',
        'N' => pressed is 'N' or 'Т',
        'Y' => pressed is 'Y' or 'Н',
        var h => pressed == h,
    };
}
