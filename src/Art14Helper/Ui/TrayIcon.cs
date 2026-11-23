namespace Art14Helper.Ui;

/// <summary>
/// The app's entry in the notification area. Mouse events arrive at the owner window as
/// <paramref name="callbackMessage"/> with the mouse message in lParam. If Explorer
/// restarts, the owner calls <see cref="Add"/> again on <see cref="TaskbarCreated"/>.
/// </summary>
internal sealed class TrayIcon(IntPtr window, uint callbackMessage, Icon icon, string tip) : IDisposable
{
    /// <summary>Broadcast by Explorer when the taskbar (re)appears.</summary>
    internal static readonly uint TaskbarCreated = User32.RegisterWindowMessageW("TaskbarCreated");

    private Icon _icon = icon;
    private string _tip = Fit(tip);
    private bool _added;

    internal Icon Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(value, _icon))
                return;
            _icon = value;
            Update();
        }
    }

    internal string Tip
    {
        get => _tip;
        set
        {
            var text = Fit(value);
            if (text == _tip)
                return;
            _tip = text;
            Update();
        }
    }

    private static string Fit(string text) => text.Length <= IconApi.TrayTipLength ? text : text[..(IconApi.TrayTipLength - 1)] + "…";

    /// <summary>
    /// Puts the icon in the tray; fails quietly while there is no taskbar yet (early at sign-in).
    /// When the entry is there already, it is updated instead, so the state never sticks at
    /// "not added" (every later change would then fail too).
    /// </summary>
    internal void Add()
        => _added = IconApi.SetTrayEntry(window, callbackMessage, _icon.Handle, _tip, update: false)
                    || IconApi.SetTrayEntry(window, callbackMessage, _icon.Handle, _tip, update: true);

    /// <summary>Explorer restarted: added again, with the icon for its (maybe new) size, in one step.</summary>
    internal void Readd(Icon icon)
    {
        _icon = icon;
        Add();
    }

    private void Update()
    {
        if (!_added || !IconApi.SetTrayEntry(window, callbackMessage, _icon.Handle, _tip, update: true))
            Add();
    }

    public void Dispose()
    {
        IconApi.RemoveTrayEntry(window);   // also when the last change failed: no dead icon after exit
        _added = false;
    }
}
