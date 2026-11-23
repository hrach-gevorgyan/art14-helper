namespace Art14Helper;

/// <summary>
/// The tray icon's right-click menu: a plain Win32 popup menu (so it looks like Windows'
/// own tray menus and follows the dark theme), filled by the same builders as the
/// settings window. Descriptions are looked up by <see cref="TipFor"/> while an item is
/// highlighted, because popup menus have no tooltips.
/// </summary>
internal sealed class TrayMenu : IMenuBuilder, IDisposable
{
    // Item ids travel in the low 16 bits of WM_MENUSELECT.
    private const int FirstId = 1, LastId = 0xEFFF;

    /// <summary>What the menu and all its submenus share.</summary>
    private sealed class Items
    {
        internal readonly Dictionary<int, Action> Actions = [];
        internal readonly Dictionary<int, string> Tips = [];
        internal readonly Dictionary<(IntPtr Menu, int Position), string> SubMenuTips = [];
        internal int NextId = FirstId;
    }

    private readonly Items _items;
    private readonly bool _isRoot;

    internal IntPtr Handle { get; }

    internal TrayMenu() : this(new Items(), isRoot: true)
    {
    }

    private TrayMenu(Items items, bool isRoot)
    {
        _items = items;
        _isRoot = isRoot;
        Handle = PopupMenus.Create();
    }

    public IMenuBuilder AddSubMenu(string text, string? tooltip = null)
    {
        var position = PopupMenus.ItemCount(Handle);
        var sub = new TrayMenu(_items, isRoot: false);
        PopupMenus.AddSubMenu(Handle, sub.Handle, text);
        if (tooltip is not null)
            _items.SubMenuTips[(Handle, position)] = tooltip;
        return sub;
    }

    public int AddItem(string text, Action? onClick, bool enabled = true, bool @checked = false, string? tooltip = null, bool toggle = false)
    {
        var id = _items.NextId;
        _items.NextId = id >= LastId ? FirstId : id + 1;
        if (onClick is not null)
            _items.Actions[id] = onClick;
        if (tooltip is not null)
            _items.Tips[id] = tooltip;
        PopupMenus.AddItem(Handle, id, text, enabled, @checked);
        return id;
    }

    public void AddSeparator() => PopupMenus.AddSeparator(Handle);

    /// <summary>Shows the menu at the pointer and waits; returns what the chosen item does, or null.</summary>
    internal Action? Show(IntPtr owner)
    {
        var at = User32.CursorPosition;
        var id = PopupMenus.Track(Handle, owner, at.X, at.Y);
        return _items.Actions.GetValueOrDefault(id);
    }

    /// <summary>The description of the highlighted entry: a submenu by its position, an item by its id.</summary>
    internal string? TipFor(IntPtr menu, int idOrPosition, bool isSubMenu)
        => isSubMenu ? _items.SubMenuTips.GetValueOrDefault((menu, idOrPosition)) : _items.Tips.GetValueOrDefault(idOrPosition);

    public void Dispose()
    {
        // Destroying the root also destroys every submenu in it.
        if (_isRoot)
            PopupMenus.Destroy(Handle);
    }
}
