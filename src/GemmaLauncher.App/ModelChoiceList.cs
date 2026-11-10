using System.Drawing;
using System.Windows.Forms;

namespace GemmaLauncher.App;

// Keep native selection, keyboard navigation and accessibility while drawing a readable two-line row.
public sealed class ModelChoiceList : ListBox
{
    private Font? _nameFont;
    private Font? _detailFont;
    public ModelChoiceList()
    {
        Name = "RunModelSelector"; Dock = DockStyle.Fill; BorderStyle = BorderStyle.None;
        DrawMode = DrawMode.OwnerDrawFixed; IntegralHeight = false; BackColor = Color.White;
        DisplayMember = nameof(ModelPresentation.DisplayName);
        UpdateTypography();
    }
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e); UpdateTypography();
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e); UpdateTypography();
    }
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e); UpdateTypography();
    }
    private void UpdateTypography()
    {
        if (Disposing || IsDisposed) return;
        // WinForms scales the inherited Font on DPI changes. Do not scale it a second time.
        _nameFont?.Dispose(); _detailFont?.Dispose();
        _nameFont = new Font(Font, FontStyle.Bold);
        _detailFont = new Font(Font.FontFamily, Font.Size * Ui.SecondaryFontSize / Ui.BodyFontSize, FontStyle.Regular, Font.Unit);
        var scale = DeviceDpi / 96f;
        ItemHeight = Math.Max((int)Math.Ceiling(74 * scale), _nameFont.Height * 2 + _detailFont.Height + (int)Math.Ceiling(18 * scale));
        Invalidate();
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || Items[e.Index] is not ModelPresentation model || _nameFont is null || _detailFont is null) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        var scale = DeviceDpi / 96f;
        var pad = (int)(16 * scale);
        using var background = new SolidBrush(selected ? Ui.Tint : BackColor);
        e.Graphics.FillRectangle(background, e.Bounds);
        if (selected) { using var accent = new SolidBrush(Ui.Green); e.Graphics.FillRectangle(accent, e.Bounds.Left, e.Bounds.Top + 8 * scale, 4 * scale, e.Bounds.Height - 16 * scale); }
        var name = new Rectangle(e.Bounds.Left + pad, e.Bounds.Top + (int)(7 * scale), Math.Max(1, e.Bounds.Width - pad * 2 - (int)(20 * scale)), _nameFont.Height * 2);
        var displayName = model.DisplayName;
        if (e.Bounds.Width / scale < 235 && model.Definition.FamilyId == "gemma-translation") displayName = "Gemma 4 · " + model.Variant;
        TextRenderer.DrawText(e.Graphics, displayName, _nameFont, name, Enabled ? Ui.Ink : Ui.Muted, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        var detail = new Rectangle(name.Left, e.Bounds.Bottom - (int)(8 * scale) - _detailFont.Height, name.Width, _detailFont.Height);
        TextRenderer.DrawText(e.Graphics, model.DownloadLabel + "  ·  " + model.Category, _detailFont, detail, Ui.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (model.FileState == ModelFileState.Ready)
        {
            var ready = new Rectangle(e.Bounds.Right - pad - (int)(18 * scale), name.Top, (int)(18 * scale), _nameFont.Height);
            TextRenderer.DrawText(e.Graphics, "✓", _nameFont, ready, Ui.Green, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }
        using var line = new Pen(Ui.Line); e.Graphics.DrawLine(line, name.Left, e.Bounds.Bottom - 1, e.Bounds.Right - pad, e.Bounds.Bottom - 1);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { _nameFont?.Dispose(); _detailFont?.Dispose(); }
    }
}
