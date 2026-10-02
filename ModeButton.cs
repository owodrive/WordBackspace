using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WordBackspace;

// A small toggle chip in the Win11 style: it shows one of two labels (on/off)
// and flips on click. The on state is highlighted, the off state is plain.
// Used for the per-word options: exact word / unconditionally,
// caps matter / any caps, word only / since space.
class ModeButton : Button
{
    private readonly string _onText;
    private readonly string _offText;
    private bool _isOn;
    private int _radius = 5;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Radius
    {
        get => _radius;
        set
        {
            _radius = value;
            Invalidate();
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool IsOn
    {
        get => _isOn;
        set
        {
            _isOn = value;
            Text = _isOn ? _onText : _offText;
            UpdateColors();
        }
    }

    public ModeButton(string onText, string offText, bool isOn)
    {
        _onText = onText;
        _offText = offText;
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        TextAlign = ContentAlignment.MiddleCenter;
        Font = Ui.Font;
        Cursor = Cursors.Hand;
        TabStop = true;
        IsOn = isOn;
    }

    // No black focus rectangle around the button after clicking.
    protected override bool ShowFocusCues => false;

    protected override void OnClick(EventArgs e)
    {
        IsOn = !IsOn;
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        if (Width <= 4 || Height <= 4 || _radius <= 2) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // The border is a filled 2px ring (outer rounded rect minus inner
        // rounded rect) instead of a stroked path, so all four sides
        // rasterize to exactly the same thickness and the corners stay
        // anti-aliased. It also covers the Flat style's rectangular border.
        using var outer = Ui.RoundRect(new RectangleF(0, 0, Width, Height), _radius);
        using var inner = Ui.RoundRect(new RectangleF(2, 2, Width - 4, Height - 4), _radius - 2);
        outer.AddPath(inner, true);
        outer.FillMode = FillMode.Alternate;
        using var brush = new SolidBrush(FlatAppearance.BorderColor);
        g.FillPath(brush, outer);
    }

    private void UpdateColors()
    {
        if (IsOn)
        {
            BackColor = Ui.ModeActiveBg;
            ForeColor = Ui.ModeActiveFg;
            FlatAppearance.BorderColor = Ui.ModeActiveBorder;
            FlatAppearance.MouseOverBackColor = Ui.ModeActiveHover;
            FlatAppearance.MouseDownBackColor = Ui.ModeActivePressed;
        }
        else
        {
            BackColor = Ui.Card;
            ForeColor = Ui.Muted;
            FlatAppearance.BorderColor = Ui.CardBorder;
            FlatAppearance.MouseOverBackColor = Ui.ModeIdleHover;
            FlatAppearance.MouseDownBackColor = Ui.ModeIdlePressed;
        }
    }
}