using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using GemmaLauncher.Core;
using static GemmaLauncher.Core.Localization;

namespace GemmaLauncher.App;

public sealed class App : ApplicationContext
{
    private LauncherViewModel? _viewModel;
    private MainWindow? _window;
    private NotifyIcon? _tray;
    private System.Drawing.Icon? _icon;
    private ContextMenuStrip? _menu;
    private ToolStripMenuItem? _toggle, _web, _open, _exit;
    private Mutex? _instance, _legacyInstance;
    private bool _ownsInstance, _ownsLegacyInstance, _exiting, _exitComplete, _hideNoticeShown;
    public bool Ready { get; private set; }

    public App(string[] args)
    {
        LauncherPaths? paths = null;
        try
        {
            using var user = WindowsIdentity.GetCurrent();
            var identity = user.User?.Value ?? throw new Win32Exception(1332);
            var activateMessage = RegisterWindowMessage("GemmaLauncher.Activate.User." + identity);
            _instance = new Mutex(false, @"Local\GemmaLauncher.User." + identity);
            _ownsInstance = Acquire(_instance);
            if (!_ownsInstance) { PostMessage(new IntPtr(0xffff), activateMessage, IntPtr.Zero, IntPtr.Zero); return; }

            paths = new LauncherPaths(ArgumentValue(args, "--data-dir"));
            var legacyIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(paths.Root.ToUpperInvariant())))[..16];
            var legacyActivateMessage = RegisterWindowMessage("GemmaLauncher.Activate." + legacyIdentity);
            _legacyInstance = new Mutex(false, @"Local\GemmaLauncher." + legacyIdentity);
            _ownsLegacyInstance = Acquire(_legacyInstance);
            if (!_ownsLegacyInstance) { PostMessage(new IntPtr(0xffff), legacyActivateMessage, IntPtr.Zero, IntPtr.Zero); return; }

            // Establish the UI context before starting any asynchronous model scan.
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var store = new SettingsStore(paths);
            var settings = store.Load();
            Core.Localization.Current.SetLanguage(settings.LanguagePreference);
            for (var i = 0; i + 1 < args.Length; i++)
                if (args[i] == "--model-folder" && Directory.Exists(args[i + 1]) && !settings.ModelFolders.Contains(args[i + 1], StringComparer.OrdinalIgnoreCase))
                    settings.ModelFolders.Add(Path.GetFullPath(args[i + 1]));
            _viewModel = new(BundledResources.LoadCatalog(), paths, settings, store);
            _window = new MainWindow(_viewModel, activateMessage, legacyActivateMessage);
            MainForm = _window;
            _window.ActivationRequested += (_, _) => ShowWindow();
            _window.ExitRequested += async (_, _) => await ExitAsync();
            _window.FormClosing += WindowClosing;
            CreateTray();
            _viewModel.SessionChanged += UpdateTray;
            UpdateTray(this, EventArgs.Empty);
            _window.Show();
            Ready = true;
        }
        catch (Exception exception)
        {
            try
            {
                if (paths is not null)
                {
                    Directory.CreateDirectory(paths.Logs);
                    File.AppendAllText(Path.Combine(paths.Logs, "launcher.log"), $"{DateTimeOffset.Now:O} {exception}\n");
                }
            }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
            Dispose(); throw;
        }
    }

    private static bool Acquire(Mutex mutex)
    {
        try { return mutex.WaitOne(0); } catch (AbandonedMutexException) { return true; }
    }

    private static string? ArgumentValue(string[] args, string key)
    {
        var index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private void CreateTray()
    {
        _menu = new ContextMenuStrip();
        _open = new(T("ui.tray.open"), null, (_, _) => ShowWindow());
        _toggle = new(T("ui.action.start"), null, (_, _) => _viewModel?.PrimaryCommand.Execute(null));
        _web = new(T("ui.connection.web"), null, (_, _) => _viewModel?.OpenWebUiCommand.Execute(null));
        _exit = new(T("ui.tray.exit"), null, async (_, _) => await ExitAsync());
        _menu.Items.AddRange([_open, _toggle, _web, new ToolStripSeparator(), _exit]);
        _icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        _tray = new NotifyIcon { Icon = _icon, Text = T("ui.app.shortTitle"), ContextMenuStrip = _menu, Visible = true };
        _tray.DoubleClick += (_, _) => ShowWindow();
    }

    private void UpdateTray(object? sender, EventArgs args)
    {
        if (_tray is null || _viewModel is null) return;
        var text = T("ui.app.shortTitle") + " · " + _viewModel.StatusLabel;
        _tray.Text = text.Length > 63 ? text[..63] : text;
        _toggle!.Text = _viewModel.PrimaryLabel;
        _toggle.Enabled = !_exiting && _viewModel.PrimaryCommand.CanExecute(null);
        _web!.Enabled = !_exiting && _viewModel.CanOpenWebUi;
        _web.Text = T("ui.connection.web");
        _open!.Text = T("ui.tray.open");
        _exit!.Text = T("ui.tray.exit");
        _exit.Enabled = !_exiting;
    }

    private void WindowClosing(object? sender, FormClosingEventArgs e)
    {
        if (_exitComplete) return;
        e.Cancel = true;
        if (_exiting) return;
        if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing) { _ = ExitAsync(); return; }
        _window!.Hide();
        if (!_hideNoticeShown && _tray is not null)
        {
            _hideNoticeShown = true;
            _tray.ShowBalloonTip(3000, T("ui.app.shortTitle"), T("ui.tray.notice"), ToolTipIcon.Info);
        }
    }

    private void ShowWindow()
    {
        if (_exiting || _window is null) return;
        _window.Show();
        if (_window.WindowState == FormWindowState.Minimized) _window.WindowState = FormWindowState.Normal;
        _window.Activate();
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        if (_window is not null) _window.Enabled = false;
        UpdateTray(this, EventArgs.Empty);
        try { if (_viewModel is not null) await _viewModel.DisposeAsync(); }
        catch (Exception ex) { MessageBox.Show(T("ui.error.appExit", ex.Message), T("ui.app.shortTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally
        {
            _exitComplete = true;
            if (_tray is not null) _tray.Visible = false;
            _window?.Close();
            ExitThread();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_viewModel is not null) _viewModel.SessionChanged -= UpdateTray;
            _tray?.Dispose(); _menu?.Dispose(); _icon?.Dispose(); _window?.Dispose();
            if (_ownsLegacyInstance) { _legacyInstance?.ReleaseMutex(); _ownsLegacyInstance = false; }
            _legacyInstance?.Dispose(); _legacyInstance = null;
            if (_ownsInstance) { _instance?.ReleaseMutex(); _ownsInstance = false; }
            _instance?.Dispose(); _instance = null;
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
