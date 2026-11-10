using System.Drawing;
using System.Windows.Forms;

namespace GemmaLauncher.App;

// Keep native selection, keyboard navigation and accessibility while drawing a readable two-line row.
public sealed class ModelChoiceList : ListBox
{
    private readonly Font _nameFont = new("Segoe UI", 10, FontStyle.Bold);
    private readonly Font _detailFont = new("Segoe UI", 9);
    public ModelChoiceList()
    {
        Name = "RunModelSelector"; Dock = DockStyle.Fill; BorderStyle = BorderStyle.None;
        DrawMode = DrawMode.OwnerDrawFixed; IntegralHeight = false; BackColor = Color.White;
        DisplayMember = nameof(ModelPresentation.DisplayName);
        ItemHeight = 74;
    }
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e); ItemHeight = (int)Math.Round(74 * DeviceDpi / 96d);
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || Items[e.Index] is not ModelPresentation model) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        var scale = DeviceDpi / 96f;
        var pad = (int)(16 * scale);
        using var background = new SolidBrush(selected ? Ui.Tint : BackColor);
        e.Graphics.FillRectangle(background, e.Bounds);
        if (selected) { using var accent = new SolidBrush(Ui.Green); e.Graphics.FillRectangle(accent, e.Bounds.Left, e.Bounds.Top + 8, 4 * scale, e.Bounds.Height - 16); }
        var name = new Rectangle(e.Bounds.Left + pad, e.Bounds.Top + (int)(7 * scale), e.Bounds.Width - pad * 2 - (int)(20 * scale), (int)(36 * scale));
        var displayName = model.DisplayName;
        if (e.Bounds.Width / scale < 235 && model.Definition.FamilyId == "gemma-translation") displayName = "Gemma 4 · " + model.Variant;
        TextRenderer.DrawText(e.Graphics, displayName, _nameFont, name, Enabled ? Ui.Ink : Ui.Muted, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        var detail = new Rectangle(name.Left, e.Bounds.Bottom - (int)(27 * scale), name.Width, (int)(21 * scale));
        TextRenderer.DrawText(e.Graphics, model.DownloadLabel + "  ·  " + model.Category, _detailFont, detail, Ui.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (model.FileState == ModelFileState.Ready)
        {
            var ready = new Rectangle(e.Bounds.Right - pad - (int)(18 * scale), name.Top, (int)(18 * scale), (int)(24 * scale));
            TextRenderer.DrawText(e.Graphics, "✓", _nameFont, ready, Ui.Green, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }
        using var line = new Pen(Ui.Line); e.Graphics.DrawLine(line, name.Left, e.Bounds.Bottom - 1, e.Bounds.Right - pad, e.Bounds.Bottom - 1);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { _nameFont.Dispose(); _detailFont.Dispose(); }
    }
}
