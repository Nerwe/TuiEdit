namespace TuiEdit;

/// <summary>
/// Панель сплит-вида: свой список вкладок, активная вкладка и скролл
/// строки вкладок. Состояние вида вкладок живёт в <see cref="DocTab"/>;
/// поля редактора — кэш активной вкладки активной панели.
/// </summary>
internal sealed class Pane
{
    public List<DocTab> Docs { get; } = new();

    public int Active;

    public int TabLeft;

    public Pane(DocTab first)
    {
        Docs.Add(first);
    }
}
