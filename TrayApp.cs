using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WordBackspace;

class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly WordStore _store;
    private readonly AppSettings _appSettings;
    private readonly Matcher _matcher;
    private readonly KeyboardHook _hook;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly System.Windows.Forms.Timer _themeTimer;
    private readonly Form _dispatcher;
    private SettingsForm? _settings;

    public TrayApp()
    {
        Logger.Info($"starting pid={Environment.ProcessId} exe={Environment.ProcessPath}");

        _store = new WordStore();
        _appSettings = new AppSettings();
        _matcher = new Matcher(_store, _appSettings);
        _hook = new KeyboardHook();
        _hook.Filter = _matcher.HandleKey;
        _hook.DeletionRequested = _matcher.ExecuteDeletion;
        _matcher.DeletionRequested = (count) => _hook.RequestDeletion(count);

        _dispatcher = new Form
        {
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new System.Drawing.Point(-10000, -10000),
            Size = new System.Drawing.Size(1, 1),
        };
        // Force handle creation: BeginInvoke requires it, and CreateControl() alone
        // does not create a Form's handle on .NET.
        var _ = _dispatcher.Handle;

        var menu = new ContextMenuStrip
        {
            // Let Windows draw the menu: the native menu follows the system
            // light/dark theme automatically (no manual theming to keep in
            // sync).
            RenderMode = ToolStripRenderMode.System,
        };

        var header = new ToolStripMenuItem("WordBackspace")
        {
            Enabled = false,
            Font = new Font("Segoe UI", 9.75f, FontStyle.Bold),
            ForeColor = SystemColors.GrayText,
        };
        menu.Items.Add(header);
        menu.Items.Add(new ToolStripSeparator());

        _settingsItem = new ToolStripMenuItem("Settings...", null, (_, _) => ToggleSettings())
        {
            Image = Glyph('\uE713'),
        };
        menu.Items.Add(_settingsItem);

        _pauseItem = new ToolStripMenuItem("Pause", null, (_, _) => SetPaused(!_matcher.Paused))
        {
            Image = Glyph('\uE769'),
        };
        menu.Items.Add(_pauseItem);

        menu.Items.Add(new ToolStripSeparator());

        _startupItem = new ToolStripMenuItem("Launch at startup") { Checked = StartupRegistry.IsEnabled() };
        _startupItem.Click += (_, _) =>
        {
            bool value = !_startupItem.Checked;
            _startupItem.Checked = value;
            StartupRegistry.SetEnabled(value);
            Logger.Info($"launch at startup = {value}");
        };
        menu.Items.Add(_startupItem);

        menu.Items.Add(new ToolStripSeparator());

        _exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApp())
        {
            Image = Glyph('\uE8BB'),
        };
        menu.Items.Add(_exitItem);

        _tray = new NotifyIcon
        {
            Icon = Ui.AppIcon(),
            Text = "WordBackspace",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ToggleSettings();

        // The system theme is polled: switching Windows between light and
        // dark re-themes the app within a half minute, no restart needed.
        _themeTimer = new System.Windows.Forms.Timer { Interval = 30000 };
        _themeTimer.Tick += (_, _) =>
        {
            if (!Theme.CheckForChange()) return;
            RefreshMenuIcons();
            if (_settings != null && !_settings.IsDisposed)
                _settings.ApplyTheme();
        };
        _themeTimer.Start();

        _hook.Start();
        Logger.Info("ready");
    }

    private void ToggleSettings()
    {
        if (_settings == null || _settings.IsDisposed)
        {
            _settings = new SettingsForm(_store, _appSettings);
            _matcher.ExcludedWindow = _settings.Handle;
            Logger.Info($"settings window created, hwnd={_settings.Handle}");
        }

        if (_settings.Visible)
        {
            _settings.Hide();
            Logger.Info("settings hidden");
        }
        else
        {
            _settings.Show();
            ForceForeground(_settings.Handle);
            _settings.Activate();
            _settings.FocusAddBox();
            Logger.Info($"settings shown, foreground={NativeMethods.GetForegroundWindow()}");
        }
    }

    private static void ForceForeground(IntPtr hwnd)
    {
        IntPtr fg = NativeMethods.GetForegroundWindow();
        if (fg == hwnd) return;

        uint fgThread = NativeMethods.GetWindowThreadProcessId(fg, out _);
        uint curThread = NativeMethods.GetCurrentThreadId();

        if (fgThread != 0 && curThread != 0 && fgThread != curThread)
        {
            NativeMethods.AttachThreadInput(curThread, fgThread, true);
            NativeMethods.SetForegroundWindow(hwnd);
            NativeMethods.AttachThreadInput(curThread, fgThread, false);
        }
        else
        {
            NativeMethods.SetForegroundWindow(hwnd);
        }
    }

    private void SetPaused(bool paused)
    {
        _matcher.Paused = paused;
        if (!paused) _matcher.ClearBuffer();
        _pauseItem.Text = paused ? "Resume" : "Pause";
        _pauseItem.Image = paused ? Glyph('\uE768') : Glyph('\uE769');
        Logger.Info($"paused={paused}");
    }

    private static Image Glyph(char code) => Ui.Glyph(code, 16);

    // Menu icon glyphs are baked bitmaps, so they are regenerated when the
    // theme changes (dark gray icons do not show on a dark menu).
    private void RefreshMenuIcons()
    {
        _settingsItem.Image = Glyph('\uE713');
        _pauseItem.Image = Glyph(_matcher.Paused ? '\uE768' : '\uE769');
        _exitItem.Image = Glyph('\uE8BB');
    }

    private void ExitApp()
    {
        Logger.Info("exiting");
        _themeTimer.Dispose();
        _hook.Dispose();
        _settings?.Close();
        _dispatcher.Close();
        _tray.Visible = false;
        _tray.Dispose();
        Application.Exit();
    }
}