namespace TuiEdit;

/// <summary>
/// Палитра интерфейса: именованные роли вместо разбросанных цветов.
/// </summary>
public sealed record Theme(
    string Name,
    // Редактор
    ConsoleColor EditorBg, ConsoleColor EditorFg,
    ConsoleColor CurLineBg, ConsoleColor CurLineFg,
    ConsoleColor GutterFg, ConsoleColor FillerFg,
    ConsoleColor SelBg, ConsoleColor SelFg,
    ConsoleColor MatchBg, ConsoleColor MatchFg,
    // Меню
    ConsoleColor MenuBarBg, ConsoleColor MenuFg, ConsoleColor MenuHotkeyFg,
    ConsoleColor MenuOpenBg, ConsoleColor MenuOpenFg,
    ConsoleColor DropBg, ConsoleColor DropFg, ConsoleColor DropBorderFg,
    ConsoleColor DropSelBg, ConsoleColor DropSelFg, ConsoleColor DropDimFg,
    // Статус и промпт
    ConsoleColor StatusBg, ConsoleColor StatusFg,
    ConsoleColor PromptBg, ConsoleColor PromptFg,
    // Модалки
    ConsoleColor ModalBg, ConsoleColor ModalFg,
    ConsoleColor ModalDangerBg, ConsoleColor ModalDangerFg,
    ConsoleColor ModalHintFg, ConsoleColor ModalHintDangerFg,
    ConsoleColor ButtonSelBg, ConsoleColor ButtonSelFg,
    // Менеджер файлов
    ConsoleColor PickerDirFg, ConsoleColor PickerUpFg, ConsoleColor PickerFileFg,
    ConsoleColor PickerEmptyFg, ConsoleColor PickerErrorFg, ConsoleColor PickerHintFg
);

/// <summary>Встроенные темы: тёмная и светлая.</summary>
public static class Themes
{
    /// <summary>Имена тем для настроек.</summary>
    public static readonly string[] Names = ["dark", "light"];

    /// <summary>Тёмная тема (классика).</summary>
    public static Theme Dark { get; } = new(
        Name: "dark",
        EditorBg: ConsoleColor.Black, EditorFg: ConsoleColor.Gray,
        CurLineBg: ConsoleColor.DarkGray, CurLineFg: ConsoleColor.White,
        GutterFg: ConsoleColor.DarkGray, FillerFg: ConsoleColor.DarkBlue,
        SelBg: ConsoleColor.DarkBlue, SelFg: ConsoleColor.White,
        MatchBg: ConsoleColor.DarkYellow, MatchFg: ConsoleColor.Black,
        MenuBarBg: ConsoleColor.DarkBlue, MenuFg: ConsoleColor.Gray, MenuHotkeyFg: ConsoleColor.Yellow,
        MenuOpenBg: ConsoleColor.Green, MenuOpenFg: ConsoleColor.Black,
        DropBg: ConsoleColor.Black, DropFg: ConsoleColor.Gray, DropBorderFg: ConsoleColor.Gray,
        DropSelBg: ConsoleColor.Green, DropSelFg: ConsoleColor.Black, DropDimFg: ConsoleColor.DarkGray,
        StatusBg: ConsoleColor.Gray, StatusFg: ConsoleColor.Black,
        PromptBg: ConsoleColor.DarkBlue, PromptFg: ConsoleColor.White,
        ModalBg: ConsoleColor.DarkBlue, ModalFg: ConsoleColor.White,
        ModalDangerBg: ConsoleColor.DarkRed, ModalDangerFg: ConsoleColor.White,
        ModalHintFg: ConsoleColor.Gray, ModalHintDangerFg: ConsoleColor.Yellow,
        ButtonSelBg: ConsoleColor.Green, ButtonSelFg: ConsoleColor.Black,
        PickerDirFg: ConsoleColor.Cyan, PickerUpFg: ConsoleColor.DarkGray, PickerFileFg: ConsoleColor.Gray,
        PickerEmptyFg: ConsoleColor.DarkGray, PickerErrorFg: ConsoleColor.Yellow, PickerHintFg: ConsoleColor.Gray);

    /// <summary>Светлая тема.</summary>
    public static Theme Light { get; } = new(
        Name: "light",
        EditorBg: ConsoleColor.White, EditorFg: ConsoleColor.Black,
        CurLineBg: ConsoleColor.Gray, CurLineFg: ConsoleColor.Black,
        GutterFg: ConsoleColor.DarkGray, FillerFg: ConsoleColor.Gray,
        SelBg: ConsoleColor.DarkBlue, SelFg: ConsoleColor.White,
        MatchBg: ConsoleColor.DarkYellow, MatchFg: ConsoleColor.Black,
        MenuBarBg: ConsoleColor.DarkBlue, MenuFg: ConsoleColor.Gray, MenuHotkeyFg: ConsoleColor.Yellow,
        MenuOpenBg: ConsoleColor.Green, MenuOpenFg: ConsoleColor.Black,
        DropBg: ConsoleColor.White, DropFg: ConsoleColor.Black, DropBorderFg: ConsoleColor.DarkGray,
        DropSelBg: ConsoleColor.Green, DropSelFg: ConsoleColor.Black, DropDimFg: ConsoleColor.DarkGray,
        StatusBg: ConsoleColor.DarkBlue, StatusFg: ConsoleColor.White,
        PromptBg: ConsoleColor.DarkBlue, PromptFg: ConsoleColor.White,
        ModalBg: ConsoleColor.DarkBlue, ModalFg: ConsoleColor.White,
        ModalDangerBg: ConsoleColor.DarkRed, ModalDangerFg: ConsoleColor.White,
        ModalHintFg: ConsoleColor.Gray, ModalHintDangerFg: ConsoleColor.Yellow,
        ButtonSelBg: ConsoleColor.Green, ButtonSelFg: ConsoleColor.Black,
        PickerDirFg: ConsoleColor.DarkCyan, PickerUpFg: ConsoleColor.DarkGray, PickerFileFg: ConsoleColor.Black,
        PickerEmptyFg: ConsoleColor.DarkGray, PickerErrorFg: ConsoleColor.DarkRed, PickerHintFg: ConsoleColor.Gray);

    /// <summary>Тема по имени (неизвестная — тёмная).</summary>
    public static Theme Get(string? name) => name switch
    {
        "light" => Light,
        _ => Dark,
    };
}
