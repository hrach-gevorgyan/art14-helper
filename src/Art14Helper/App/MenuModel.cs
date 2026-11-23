namespace Art14Helper;

/// <summary>
/// What the menu builders write to. The tray menu (<see cref="TrayMenu"/>)
/// and the settings window (<see cref="MenuModel"/>) are filled by the same
/// builders, so both always offer the same choices with the same logic.
/// </summary>
internal interface IMenuBuilder
{
    IMenuBuilder AddSubMenu(string text, string? tooltip = null);

    /// <param name="toggle">An on/off setting: drawn as a switch in the window.</param>
    int AddItem(string text, Action? onClick, bool enabled = true, bool @checked = false,
        string? tooltip = null, bool toggle = false);

    void AddSeparator();
}

internal enum MenuNodeKind
{
    Item,
    Separator,
    SubMenu
}

internal sealed class MenuNode
{
    internal MenuNodeKind Kind { get; init; }
    internal string Text { get; init; } = "";
    internal string? Tooltip { get; init; }
    internal Action? Action { get; init; }
    internal bool Enabled { get; init; } = true;
    internal bool Checked { get; init; }
    internal bool Toggle { get; init; }
    internal MenuModel? Children { get; init; }

    /// <summary>A disabled item without an action: a section title in the menu.</summary>
    internal bool IsHeader => Kind == MenuNodeKind.Item && Action is null && !Enabled;
}

/// <summary>The menu as data, for the settings window.</summary>
internal sealed class MenuModel : IMenuBuilder
{
    internal List<MenuNode> Nodes { get; } = [];

    public IMenuBuilder AddSubMenu(string text, string? tooltip = null)
    {
        var children = new MenuModel();
        Nodes.Add(new MenuNode { Kind = MenuNodeKind.SubMenu, Text = text, Tooltip = tooltip, Children = children });
        return children;
    }

    public int AddItem(string text, Action? onClick, bool enabled = true, bool @checked = false,
        string? tooltip = null, bool toggle = false)
    {
        Nodes.Add(new MenuNode
        {
            Kind = MenuNodeKind.Item,
            Text = text,
            Action = onClick,
            Enabled = enabled,
            Checked = @checked,
            Tooltip = tooltip,
            Toggle = toggle
        });
        return Nodes.Count;
    }

    public void AddSeparator() => Nodes.Add(new MenuNode { Kind = MenuNodeKind.Separator });
}
