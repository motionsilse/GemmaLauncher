using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using GemmaLauncher.Core;
using static GemmaLauncher.Core.Localization;
using Forms = System.Windows.Forms;

namespace GemmaLauncher.App;

public partial class App : System.Windows.Application
{
    private LauncherViewModel? _viewModel;
    private MainWindow? _window;
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _toggle;
    private Forms.ToolStripMenuItem? _web;
    private Forms.ToolStripMenuItem? _open;
    private Forms.ToolStripMenuItem? _exit;
    private Mutex? _instance;
    private bool _ownsInstance;
    private Mutex? _legacyInstance;
    private bool _ownsLegacyInstance;
    private bool _exiting;
    private bool _hideNoticeShown;
    private uint _activateMessage;
    private uint _legacyActivateMessage;
    private LauncherPaths? _paths;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            // One launcher per Windows user in this session, independent of its copy or data folder.
            using var user = WindowsIdentity.GetCurrent();
            var identity = user.User?.Value ?? throw new Win32Exception(1332);
            _activateMessage = RegisterWindowMessage("GemmaLauncher.Activate.User." + identity);
            _instance = new Mutex(false, @"Local\GemmaLauncher.User." + identity);
            try { _ownsInstance = _instance.WaitOne(0); } catch (AbandonedMutexException) { _ownsInstance = true; }
            if (!_ownsInstance) { PostMessage(new IntPtr(0xffff), _activateMessage, IntPtr.Zero, IntPtr.Zero); Shutdown(); return; }

            var paths = new LauncherPaths(ArgumentValue(e.Args, "--data-dir"));
            _paths = paths;
            // Keep the same-data-folder activation protocol for existing 0.1.0 copies.
            var legacyIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(paths.Root.ToUpperInvariant())))[..16];
            _legacyActivateMessage = RegisterWindowMessage("GemmaLauncher.Activate." + legacyIdentity);
            _legacyInstance = new Mutex(false, @"Local\GemmaLauncher." + legacyIdentity);
            try { _ownsLegacyInstance = _legacyInstance.WaitOne(0); } catch (AbandonedMutexException) { _ownsLegacyInstance = true; }
            if (!_ownsLegacyInstance) { PostMessage(new IntPtr(0xffff), _legacyActivateMessage, IntPtr.Zero, IntPtr.Zero); Shutdown(); return; }
            var store = new SettingsStore(paths);
            var settings = store.Load();
            Core.Localization.Current.SetLanguage(settings.LanguagePreference);
            var catalog = CatalogLoader.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "catalog.json"));
            for (var i = 0; i + 1 < e.Args.Length; i++)
                if (e.Args[i] == "--model-folder" && System.IO.Directory.Exists(e.Args[i + 1]) && !settings.ModelFolders.Contains(e.Args[i + 1], StringComparer.OrdinalIgnoreCase)) settings.ModelFolders.Add(System.IO.Path.GetFullPath(e.Args[i + 1]));
            _viewModel = new(catalog, paths, settings, store);
            _window = new MainWindow { DataContext = _viewModel };
            MainWindow = _window;
            _window.HideRequested += (_, _) => HideToTray();
            _window.ExitRequested += async (_, _) => await ExitAsync();
            _window.Closing += WindowClosing;
            _window.SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(_window).Handle)?.AddHook(WindowMessage);
            CreateTray();
            _viewModel.SessionChanged += (_, _) => UpdateTray();
            UpdateTray();
            _window.Show();
        }
        catch (Exception ex)
        {
            try
            {
                if (_paths is not null)
                {
                    System.IO.Directory.CreateDirectory(_paths.Logs);
                    System.IO.File.AppendAllText(System.IO.Path.Combine(_paths.Logs, "launcher.log"), $"{DateTimeOffset.Now:O} {ex}\n");
                }
            }
            catch (Exception logError) when (logError is System.IO.IOException or UnauthorizedAccessException) { }
            System.Windows.MessageBox.Show(T("ui.error.appStart", ex.Message), T("ui.app.shortTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static string? ArgumentValue(string[] args, string key)
    {
        var index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        _open = new Forms.ToolStripMenuItem(T("ui.tray.open"), null, (_, _) => ShowWindow());
        menu.Items.Add(_open);
        _toggle = new Forms.ToolStripMenuItem(T("ui.action.start"), null, (_, _) => _viewModel?.PrimaryCommand.Execute(null));
        _web = new Forms.ToolStripMenuItem(T("ui.connection.web"), null, (_, _) => _viewModel?.OpenWebUiCommand.Execute(null));
        menu.Items.Add(_toggle); menu.Items.Add(_web); menu.Items.Add(new Forms.ToolStripSeparator());
        _exit = new Forms.ToolStripMenuItem(T("ui.tray.exit"), null, async (_, _) => await ExitAsync());
        menu.Items.Add(_exit);
        var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application;
        _tray = new Forms.NotifyIcon { Icon = icon, Text = T("ui.app.shortTitle"), ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => ShowWindow();
    }
    private void UpdateTray()
    {
        if (_tray is null || _viewModel is null || _toggle is null || _web is null) return;
        var text = T("ui.app.shortTitle") + " · " + _viewModel.StatusLabel;
        _tray.Text = text.Length > 63 ? text[..63] : text;
        _toggle.Text = _viewModel.PrimaryLabel;
        _toggle.Enabled = !_exiting && _viewModel.PrimaryCommand.CanExecute(null);
        _web.Enabled = !_exiting && _viewModel.CanOpenWebUi;
        _web.Text = T("ui.connection.web");
        if (_open is not null) _open.Text = T("ui.tray.open");
        if (_exit is not null) _exit.Text = T("ui.tray.exit");
    }
    private void HideToTray()
    {
        _window?.Hide();
        if (!_hideNoticeShown && _tray is not null)
        {
            _hideNoticeShown = true;
            _tray.ShowBalloonTip(3000, T("ui.app.shortTitle"), T("ui.tray.notice"), Forms.ToolTipIcon.Info);
        }
    }
    private void WindowClosing(object? sender, CancelEventArgs e) { if (!_exiting) { e.Cancel = true; HideToTray(); } }
    private void ShowWindow()
    {
        if (_exiting || _window is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }
    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == _activateMessage || (uint)message == _legacyActivateMessage) { ShowWindow(); handled = true; }
        return IntPtr.Zero;
    }
    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true; UpdateTray();
        try { if (_viewModel is not null) await _viewModel.DisposeAsync(); }
        catch (Exception ex) { System.Windows.MessageBox.Show(T("ui.error.appExit", ex.Message), T("ui.app.shortTitle"), MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { _tray!.Visible = false; _window?.Close(); Shutdown(); }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        if (_ownsLegacyInstance) _legacyInstance?.ReleaseMutex();
        _legacyInstance?.Dispose();
        if (_ownsInstance) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
