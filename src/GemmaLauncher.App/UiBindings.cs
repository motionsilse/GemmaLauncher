using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Input;
using static GemmaLauncher.Core.Localization;

namespace GemmaLauncher.App;

// Explicit bindings keep native controls small and make rejected selections reversible.
internal sealed class UiBindings(Control owner, LauncherViewModel viewModel) : IDisposable
{
    private readonly List<Action> _updates = [];
    private Action? _memoryUpdate;
    private bool _refreshing, _queued;
    public void Start() { viewModel.PropertyChanged += Changed; Refresh(); }
    public void Dispose() => viewModel.PropertyChanged -= Changed;
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (owner.IsDisposed) return;
        // A telemetry sample has one consumer. Avoid reformatting every selector and
        // laying out every created page on each two-second sample.
        if (e.PropertyName == nameof(LauncherViewModel.MemoryUsage)) { _memoryUpdate?.Invoke(); return; }
        RequestRefresh();
    }
    public void RequestRefresh()
    {
        if (_queued || owner.IsDisposed) return;
        if (!owner.IsHandleCreated) { Refresh(); return; }
        _queued = true;
        owner.BeginInvoke(() => { _queued = false; if (!owner.IsDisposed) Refresh(); });
    }
    public void Refresh()
    {
        if (_refreshing || owner.IsDisposed) return;
        _refreshing = true;
        owner.SuspendLayout();
        try { foreach (var update in _updates) update(); }
        finally { owner.ResumeLayout(true); _refreshing = false; }
    }
    public void Watch(Action update) => _updates.Add(update);
    public void Text(Control control, Func<string> value) => Watch(() => { var text = value(); if (control.Text != text) control.Text = text; });
    public void MemoryText(Control control, Func<string>? format = null)
    {
        _memoryUpdate += () => { var text = format?.Invoke() ?? viewModel.MemoryUsage; if (control.Text != text) control.Text = text; };
        Text(control, () => format?.Invoke() ?? viewModel.MemoryUsage);
    }
    public void Localize(Control control, string key)
    {
        if (control is ButtonBase button) button.UseMnemonic = false;
        Text(control, () => control is TabPage ? T(key).Replace("&", "&&", StringComparison.Ordinal) : T(key));
        Watch(() => control.AccessibleName = T(key));
    }
    public void Enabled(Control control, Func<bool> value) => Watch(() => control.Enabled = value());
    public void Visible(Control control, Func<bool> value) => Watch(() => control.Visible = value());
    public void Edit(TextBox control, Func<string> get, Action<string> set)
    {
        Text(control, get);
        control.TextChanged += (_, _) => { if (!_refreshing) set(control.Text); };
    }
    public void Check(CheckBox control, Func<bool> get, Action<bool> set)
    {
        Watch(() => control.Checked = get());
        control.CheckedChanged += (_, _) => { if (!_refreshing) { set(control.Checked); RequestRefresh(); } };
    }
    public void Number(NumericUpDown control, Func<int> get, Action<int> set)
    {
        Watch(() => control.Value = Math.Clamp(get(), (int)control.Minimum, (int)control.Maximum));
        control.ValueChanged += (_, _) => { if (!_refreshing) { set((int)control.Value); RequestRefresh(); } };
    }
    public void Command(Button button, ICommand command, Func<object?>? parameter = null, Action? before = null)
    {
        button.Tag = command;
        Enabled(button, () => command.CanExecute(parameter?.Invoke()));
        button.Click += (_, _) => { before?.Invoke(); command.Execute(parameter?.Invoke()); RequestRefresh(); };
    }
    public void Choice<T>(ComboBox combo, Func<IEnumerable<T>> items, Func<T?> selected, Action<T> select, Func<T, string> label) where T : class
    {
        string[] previousLabels = [];
        combo.Format += (_, e) => { if (e.ListItem is T item) e.Value = label(item); };
        combo.FormattingEnabled = true;
        Watch(() =>
        {
            var values = items().ToArray();
            var labels = values.Select(label).ToArray();
            if (!combo.Items.Cast<T>().SequenceEqual(values) || !previousLabels.SequenceEqual(labels))
            {
                combo.BeginUpdate();
                combo.Items.Clear(); combo.Items.AddRange(values.Cast<object>().ToArray());
                combo.EndUpdate(); previousLabels = labels;
            }
            if (!Equals(combo.SelectedItem, selected())) combo.SelectedItem = selected();
        });
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (!_refreshing && combo.SelectedItem is T value) { select(value); RequestRefresh(); }
        };
    }
    public void Models(ModelChoiceList list)
    {
        Watch(() =>
        {
            var values = viewModel.ModelCards.OrderBy(card => card.IsUserModel).ThenBy(card => card.IsUserModel ? 0 : card.Definition.FamilyOrder).ToArray();
            if (!list.Items.Cast<ModelPresentation>().SequenceEqual(values))
            {
                list.BeginUpdate(); list.Items.Clear(); list.Items.AddRange(values); list.EndUpdate();
            }
            if (!Equals(list.SelectedItem, viewModel.SelectedPresentation)) list.SelectedItem = viewModel.SelectedPresentation;
            list.AccessibleName = T("ui.model.choose");
            list.Invalidate();
        });
        list.SelectedIndexChanged += (_, _) =>
        {
            if (!_refreshing && list.SelectedItem is ModelPresentation card)
            {
                viewModel.SelectedModel = card.Definition;
                RequestRefresh();
            }
        };
    }
}

internal static class Ui
{
    public static readonly Color Paper = Color.FromArgb(244, 246, 243);
    public static readonly Color Ink = Color.FromArgb(32, 48, 39);
    public static readonly Color Muted = Color.FromArgb(100, 113, 105);
    public static readonly Color Green = Color.FromArgb(39, 94, 66);
    public static readonly Color Tint = Color.FromArgb(229, 239, 231);
    public static readonly Color Line = Color.FromArgb(216, 224, 217);
    public static TableLayoutPanel Stack() => new() { ColumnCount = 1, Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20), BackColor = Color.White, ColumnStyles = { new ColumnStyle(SizeType.Percent, 100) } };
    public static Panel Scroll(TableLayoutPanel stack)
    {
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White, Margin = new Padding(0) };
        panel.Controls.Add(stack); return panel;
    }
    public static void Add(TableLayoutPanel stack, Control control)
    {
        control.Margin = new Padding(0, 3, 0, 6);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        var row = stack.RowCount++;
        while (stack.RowStyles.Count <= row) stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.Controls.Add(control, 0, row);
    }
    public static Label Label(string text = "") => new() { Text = text, AutoSize = true, UseMnemonic = false, ForeColor = Ink, Dock = DockStyle.Top };
    public static Button Button()
    {
        var button = new Button { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(92, 36), Padding = new Padding(12, 5, 12, 5), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Green, Cursor = Cursors.Hand };
        button.FlatAppearance.BorderColor = Line; button.FlatAppearance.MouseOverBackColor = Tint;
        return button;
    }
    public static void Primary(Button button)
    {
        void RefreshState()
        {
            button.BackColor = button.Enabled ? Green : Line;
            button.ForeColor = button.Enabled ? Color.White : Muted;
            button.Cursor = button.Enabled ? Cursors.Hand : Cursors.Default;
        }
        button.EnabledChanged += (_, _) => RefreshState(); RefreshState();
        button.FlatAppearance.BorderSize = 0; button.FlatAppearance.MouseOverBackColor = Color.FromArgb(28, 76, 51);
        button.MinimumSize = new Size(160, 46); SetFont(button, 11, FontStyle.Bold);
    }
    public static void SetFont(Control control, float size, FontStyle style = FontStyle.Regular)
    {
        var font = new Font("Segoe UI", size, style); control.Font = font;
        control.Disposed += (_, _) => font.Dispose();
    }
    public static ComboBox Combo(string name) => new() { Name = name, DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, IntegralHeight = false, DropDownHeight = 280 };
    public static TextBox TextBox(bool multiline = false) => new() { Multiline = multiline, AcceptsReturn = multiline, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None, Height = multiline ? 120 : 28, Dock = DockStyle.Top };
}

public abstract class LauncherPage : UserControl
{
    protected LauncherViewModel ViewModel { get; }
    private protected UiBindings Bindings { get; }
    protected LauncherPage(LauncherViewModel viewModel)
    {
        ViewModel = viewModel; Dock = DockStyle.Fill; BackColor = Ui.Paper;
        Bindings = new(this, viewModel);
    }
    protected void StartBindings() => Bindings.Start();
    protected Label Heading(TableLayoutPanel stack, string key)
    {
        var label = Ui.Label(); Bindings.Localize(label, key); Ui.Add(stack, label); return label;
    }
    protected Label Value(TableLayoutPanel stack, Func<string> value)
    {
        var label = Ui.Label(); Bindings.Text(label, value); Ui.Add(stack, label); return label;
    }
    protected Button Action(TableLayoutPanel stack, string key, ICommand command, Func<object?>? parameter = null)
    {
        var button = Ui.Button(); Bindings.Localize(button, key); Bindings.Command(button, command, parameter); Ui.Add(stack, button); return button;
    }
    protected ComboBox Choice<T>(TableLayoutPanel stack, string name, string key, Func<IEnumerable<T>> items, Func<T?> selected, Action<T> select, Func<T,string> label) where T : class
    {
        var heading = Heading(stack, key); var combo = Ui.Combo(name); combo.Tag = heading; Bindings.Watch(() => combo.AccessibleName = Core.Localization.T(key));
        Bindings.Choice(combo, items, selected, select, label); Ui.Add(stack, combo); return combo;
    }
    protected TextBox Edit(TableLayoutPanel stack, string key, Func<string> get, Action<string> set, bool multiline = false)
    {
        Heading(stack, key); var box = Ui.TextBox(multiline); Bindings.Watch(() => box.AccessibleName = T(key));
        Bindings.Edit(box, get, set); Ui.Add(stack, box); return box;
    }
    protected override void Dispose(bool disposing) { if (disposing) Bindings.Dispose(); base.Dispose(disposing); }
}
