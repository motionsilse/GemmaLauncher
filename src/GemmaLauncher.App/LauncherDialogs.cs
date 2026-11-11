using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GemmaLauncher.Core;
using static GemmaLauncher.Core.Localization;

namespace GemmaLauncher.App;

// Every owned dialog keeps control of the same server session, including while an editor draft is open.
public sealed class LauncherDialog : Form
{
    private readonly UiBindings _bindings;
    public Panel Content { get; } = new() { Dock = DockStyle.Fill, AutoScroll = true };
    public FlowLayoutPanel Actions { get; } = new() { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = true, Padding = new Padding(0, 8, 0, 0) };
    public LauncherDialog(LauncherViewModel vm, string title, Size size, bool showSessionControls = true)
    {
        Text = title; Size = size; MinimumSize = new Size(360, 240); StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96); Ui.SetFont(this, Ui.BodyFontSize);
        BackColor = Color.White; ForeColor = Ui.Ink; MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
        _bindings = new(this, vm);
        void WrapMessages()
        {
            foreach (var label in Content.Controls.OfType<Label>().Where(label => label.AutoSize))
                label.MaximumSize = new Size(Math.Max(1, Content.ClientSize.Width - label.Margin.Horizontal), 0);
        }
        Content.SizeChanged += (_, _) => WrapMessages(); Content.ControlAdded += (_, _) => WrapMessages();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(20) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var running = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Ui.Tint, Padding = new Padding(10), Margin = new Padding(0, 0, 0, 14) };
        running.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); running.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var status = Ui.Label(); _bindings.Text(status, () => vm.SelectedModel?.DisplayName + "\n" + vm.StatusLabel); running.Controls.Add(status, 0, 0);
        var stop = Ui.Button(); stop.Name = "DialogSessionAction"; _bindings.Text(stop, () => vm.PrimaryLabel); _bindings.Command(stop, vm.PrimaryCommand); running.Controls.Add(stop, 1, 0);
        _bindings.Visible(running, () => showSessionControls && (vm.IsBusy || vm.IsServerRunning));
        root.Controls.Add(running, 0, 0); root.Controls.Add(Content, 0, 1); root.Controls.Add(Actions, 0, 2); Controls.Add(root); _bindings.Start();
        if (showSessionControls && vm.BackgroundOperationsFactory is not null) { running.Visible = false; root.Controls.Remove(running); Disposed += (_, _) => running.Dispose(); root.Controls.Add(vm.BackgroundOperationsFactory(), 0, 0); }
    }
    public Button AddAction(string text, DialogResult result, bool primary = false)
    {
        var button = Ui.Button(); button.Text = text; button.DialogResult = result;
        if (primary) Ui.Primary(button); Actions.Controls.Add(button);
        if (result == DialogResult.Cancel) CancelButton = button;
        return button;
    }
    protected override void OnShown(EventArgs e) { WindowLayout.FitToWorkArea(this, Screen.FromControl(this).WorkingArea); base.OnShown(e); }
    protected override void Dispose(bool disposing) { if (disposing) _bindings.Dispose(); base.Dispose(disposing); }
}

internal static class LauncherDialogs
{
    public static async Task<string?> AnisAccess(LauncherViewModel vm, IWin32Window owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var clientId = LauncherViewModel.HuggingFaceClientId;
        using var dialog = new LauncherDialog(vm, T("ui.anis.firstUse"), new Size(620, 360), showSessionControls: false);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var stack = Ui.Stack();
        stack.Padding = Padding.Empty;
        dialog.Content.Controls.Add(Ui.Scroll(stack));
        var status = Ui.Label(T("engine.install.verifying", "ANIS"));
        status.Name = "AnisDownloadStatus";
        Ui.SetFont(status, Ui.BodyFontSize, FontStyle.Bold);
        Ui.Add(stack, status);
        var transfer = Ui.Label(); transfer.Name = "AnisDownloadProgressText";
        Ui.Add(stack, transfer);
        var bar = new ProgressBar { Name = "AnisDownloadProgress", Height = 10, AccessibleName = T("ui.progress.name") };
        Ui.Add(stack, bar);
        var source = new LinkLabel { AutoSize = true, Text = T("ui.anis.officialAccess"), LinkColor = Ui.Green };
        source.LinkClicked += (_, _) => Open(new Uri(AnisBackend.ModelPage));
        Ui.Add(stack, source);
        var folder = DownloadsFolder();
        var choose = Ui.Button();
        choose.Text = T("ui.anis.downloadFolder");
        Ui.Add(stack, choose);
        choose.Anchor = AnchorStyles.Left;
        var retry = dialog.AddAction(T("ui.anis.login"), DialogResult.None, true);
        retry.Name = "AnisDownloadRetry";
        var reconnect = new LinkLabel { AutoSize = true, Text = T("ui.anis.login"), LinkColor = Ui.Green };
        Ui.Add(stack, reconnect);
        Task? monitor = null;
        choose.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog { Description = choose.Text, SelectedPath = folder };
            if (picker.ShowDialog(dialog) != DialogResult.OK) return;
            folder = picker.SelectedPath;
            Prepare(signIn: false);
        };
        retry.Click += (_, _) => Prepare(signIn: true);
        reconnect.LinkClicked += (_, _) =>
        {
            if (monitor is { IsCompleted: false }) return;
            vm.ClearAnisAuthorization();
            Prepare(signIn: true);
        };
        dialog.AddAction(T("ui.preset.cancel"), DialogResult.Cancel);
        dialog.Shown += (_, _) => Prepare(signIn: true);
        _ = dialog.Handle;
        using var cancellation = token.Register(() =>
        {
            try { if (!dialog.IsDisposed) dialog.BeginInvoke(() => { if (!dialog.IsDisposed) { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); } }); }
            catch (InvalidOperationException) when (dialog.IsDisposed || !dialog.IsHandleCreated) { }
        });
        var result = dialog.ShowDialog(owner);
        lifetime.Cancel();
        if (monitor is not null) await monitor;
        token.ThrowIfCancellationRequested();
        return result == DialogResult.OK ? vm.AnisManagedFolder : null;

        void Prepare(bool signIn)
        {
            if (monitor is { IsCompleted: false }) return;
            monitor = PrepareAsync(signIn);
        }

        async Task PrepareAsync(bool signIn)
        {
            retry.Enabled = choose.Enabled = false;
            reconnect.Visible = source.Visible = false;
            transfer.Visible = bar.Visible = false;
            var accessDenied = false;
            var reportProgress = true;
            try
            {
                status.Text = T("engine.install.verifying", "ANIS");
                var missing = await vm.ImportAnisDownloadsAsync(folder, lifetime.Token);
                if (missing.Count == 0)
                {
                    vm.ClearAnisAuthorization();
                    dialog.DialogResult = DialogResult.OK;
                    return;
                }
                var total = AnisBackend.RuntimeModelFiles.Count;
                status.Text = T("ui.anis.downloadProgress", total - missing.Count, total);
                if (!signIn) return;
                if (string.IsNullOrWhiteSpace(clientId))
                {
                    status.Text = T("ui.anis.loginFailed");
                    return;
                }

                status.Text = vm.HasAnisAuthorization ? T("engine.install.connecting", "Hugging Face") : T("ui.anis.awaitingApproval");
                var progress = new Progress<InstallationProgress>(p =>
                {
                    if (!reportProgress || lifetime.IsCancellationRequested) return;
                    status.Text = p.Message;
                    transfer.Text = Ui.TransferProgressText(p);
                    transfer.Visible = bar.Visible = transfer.Text.Length > 0;
                    bar.Value = (int)(p.Percent ?? 0);
                });
                await vm.DownloadAnisWithLoginAsync(clientId, progress, lifetime.Token);
                dialog.DialogResult = DialogResult.OK;
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or InvalidDataException or TimeoutException or UnauthorizedAccessException or HttpRequestException or System.ComponentModel.Win32Exception)
            {
                accessDenied = error is AnisAccessRequiredException;
                status.Text = accessDenied ? T("ui.anis.oauthAccessDenied") : error.Message;
            }
            finally
            {
                reportProgress = false;
                transfer.Visible = bar.Visible = false;
                source.Visible = accessDenied;
                reconnect.Visible = accessDenied && vm.HasAnisAuthorization;
                retry.Text = T(vm.HasAnisAuthorization ? "ui.anis.retryDownload" : "ui.anis.login");
                retry.Enabled = choose.Enabled = true;
            }
        }

        void Open(Uri url)
        {
            try { vm.OpenAnisDownload(url); }
            catch (System.ComponentModel.Win32Exception ex) { status.Text = T("ui.note.browserFailed", ex.Message); }
        }
    }

    private static string DownloadsFolder()
    {
        var downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        var result = SHGetKnownFolderPath(in downloads, 0, IntPtr.Zero, out var path);
        try
        {
            if (result == 0 && Marshal.PtrToStringUni(path) is { Length: > 0 } folder) return folder;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
        finally { Marshal.FreeCoTaskMem(path); }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(in Guid folder, uint flags, IntPtr token, out IntPtr path);
    public static PresetEditDecision ResolvePreset(LauncherViewModel vm, IWin32Window owner)
    {
        using var dialog = new LauncherDialog(vm, T("ui.preset.changesTitle"), new Size(580, 320));
        var message = Ui.Label(T("ui.preset.changesHint") + (vm.IsPresetShared ? "\n\n" + vm.PresetScopeLabel : "")); dialog.Content.Controls.Add(message);
        dialog.AddAction(T("ui.library.save"), DialogResult.Yes, true);
        dialog.AddAction(T("ui.preset.discard"), DialogResult.No);
        dialog.AddAction(T("ui.preset.keepEditing"), DialogResult.Cancel);
        return dialog.ShowDialog(owner) switch { DialogResult.Yes => PresetEditDecision.Save, DialogResult.No => PresetEditDecision.Discard, _ => PresetEditDecision.Cancel };
    }
    public static bool DeletePreset(LauncherViewModel vm, IWin32Window owner, string name)
    {
        using var dialog = new LauncherDialog(vm, T("ui.preset.deleteTitle", name), new Size(580, 300));
        dialog.Content.Controls.Add(Ui.Label(T("ui.preset.deleteTitle", name) + "\n\n" + T("ui.preset.deleteHint") + (vm.IsPresetShared ? "\n\n" + vm.PresetScopeLabel : "")));
        dialog.AddAction(T("ui.library.deletePreset"), DialogResult.Yes);
        dialog.AddAction(T("ui.preset.cancel"), DialogResult.Cancel);
        return dialog.ShowDialog(owner) == DialogResult.Yes;
    }
}
