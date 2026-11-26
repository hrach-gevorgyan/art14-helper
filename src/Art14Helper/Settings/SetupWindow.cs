namespace Art14Helper;

/// <summary>
/// Install, update and uninstall in the settings window's style rather than message
/// boxes: first the choice (with a few options), then the work with a progress bar and
/// the outcome. Modal: <see cref="ShowDialog"/> returns when the window closes.
/// </summary>
internal sealed class SetupWindow : PaintedWindow
{
    internal enum Mode
    {
        Install,
        Update,
        Uninstall,
        Installing
    }

    internal sealed record Choices(bool StartWithWindows, bool RecommendedSettings, bool KeepSettings, bool RemoveChargeLimit = false);

    private readonly Mode _mode;
    private readonly Func<Choices, Action<string>, string>? _work;
    private readonly Surface _page;
    private bool _startWithWindows = true;
    private bool _recommended = true;
    private bool _keepSettings;
    private bool _removeChargeLimit;
    private bool _working;
    private HeadingPart? _heading;
    private TextPart? _description;
    private CardPart? _options;
    private TextPart? _status;
    private ProgressPart? _progress;
    private TextPart? _notice;
    private ButtonPart? _primary;
    private ButtonPart? _secondary;

    /// <summary>What the user went ahead with; null if the window was closed or cancelled.</summary>
    internal Choices? Result { get; private set; }

    /// <param name="work">For <see cref="Mode.Installing"/> and <see cref="Mode.Uninstall"/>: runs on a
    /// background thread, reports each step, returns the closing message, throws on failure.</param>
    internal SetupWindow(Mode mode, Func<Choices, Action<string>, string>? work = null)
    {
        _mode = mode;
        _work = work;
        _page = AddSurface();
    }

    /// <summary>Shows the window and handles its messages until it closes.</summary>
    internal void ShowDialog()
    {
        var (area, scale) = User32.MonitorAt(User32.CursorPosition);
        var size = new Size((int)(560 * scale), (int)(_mode switch { Mode.Update => 400, Mode.Uninstall => 500, _ => 470 } * scale));
        var bounds = new Rectangle(area.Left + (area.Width - size.Width) / 2, area.Top + (area.Height - size.Height) / 2, size.Width, size.Height);
        Open("Art14Helper.Setup", T("{0} setup", AppInfo.Name), User32.Caption | User32.SystemMenu | User32.MinimizeBox, bounds, scale);
        ShowAndActivate();
        if (_mode == Mode.Installing)
            UiThread.Post(() => _ = RunAsync(new Choices(true, true, false)));
        UiThread.RunUntil(() => !IsOpen);
    }

    protected override void ScaleChanged()
    {
        _heading?.Free();
        _page.Clear();
        var (title, subtitle) = _mode switch
        {
            Mode.Install => (T("Install {0}", AppInfo.Name), T("Charge limit, keyboard light, touchpad, display colors and driver updates for the MagicBook Art 14.")),
            Mode.Update => (T("Update {0}", AppInfo.Name), T("Another version is installed. Your settings stay as they are.")),
            Mode.Uninstall => (T("Uninstall {0}", AppInfo.Name), T("Removes the app, its autostart and its Start menu entry. BIOS settings stay as they are, unless you turn off the charge limit below.")),
            _ => (T("Installing {0}", AppInfo.Name), T("This takes a few seconds."))
        };
        _page.Add(_heading = new HeadingPart(title, UiFont.Strong(24, Scale), S(40), S(16), S(56)));
        _page.Add(_description = new TextPart(subtitle, UiFont.Text(14, Scale), FluentTheme.TextSecondary, wrap: true));
        _options = _mode is Mode.Install or Mode.Uninstall ? new CardPart(null, OptionRows(), Scale) { Locked = _working } : null;
        if (_options is not null)
            _page.Add(_options);
        _page.Add(_status = new TextPart(_status?.Text ?? "", UiFont.Text(14, Scale), _status?.Color ?? FluentTheme.Text, wrap: true));
        var running = _progress?.Running ?? false;
        _progress?.Dispose();
        _page.Add(_progress = new ProgressPart(Scale) { Running = running });
        _notice = _mode != Mode.Uninstall ? new TextPart(AppInfo.Disclaimer, UiFont.Text(12, Scale), FluentTheme.TextTertiary, wrap: true) : null;
        if (_notice is not null)
            _page.Add(_notice);

        var secondary = new ButtonPart(_secondary?.Text ?? T("Cancel"), primary: false, Scale, Close) { Enabled = _secondary?.Enabled ?? _mode != Mode.Installing };
        var primary = new ButtonPart(_mode switch
        {
            Mode.Install => T("Install"),
            Mode.Update => T("Update"),
            Mode.Uninstall => T("Uninstall"),
            _ => T("Open")
        }, primary: true, Scale, GoAhead) { Visible = _primary?.Visible ?? _mode != Mode.Installing };
        (_secondary, _primary) = (secondary, primary);
        _page.Add(primary);
        _page.Add(secondary);
    }

    protected override void Layout()
    {
        var client = ClientSize;
        _page.Area = new Rectangle(Point.Empty, client);
        var left = S(32);
        var width = client.Width - 2 * left;
        void Place(Part? part, int y, int height)
        {
            if (part is not null)
                part.Bounds = new Rectangle(left, y, width, height);
        }

        Place(_heading, S(28), S(56));
        Place(_description, S(96), S(48));
        var y = S(156);
        if (_options is not null)
        {
            Place(_options, y, _options.HeightFor(width));
            y += _options.Bounds.Height + S(16);
        }
        Place(_status, y, S(48));
        Place(_progress, y + S(52), S(4));
        // Too short for its content (longer text, a larger font): grow, keeping the window centred.
        var needed = y + S(60) + (_notice is null ? S(64) : S(132));
        if (client.Height < needed && User32.WindowBounds(Handle) is { IsEmpty: false } window)
        {
            var grow = needed - client.Height;
            User32.Move(Handle, new Rectangle(window.X, window.Y - grow / 2, window.Width, window.Height + grow));
            if (ClientSize.Height >= needed)
                return;   // the resize already laid everything out again
        }
        Place(_notice, client.Height - S(132), S(48));
        var buttonTop = client.Height - S(64);
        if (_secondary is not null)
            _secondary.Bounds = new Rectangle(client.Width - left - S(120), buttonTop, S(120), S(36));
        if (_primary is not null && _secondary is not null)
            _primary.Bounds = new Rectangle(_secondary.Bounds.Left - S(132), buttonTop, S(120), S(36));
    }

    protected override bool KeyPressed(int key)
    {
        if (key != User32.KeyEscape || _secondary?.Enabled != true)
            return false;
        Close();
        return true;
    }

    protected override nint? Receive(uint message, nint wParam, nint lParam)
    {
        if (message == WindowMessage.Close && _working)
            return 0;   // the work can't be stopped halfway
        if (message == WindowMessage.Destroy)
        {
            _heading?.Free();
            _progress?.Dispose();
        }
        return base.Receive(message, wParam, lParam);
    }

    private List<RowSpec> OptionRows() => _mode == Mode.Uninstall
        ?
        [
            new RowSpec
            {
                Text = T("Keep my settings"),
                Secondary = T("For installing again later. Off removes everything the app saved."),
                Kind = RowKind.Toggle,
                Checked = _keepSettings,
                OnClick = () => Flip(ref _keepSettings)
            },
            new RowSpec
            {
                Text = T("Turn off the charge limit"),
                Secondary = T("Lets the battery charge to 100% again."),
                Kind = RowKind.Toggle,
                Checked = _removeChargeLimit,
                OnClick = () => Flip(ref _removeChargeLimit)
            }
        ]
        :
        [
            new RowSpec
            {
                Text = T("Start with Windows"),
                Secondary = T("Opens in the tray when you sign in, without a UAC prompt."),
                Kind = RowKind.Toggle,
                Checked = _startWithWindows,
                OnClick = () => Flip(ref _startWithWindows)
            },
            new RowSpec
            {
                Text = T("Apply recommended settings"),
                Secondary = T("Charge limit Home, keyboard light Auto, refresh rate Auto, P3 colors with native white. All can be changed later."),
                Kind = RowKind.Toggle,
                Checked = _recommended,
                OnClick = () => Flip(ref _recommended)
            }
        ];

    private void Flip(ref bool option)
    {
        option = !option;
        if (_options is not null)
            _options.Rows = OptionRows();
    }

    private void GoAhead()
    {
        var choices = new Choices(_startWithWindows, _recommended, _keepSettings, _removeChargeLimit);
        // Install and update: the elevated copy does the work and shows its own window.
        if (_mode == Mode.Uninstall && _work is not null)
        {
            _ = RunAsync(choices);
            return;
        }
        Result = choices;
        Close();
    }

    private async Task RunAsync(Choices choices)
    {
        if (_work is null || _primary is null || _secondary is null || _status is null || _progress is null)
            return;
        _working = true;
        _primary.Visible = false;
        _secondary.Enabled = false;
        if (_options is not null)
            _options.Locked = true;
        _progress.Running = true;
        Invalidate();
        try
        {
            var message = await Task.Run(() => _work(choices, step => UiThread.Post(() => ShowStatus(step, FluentTheme.Text))));
            ShowStatus(message, FluentTheme.Text);
            Result = choices;
        }
        catch (Exception exception)
        {
            Log.Error("Setup failed", exception);
            ShowStatus(T("That didn't work: {0}", ShownMessage(exception)), FluentTheme.Danger);
        }
        _working = false;
        _progress.Running = false;
        _secondary.Text = T("Close");
        _secondary.Enabled = true;
        Invalidate();
    }

    private void ShowStatus(string text, Color color)
    {
        if (_status is null)
            return;
        _status.Text = text;
        _status.Color = color;
        Invalidate();
    }
}
