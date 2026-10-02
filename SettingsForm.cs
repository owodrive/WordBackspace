using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WordBackspace;

class SettingsForm : Form
{
    // All layout is specified in logical (96-DPI) pixels and scaled by the
    // form's actual DeviceDpi. AutoScaleMode is None, so WinForms never rescales
    // the layout itself; S() scales every pixel value and point-based fonts are
    // scaled by GDI automatically. The UI is rebuilt if the DPI ever changes.
    private const int WinW = 552;
    private const int WinH = 264;
    private const int Pad = 16;
    private const int TitleY = 14;
    private const int DescY = 40;
    private const int AddY = 62;
    private const int AddH = 48;
    private const int AddInputH = 28;
    private const int HeadY = 122;
    private const int ListY = 142;
    private const int FooterH = 18;
    private const int RowH = 40;
    private const int RowGap = 6;
    private const int InputH = 26;
    private const int DelW = 24;
    private const int InnerPad = 12;
    private const string TipRequirement =
        "Standalone or stuck to other text. exact word: 'GoWordBackspace' stays. unconditionally: it deletes, leaving 'Go'.";
    private const string TipCapitalization =
        "Case-sensitive or not. caps matter: 'wordbackspace' stays. any caps: it deletes.";
    private const string TipDeleted =
        "How far back it erases. word only: 'GoWordBackspace' leaves 'Go'. since space: it leaves nothing.";
    private const string TipSwallow =
        "Keys you type while a word is being deleted are swallowed so they cannot land between the backspaces and leave part of the word. enter only: just Enter is swallowed. all keys: everything typed during the deletion is swallowed.";
    private const string TipDelay =
        "Delay between the backspaces that erase a matched word, in ms per character. 1 is the default; 0 is as fast as possible; higher values delete more slowly.";
    private const int WheelMsg = 0x020E;
    private const int StripW = 12;
    // Inertia parameters for wheel scrolling: each wheel notch adds an
    // impulse; velocity decays exponentially. Total travel of one notch is
    // v0 * InertiaTauMs.
    private const double InertiaTauMs = 130.0;
    private const double InertiaVelCap = 1.0;   // px/ms
    private const double InertiaSettle = 0.004; // px/ms

    private readonly WordStore _store;
    private readonly AppSettings _appSettings;
    private TextBox _newWord = null!;
    private ModeButton _newExact = null!;
    private ModeButton _newCase = null!;
    private ModeButton _newDel = null!;
    private ModeButton _swallowChip = null!;
    private Control _swallowRow = null!;
    private TextBox _delayInput = null!;
    private Control _delayRow = null!;
    private Label _footer = null!;
    private Panel _content = null!;
    private Panel _thumbStrip = null!;
    private List<Row> _rows = new();
    private int _contentHeight;
    private int _offset;
    private int _wheelAccum;
    private int _wheelImpulsePx;
    private volatile float _vel;
    private Thread? _animThread;
    private volatile bool _animActive;
    // Wakes the scroll frame thread from its idle wait so a new glide starts
    // without polling latency.
    private readonly ManualResetEventSlim _animWake = new(false);
    private System.Windows.Forms.Timer? _fadeTimer;
    private int _scrollAlpha;
    private int _fadeTicks;
    private bool _dragging;
    private Point _dragStart;
    private int _dragStartOffset;
    private volatile int _dragPointY;
    private float _dragDragRatio;
    private Thread? _dragFrameThread;
    private volatile bool _dragFrameActive;
    private volatile bool _formClosed;
    private int _animPendingPos;
    private int _dragPendingPos;
    // Cached delegates: BeginInvoke'ing a fresh closure every frame would
    // allocate constantly and cause GC pauses (occasional choppiness).
    private Action _animTick = null!;
    private Action _dragTick = null!;
    private Action _animTickFinal = null!;
    private SolidBrush? _thumbBrush;
    private int _thumbBrushAlpha = -1;
    private HoverTip? _tip;
    private (Control c, string text)? _tipPending;
    private readonly System.Windows.Forms.Timer _tipTimer = new() { Interval = 450 };

    private sealed class Row
    {
        public Control Card = null!;
        public TextBox Tb = null!;
        public ModeButton Exact = null!;
        public ModeButton Case = null!;
        public ModeButton Del = null!;
        public string Text = null!;
    }

    public SettingsForm(WordStore store, AppSettings appSettings)
    {
        _store = store;
        _appSettings = appSettings;
        _animTick = () =>
        {
            if (_animActive) SetOffset(_animPendingPos);
        };
        _dragTick = () =>
        {
            if (_dragging) SetOffset(_dragPendingPos);
        };
        // Applied once when the glide settles; not guarded by _animActive
        // (which is already cleared by then).
        _animTickFinal = () => SetOffset(_animPendingPos);

        Text = "WordBackspace";
        Font = Ui.Font;
        ForeColor = Ui.Text;
        BackColor = Ui.WindowBg;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(S(WinW), S(WinH));
        try { RefreshIcon(); } catch { }
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        _store.Changed += (_, _) =>
        {
            if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)RebuildRows);
        };

        DpiChangedAfterParent += (_, _) =>
        {
            BuildAll();
            RefreshIcon();
        };
        _tipTimer.Tick += (_, _) =>
        {
            _tipTimer.Stop();
            if (_tipPending is { } p) ShowTip(p.c, p.text);
        };
        Deactivate += (_, _) => HideTip();
    }

    // Re-applies the current color theme; called when the system theme
    // changes while the window exists.
    public void ApplyTheme()
    {
        NativeMethods.UseImmersiveDarkMode(Handle, Theme.IsDark);
        ForeColor = Ui.Text;
        BackColor = Ui.WindowBg;
        BuildAll();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BuildAll();
        NativeMethods.UseImmersiveDarkMode(Handle, Theme.IsDark);
        // Pre-create the scroll frame thread while the window opens so the
        // first scroll does not pay thread-creation cost (first-movement
        // stutter).
        EnsureAnimThread();
    }

    private int CurrentDpi
    {
        get
        {
            if (IsHandleCreated) return DeviceDpi;
            try
            {
                using var g = Graphics.FromHwnd(IntPtr.Zero);
                return (int)Math.Round(g.DpiX);
            }
            catch { return 96; }
        }
    }

    private int S(int logical) => (int)Math.Round(logical * CurrentDpi / 96f);

    // The title bar renders the window icon at 16 logical pixels; give it
    // a HICON of exactly that physical size so DWM draws it 1:1. Re-run
    // when the window lands on a monitor with a different DPI.
    private void RefreshIcon()
        => Icon = Ui.WindowIcon(CurrentDpi);

    private static int Measure(string text, Font font)
        => TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding).Width;

    // Height of one text line in pixels (GDI metrics, matching the edit
    // control's own line box).
    private static int LineHeight(Font font)
        => TextRenderer.MeasureText("Wg", font, Size.Empty, TextFormatFlags.NoPadding).Height;

    private int ChipWidth(string onText, string offText)
        => Math.Max(Measure(onText, Ui.Font), Measure(offText, Ui.Font)) + S(24);

    private void AddColumnHeader(string text, int chipLeft, int chipWidth, string tip)
    {
        var label = new Label
        {
            Text = text,
            Font = Ui.FontSmall,
            ForeColor = Ui.Muted,
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(chipLeft, S(HeadY)),
        };
        _content.Controls.Add(label);
        // Center over the chip using the label's real AutoSize width.
        label.Location = new Point(chipLeft + (chipWidth - label.Width) / 2, S(HeadY));
        AttachTip(label, tip);
    }

    private void AttachTip(Control c, string text)
    {
        c.MouseEnter += (_, _) =>
        {
            _tipPending = (c, text);
            _tipTimer.Start();
        };
        c.MouseLeave += (_, _) =>
        {
            _tipPending = null;
            _tipTimer.Stop();
            HideTip();
        };
    }

    private void ShowTip(Control target, string text)
    {
        HideTip();
        var tip = new HoverTip(text, Ui.FontSmall, S(6), S(8), S(6), Ui.Text, S(240));
        _tip = tip;
        Controls.Add(tip);
        Point at = PointToClient(target.PointToScreen(Point.Empty));
        int x = at.X + target.Width / 2 - tip.Width / 2;
        x = Math.Clamp(x, S(4), Math.Max(S(4), ClientSize.Width - tip.Width - S(4)));
        int y = at.Y + target.Height + S(4);
        if (y + tip.Height > ClientSize.Height - S(4))
            y = at.Y - tip.Height - S(4);
        tip.Location = new Point(x, Math.Max(S(4), y));
        tip.BringToFront();
        tip.MouseEnter += (_, _) => HideTip();
        tip.Visible = true;
    }

    private void HideTip()
    {
        _tipPending = null;
        _tipTimer.Stop();
        if (_tip == null) return;
        Controls.Remove(_tip);
        _tip.Dispose();
        _tip = null;
    }

    private ModeButton MakeChip(string onText, string offText, bool isOn)
    {
        var b = new ModeButton(onText, offText, isOn) { Radius = S(5) };
        Ui.ApplyRounded(b, S(5));
        return b;
    }

    private int CardWidth => S(WinW - 2 * Pad);

    private int MaxOffset => Math.Max(0, _contentHeight - ClientSize.Height);

    private int ClampOffset(int offset) => Math.Clamp(offset, 0, MaxOffset);

    private void BuildAll()
    {
        HideTip();
        while (Controls.Count > 0)
        {
            var c = Controls[0];
            Controls.Remove(c);
            c.Dispose();
        }
        _rows.Clear();
        _offset = 0;
        _wheelAccum = 0;
        _dragging = false;
        _dragFrameActive = false;
        _animActive = false;
        _vel = 0;
        Interlocked.Exchange(ref _wheelImpulsePx, 0);

        ClientSize = new Size(S(WinW), S(WinH));

        // All scrollable content lives in one panel that is as tall as the
        // content. Scrolling moves that single window instead of N child
        // controls, so the content always moves as one atomic unit: no
        // smearing, no lag between the window background and the controls,
        // and rows that start off-screen simply ride along in the panel's
        // window buffer.
        _content = new Panel
        {
            BackColor = Ui.WindowBg,
            Location = Point.Empty,
            Size = new Size(S(WinW), 1),
        };
        Controls.Add(_content);

        int pad = S(Pad);

        var title = new Label
        {
            Text = "WordBackspace",
            Font = new Font(Ui.Font.FontFamily, 14f, FontStyle.Bold),
            ForeColor = Ui.Text,
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(pad, S(TitleY)),
        };
        _content.Controls.Add(title);

        var desc = new Label
        {
            Text = "When you type one of the words below, WordBackspace deletes it. Words can contain spaces.",
            Font = Ui.Font,
            ForeColor = Ui.Muted,
            BackColor = Color.Transparent,
            AutoSize = false,
            Location = new Point(pad, S(DescY)),
            Size = new Size(CardWidth, S(16)),
        };
        _content.Controls.Add(desc);

        var addCard = BuildAddCard(pad);
        _content.Controls.Add(addCard);

        var head = new Label
        {
            Text = "Words",
            Font = Ui.FontBold,
            ForeColor = Ui.Muted,
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(pad, S(HeadY)),
        };
        _content.Controls.Add(head);

        // Column headers above the word list, centered over each row's
        // option chips (same right-anchored layout as CreateRow).
        int wExact = ChipWidth("exact word", "unconditionally");
        int wCase = ChipWidth("caps matter", "any caps");
        int wDel = ChipWidth("word only", "since space");
        int leftDel = CardWidth - S(10) - S(DelW) - S(8) - wDel;
        int leftCase = leftDel - S(8) - wCase;
        int leftExact = leftCase - S(8) - wExact;
        AddColumnHeader("requirement", pad + leftExact, wExact, TipRequirement);
        AddColumnHeader("capitalization", pad + leftCase, wCase, TipCapitalization);
        AddColumnHeader("deleted", pad + leftDel, wDel, TipDeleted);

        _footer = new Label
        {
            Text = "Saved automatically",
            Font = Ui.FontSmall,
            ForeColor = Ui.Muted,
            BackColor = Color.Transparent,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(pad, 0),
            Size = new Size(CardWidth, S(FooterH)),
        };
        _content.Controls.Add(_footer);

        _swallowRow = BuildSwallowRow();
        _content.Controls.Add(_swallowRow);

        _delayRow = BuildDelayRow();
        _content.Controls.Add(_delayRow);

        // Win11-style overlay scrollbar: a thin opaque strip on the right edge
        // that paints the thumb and captures the drag. It sits above the
        // content panel, which now covers the whole window.
        _thumbStrip = new ThumbStrip
        {
            BackColor = Ui.WindowBg,
            Location = new Point(S(WinW) - StripW, 0),
            Size = new Size(StripW, ClientSize.Height),
        };
        _thumbStrip.Paint += (_, e) => PaintThumb(e.Graphics);
        _thumbStrip.MouseDown += (_, e) => StripDown(e);
        _thumbStrip.MouseMove += (_, e) => StripMove(e);
        _thumbStrip.MouseUp += (_, e) => StripUp(e);
        Controls.Add(_thumbStrip);
        _thumbStrip.BringToFront();

        RebuildRows();
    }

    private Control BuildAddCard(int x)
    {
        var card = new CardPanel
        {
            Location = new Point(x, S(AddY)),
            Size = new Size(CardWidth, S(AddH)),
            CornerRadius = S(8),
        };

        int inner = S(InnerPad);
        int inputH = S(AddInputH);
        int btnW = S(56);
        int btnH = S(AddInputH);
        int wExact = ChipWidth("exact word", "unconditionally");
        int wCase = ChipWidth("caps matter", "any caps");
        int wDel = ChipWidth("word only", "since space");

        int btnX = CardWidth - inner - btnW;
        int delX = btnX - S(8) - wDel;
        int caseX = delX - S(8) - wCase;
        int exactX = caseX - S(8) - wExact;
        int inputW = exactX - S(10) - inner;
        int centerY = (S(AddH) - inputH) / 2;

        int inputBoxW = Math.Max(inputW, 80);
        var inputFont = new Font(Ui.Font.FontFamily, 10f);
        int lineH = LineHeight(inputFont);
        _newWord = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Ui.Card,
            ForeColor = Ui.Text,
            Font = inputFont,
            // The box is exactly one text line tall and centered in the
            // rounded frame, so the text sits vertically centered (a plain
            // TextBox top-aligns its text in extra height). The left inset
            // leaves a small gap between the border and the text.
            Location = new Point(7, (inputH - lineH) / 2),
            Size = new Size(inputBoxW - 9, lineH),
            TabStop = true,
        };
        DrawPlaceholder(_newWord, "Add a word…");
        _newWord.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                AddNew();
            }
        };
        var newWordBox = new RoundBox
        {
            Location = new Point(inner, centerY),
            Size = new Size(inputBoxW, inputH),
        };
        newWordBox.Controls.Add(_newWord);
        _newWord.Enter += (_, _) => newWordBox.BorderColor = Ui.Accent;
        _newWord.Leave += (_, _) => newWordBox.BorderColor = Ui.InputBorder;
        card.Controls.Add(newWordBox);

        // Defaults for new words: exact word, any caps, word only.
        _newExact = MakeChip("exact word", "unconditionally", true);
        _newExact.Location = new Point(exactX, centerY);
        _newExact.Size = new Size(wExact, inputH);
        card.Controls.Add(_newExact);

        _newCase = MakeChip("caps matter", "any caps", false);
        _newCase.Location = new Point(caseX, centerY);
        _newCase.Size = new Size(wCase, inputH);
        card.Controls.Add(_newCase);

        _newDel = MakeChip("word only", "since space", true);
        _newDel.Location = new Point(delX, centerY);
        _newDel.Size = new Size(wDel, inputH);
        card.Controls.Add(_newDel);

        AttachTip(_newExact, TipRequirement);
        AttachTip(_newCase, TipCapitalization);
        AttachTip(_newDel, TipDeleted);

        var add = new RoundButton
        {
            Text = "Add",
            Location = new Point(btnX, centerY),
            Size = new Size(btnW, btnH),
            ForeColor = Ui.AccentText,
            Font = Ui.FontBold,
            Radius = S(5),
            TabStop = true,
        };
        add.Click += (_, _) => AddNew();
        card.Controls.Add(add);

        return card;
    }

    // Bottom global setting: which keys are swallowed while a word is being
    // deleted (see BurstSwallowMode). A label plus a two-state chip, laid out
    // like a word row.
    private Control BuildSwallowRow()
    {
        var card = new CardPanel
        {
            Location = Point.Empty,
            Size = new Size(CardWidth, S(RowH)),
            CornerRadius = S(8),
        };

        int inner = S(InnerPad);
        int inputH = S(InputH);
        int chipW = ChipWidth("all keys", "enter only");
        int chipX = CardWidth - inner - chipW;
        int centerY = (S(RowH) - inputH) / 2;

        var label = new Label
        {
            Text = "While deleting a word, swallow",
            Font = Ui.Font,
            ForeColor = Ui.Text,
            BackColor = Color.Transparent,
            AutoSize = true,
        };
        card.Controls.Add(label);
        label.Location = new Point(inner, (S(RowH) - LineHeight(Ui.Font)) / 2);

        _swallowChip = MakeChip("all keys", "enter only",
            _appSettings.SwallowMode == BurstSwallowMode.AllKeys);
        _swallowChip.Location = new Point(chipX, centerY);
        _swallowChip.Size = new Size(chipW, inputH);
        _swallowChip.Click += (_, _) =>
        {
            var mode = _swallowChip.IsOn ? BurstSwallowMode.AllKeys : BurstSwallowMode.EnterOnly;
            _appSettings.SetSwallowMode(mode);
        };
        AttachTip(_swallowChip, TipSwallow);
        card.Controls.Add(_swallowChip);

        return card;
    }

    // Bottom global setting: the delay between the individual backspaces
    // (ms per character). A label plus a numeric input, saved on every
    // keystroke, like the word rows.
    private Control BuildDelayRow()
    {
        var card = new CardPanel
        {
            Location = Point.Empty,
            Size = new Size(CardWidth, S(RowH)),
            CornerRadius = S(8),
        };

        int inner = S(InnerPad);
        int inputH = S(InputH);
        int boxW = S(72);
        int boxX = CardWidth - inner - boxW;
        int centerY = (S(RowH) - inputH) / 2;

        var label = new Label
        {
            Text = "Deletion delay (ms/char)",
            Font = Ui.Font,
            ForeColor = Ui.Text,
            BackColor = Color.Transparent,
            AutoSize = true,
        };
        card.Controls.Add(label);
        label.Location = new Point(inner, (S(RowH) - LineHeight(Ui.Font)) / 2);

        var box = new RoundBox
        {
            Location = new Point(boxX, centerY),
            Size = new Size(boxW, inputH),
        };
        var inputFont = new Font(Ui.Font.FontFamily, 10f);
        int lineH = LineHeight(inputFont);
        _delayInput = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Ui.Card,
            ForeColor = Ui.Text,
            Font = inputFont,
            TextAlign = HorizontalAlignment.Right,
            Text = _appSettings.DeletionDelayMs.ToString(),
            Location = new Point(7, (inputH - lineH) / 2),
            Size = new Size(boxW - 9, lineH),
            TabStop = true,
        };
        box.Controls.Add(_delayInput);
        _delayInput.Enter += (_, _) => box.BorderColor = Ui.Accent;

        void Commit()
        {
            if (!int.TryParse(_delayInput.Text.Trim(), out int ms))
            {
                _delayInput.Text = _appSettings.DeletionDelayMs.ToString();
                return;
            }
            ms = Math.Clamp(ms, 0, 500);
            _delayInput.Text = ms.ToString();
            if (ms == _appSettings.DeletionDelayMs) return;
            _appSettings.SetDeletionDelayMs(ms);
        }

        _delayInput.Leave += (_, _) => box.BorderColor = Ui.InputBorder;
        _delayInput.Leave += (_, _) => Commit();
        _delayInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Commit();
            }
        };
        // The delay is saved on every keystroke so it is already in effect
        // while the user is still typing it. An empty field waits for
        // Enter/leave, like the word rows.
        _delayInput.TextChanged += (_, _) =>
        {
            if (_delayInput.Text.Length > 0) Commit();
        };
        AttachTip(label, TipDelay);
        AttachTip(_delayInput, TipDelay);
        card.Controls.Add(box);

        return card;
    }

    private void RebuildRows()
    {
        HideTip();
        var words = _store.Words;

        // Reuse existing row controls when the word still exists, so toggling a
        // mode or editing a word does not flicker the whole list.
        var byText = new Dictionary<string, Row>();
        foreach (var r in _rows)
            if (!byText.ContainsKey(r.Text)) byText[r.Text] = r;

        var used = new HashSet<Row>();
        var next = new List<Row>(words.Count);
        foreach (var w in words)
        {
            if (byText.TryGetValue(w.Text, out var existing) && !used.Contains(existing))
            {
                used.Add(existing);
                existing.Exact.IsOn = w.ExactOnly;
                existing.Case.IsOn = w.CaseSensitive;
                existing.Del.IsOn = w.WordOnly;
                next.Add(existing);
            }
            else
            {
                next.Add(CreateRow(w));
            }
        }

        foreach (var r in _rows)
        {
            if (used.Contains(r)) continue;
            _content.Controls.Remove(r.Card);
            r.Card.Dispose();
        }
        _rows = next;

        int rowH = S(RowH);
        int rowGap = S(RowGap);
        int lastBottom = S(HeadY) + S(16);
        for (int i = 0; i < _rows.Count; i++)
        {
            int top = S(ListY) + i * (rowH + rowGap);
            _rows[i].Card.Location = new Point(S(Pad), top);
            lastBottom = top + rowH;
        }

        int footerTop = lastBottom + S(10);
        _swallowRow.Location = new Point(S(Pad), footerTop);
        footerTop += S(RowH) + S(10);
        _delayRow.Location = new Point(S(Pad), footerTop);
        footerTop += S(RowH) + S(10);
        _footer.Location = new Point(S(Pad), footerTop);
        _contentHeight = footerTop + S(FooterH) + S(8);
        _content.Height = _contentHeight;

        _offset = ClampOffset(_offset);
        ApplyAllTops();
        ShowScrollbar();
    }

    private Row CreateRow(Word w)
    {
        string current = w.Text;

        var card = new CardPanel
        {
            Location = Point.Empty,
            Size = new Size(CardWidth, S(RowH)),
            CornerRadius = S(8),
        };

        int inner = S(InnerPad);
        int inputH = S(InputH);
        int wExact = ChipWidth("exact word", "unconditionally");
        int wCase = ChipWidth("caps matter", "any caps");
        int wDel = ChipWidth("word only", "since space");
        int delW = S(DelW);

        int delX = CardWidth - S(10) - delW;
        int optDelX = delX - S(8) - wDel;
        int optCaseX = optDelX - S(8) - wCase;
        int optExactX = optCaseX - S(8) - wExact;
        int inputW = optExactX - S(10) - inner;
        int centerY = (S(RowH) - inputH) / 2;

        int inputBoxW = Math.Max(inputW, 80);
        var tbFont = new Font(Ui.Font.FontFamily, 10f);
        int lineH = LineHeight(tbFont);
        var tb = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Ui.Card,
            ForeColor = Ui.Text,
            Font = tbFont,
            Text = current,
            // One text line tall, centered in the rounded frame; the left inset
            // leaves a small gap between the border and the text.
            Location = new Point(7, (inputH - lineH) / 2),
            Size = new Size(inputBoxW - 9, lineH),
            TabStop = true,
        };
        var inputBox = new RoundBox
        {
            Location = new Point(inner, centerY),
            Size = new Size(inputBoxW, inputH),
        };
        inputBox.Controls.Add(tb);
        tb.Enter += (_, _) => inputBox.BorderColor = Ui.Accent;

        var exact = MakeChip("exact word", "unconditionally", w.ExactOnly);
        exact.Location = new Point(optExactX, centerY);
        exact.Size = new Size(wExact, inputH);

        var caseChip = MakeChip("caps matter", "any caps", w.CaseSensitive);
        caseChip.Location = new Point(optCaseX, centerY);
        caseChip.Size = new Size(wCase, inputH);

        var delChip = MakeChip("word only", "since space", w.WordOnly);
        delChip.Location = new Point(optDelX, centerY);
        delChip.Size = new Size(wDel, inputH);

        AttachTip(exact, TipRequirement);
        AttachTip(caseChip, TipCapitalization);
        AttachTip(delChip, TipDeleted);

        var del = new GlyphButton
        {
            Location = new Point(delX, (S(RowH) - delW) / 2),
            Size = new Size(delW, delW),
            Font = new Font(Ui.Font.FontFamily, 10f),
            TabStop = false,
        };

        var row = new Row { Card = card, Tb = tb, Exact = exact, Case = caseChip, Del = delChip, Text = current };

        void ApplyFlags() =>
            _store.UpdateFlags(current, exact.IsOn, caseChip.IsOn, delChip.IsOn);
        exact.Click += (_, _) => ApplyFlags();
        caseChip.Click += (_, _) => ApplyFlags();
        delChip.Click += (_, _) => ApplyFlags();

        void Commit()
        {
            string nextText = WordStore.Normalize(tb.Text);
            if (nextText == current) return;
            if (nextText.Length == 0)
            {
                _store.Remove(current);
                return;
            }
            _store.UpdateText(current, nextText, exact.IsOn, caseChip.IsOn, delChip.IsOn);
            current = nextText;
            row.Text = nextText;
        }

        // The word is saved on every keystroke, not only on Enter/leave, so a
        // word edited in here is already active in other apps. Clearing the
        // field to empty still waits for Enter/leave so the row does not
        // vanish while the user is re-typing it.
        tb.TextChanged += (_, _) =>
        {
            string nextText = WordStore.Normalize(tb.Text);
            if (nextText.Length == 0 || nextText == current) return;
            _store.UpdateText(current, nextText, exact.IsOn, caseChip.IsOn, delChip.IsOn);
            current = nextText;
            row.Text = nextText;
        };

        tb.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Commit();
            }
        };
        tb.Leave += (_, _) => inputBox.BorderColor = Ui.InputBorder;
        tb.Leave += (_, _) => Commit();
        del.Click += (_, _) => _store.Remove(current);

        card.Controls.Add(inputBox);
        card.Controls.Add(exact);
        card.Controls.Add(caseChip);
        card.Controls.Add(delChip);
        card.Controls.Add(del);
        _content.Controls.Add(card);
        return row;
    }

    private void ApplyAllTops()
    {
        // One SetWindowPos moves the entire content — atomic and instant, no
        // per-control repaints in between.
        _content.Top = -_offset;
    }

    private void SetOffset(int offset)
    {
        offset = ClampOffset(offset);
        if (offset == _offset) return;
        _offset = offset;
        ApplyAllTops();
        _thumbStrip.Invalidate();
    }

    private float ThumbHeight()
    {
        if (MaxOffset <= 0) return 0f;
        float trackH = ClientSize.Height - 8f;
        return Math.Max(32f, trackH * ClientSize.Height / (float)Math.Max(_contentHeight, 1));
    }

    // Thumb geometry in _thumbStrip coordinates. The strip's top edge aligns
    // with the form's top edge, so Y is shared with the form.
    private RectangleF ThumbRect()
    {
        if (MaxOffset <= 0) return RectangleF.Empty;
        float thumbH = ThumbHeight();
        float trackH = ClientSize.Height - 8f;
        float y = 4f + _offset * (trackH - thumbH) / MaxOffset;
        return new RectangleF(3f, y, 6f, thumbH);
    }

    private void PaintThumb(Graphics g)
    {
        g.Clear(Ui.WindowBg);
        if (MaxOffset <= 0 || _scrollAlpha <= 8) return;
        var r = ThumbRect();
        if (r.IsEmpty || r.Height < 6f) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // Cache the brush (recreated only when the fade alpha changes) and
        // draw the 6px pill as a rectangle plus two end caps — no per-frame
        // brush or GraphicsPath allocation.
        if (_thumbBrush is null || _thumbBrushAlpha != _scrollAlpha)
        {
            _thumbBrush?.Dispose();
            _thumbBrushAlpha = _scrollAlpha;
            _thumbBrush = new SolidBrush(Color.FromArgb(_scrollAlpha, Ui.Thumb));
        }
        float mid = r.Y + r.Width;
        g.FillRectangle(_thumbBrush, r.X, mid, r.Width, r.Bottom - mid);
        g.FillEllipse(_thumbBrush, r.X, r.Y, r.Width, r.Width);
        g.FillEllipse(_thumbBrush, r.X, r.Bottom - r.Width, r.Width, r.Width);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WheelMsg)
        {
            int delta = (short)(m.LParam.ToInt64() >> 16);
            if (delta != 0 && MaxOffset > 0)
            {
                HandleWheel(delta);
                return;
            }
        }
        base.WndProc(ref m);
    }

    // Wheel messages over child controls (the content panel, text boxes, ...)
    // are forwarded by WinForms to the parent's OnMouseWheel (not its WndProc),
    // so handle both paths.
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (MaxOffset > 0 && e.Delta != 0)
        {
            HandleWheel(e.Delta);
            return;
        }
        base.OnMouseWheel(e);
    }

    private void HandleWheel(int delta)
    {
        HideTip();
        // Wheeling while dragging the thumb means the user switched intent:
        // stop the drag so the two do not fight over the offset.
        if (_dragging)
        {
            _dragging = false;
            _dragFrameActive = false;
            _thumbStrip.Capture = false;
        }
        if (MaxOffset <= 0)
        {
            _wheelAccum = 0;
            return;
        }

        // Windows convention: scroll SystemInformation.MouseWheelScrollLines
        // lines per 120 delta units (default 3, -1 = one page, 0 = none).
        // Deltas are accumulated so fine wheels and fast notches behave the
        // same way.
        _wheelAccum += delta;
        int lines = SystemInformation.MouseWheelScrollLines;
        if (lines == 0)
        {
            _wheelAccum = 0;
            return;
        }
        int distance = lines < 0 ? ClientSize.Height : lines * S(16);
        while (_wheelAccum >= 120)
        {
            _wheelAccum -= 120;
            if (distance > 0) QueueScroll(-distance);
        }
        while (_wheelAccum <= -120)
        {
            _wheelAccum += 120;
            if (distance > 0) QueueScroll(distance);
        }
        ShowScrollbar();
    }

    private void QueueScroll(int distancePx)
    {
        // Each notch adds an impulse; the frame thread turns the accumulated
        // impulse into velocity. Consecutive notches build up velocity
        // instead of restarting an animation, so fast scrolling is one
        // smooth glide.
        Interlocked.Add(ref _wheelImpulsePx, distancePx);
        _animActive = true;
        _animWake.Set();
        EnsureAnimThread();
    }

    private void EnsureAnimThread()
    {
        if (_animThread is { IsAlive: true }) return;
        _animThread = new Thread(AnimLoop) { IsBackground = true, Name = "wordbackspace-scroll" };
        _animThread.Start();
    }

// A dedicated frame loop (4ms) drives the offset with an
// inertia model: wheel notches queue impulses (px), the loop converts them to
// velocity (px/ms), moves the content by velocity * dt each frame, and decays
// the velocity exponentially. WinForms timers only tick every ~15ms, which
// makes interpolated scrolling visibly choppy. The thread is created once
// when the window opens and waits on an event between scrolls; the 1 ms timer
// resolution (process-wide) is only held while a glide is actually in flight,
// so an idle settings window does not keep the CPU out of deep idle states.
    private void AnimLoop()
    {
        bool highRes = false;
        try
        {
            int last = 0;
            while (!_formClosed)
            {
                if (!_animActive)
                {
                    if (highRes)
                    {
                        NativeMethods.timeEndPeriod(1);
                        highRes = false;
                    }
                    last = 0;
                    _animWake.Reset();
                    // A wheel event that set _animActive between the check
                    // above and the Reset is caught here instead of
                    // sleeping in Wait until the next event.
                    if (_animActive) continue;
                    _animWake.Wait();
                    continue;
                }

                if (!highRes)
                {
                    NativeMethods.timeBeginPeriod(1);
                    highRes = true;
                }

                int now = Environment.TickCount;
                int dt = last == 0 ? 4 : Math.Min(32, now - last);
                if (dt <= 0) dt = 4;
                last = now;

                int px = Interlocked.Exchange(ref _wheelImpulsePx, 0);
                if (px != 0)
                    _vel = (float)Math.Clamp(_vel + px / InertiaTauMs,
                        -InertiaVelCap, InertiaVelCap);

                if (Math.Abs(_vel) < InertiaSettle)
                {
                    _vel = 0;
                    _animActive = false;
                    last = 0;
                    continue;
                }

                int max = MaxOffset;
                int pos = (int)Math.Round(_offset + _vel * dt);
                if (pos <= 0)
                {
                    pos = 0;
                    if (_vel < 0) _vel = 0;
                }
                else if (pos >= max)
                {
                    pos = max;
                    if (_vel > 0) _vel = 0;
                }

                _vel *= (float)Math.Exp(-dt / InertiaTauMs);

                _animPendingPos = pos;
                try
                {
                    if (Math.Abs(_vel) < InertiaSettle)
                    {
                        // Glide over: deliver the final position unguarded and
                        // stop.
                        _vel = 0;
                        _animActive = false;
                        last = 0;
                        BeginInvoke(_animTickFinal);
                    }
                    else
                    {
                        BeginInvoke(_animTick);
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                Thread.Sleep(4);
            }
        }
        finally
        {
            _animActive = false;
            if (highRes) NativeMethods.timeEndPeriod(1);
        }
    }

    private void StripDown(MouseEventArgs e)
    {
        if (MaxOffset > 0 && ThumbRect().Contains(e.Location))
        {
            // The drag takes over: stop any in-flight wheel glide so it
            // does not fight the cursor for the offset.
            _animActive = false;
            _vel = 0;
            Interlocked.Exchange(ref _wheelImpulsePx, 0);
            _dragging = true;
            _dragStart = e.Location;
            _dragStartOffset = _offset;
            _dragPointY = e.Y;
            float trackH = ClientSize.Height - 8f;
            float thumbH = ThumbHeight();
            _dragDragRatio = trackH - thumbH <= 0f ? 0f : MaxOffset / (trackH - thumbH);
            _dragFrameActive = true;
            if (_dragFrameThread is null or { IsAlive: false })
            {
                _dragFrameThread = new Thread(DragFrameLoop) { IsBackground = true, Name = "wordbackspace-drag" };
                _dragFrameThread.Start();
            }
            _thumbStrip.Capture = true;
            _scrollAlpha = 255;
            _thumbStrip.Invalidate();
        }
    }

    private void StripMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        // Only record the latest position; the frame thread applies it at a
        // steady rate. The handler is near-zero cost, so even a very fast
        // mouse can never build up an input backlog (the cause of the
        // residual smearing at high scroll speeds).
        _dragPointY = e.Y;
        if (_scrollAlpha != 255) _scrollAlpha = 255;
    }

    private void StripUp(MouseEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        _dragFrameActive = false;
        _thumbStrip.Capture = false;
        ShowScrollbar();
    }

    // Applies drag positions at a steady ~240fps from the latest recorded
    // mouse point, coalescing fast input bursts into one move per frame.
    private void DragFrameLoop()
    {
        NativeMethods.timeBeginPeriod(1);
        try
        {
            while (_dragFrameActive)
            {
                int py = _dragPointY;
                int pos = (int)Math.Round(_dragStartOffset + (py - _dragStart.Y) * _dragDragRatio);
                try
                {
                    _dragPendingPos = pos;
                    BeginInvoke(_dragTick);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                Thread.Sleep(4);
            }
        }
        finally
        {
            _dragFrameActive = false;
            NativeMethods.timeEndPeriod(1);
        }
    }

    private void ShowScrollbar()
    {
        _scrollAlpha = 255;
        _fadeTicks = 0;
        _thumbStrip.Invalidate();
        if (_fadeTimer == null)
        {
            _fadeTimer = new System.Windows.Forms.Timer { Interval = 40 };
            _fadeTimer.Tick += (_, _) =>
            {
                if (_fadeTicks++ < 30) return;
                _scrollAlpha = Math.Max(0, _scrollAlpha - 28);
                _thumbStrip.Invalidate();
                if (_scrollAlpha == 0) _fadeTimer.Stop();
            };
        }
        if (!_fadeTimer.Enabled) _fadeTimer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _formClosed = true;
        _animActive = false;
        _dragFrameActive = false;
        _animWake.Set();
        _fadeTimer?.Stop();
        _fadeTimer?.Dispose();
        _thumbBrush?.Dispose();
        base.OnFormClosed(e);
    }

    private void AddNew()
    {
        string text = _newWord.Text.Trim();
        if (text.Length == 0) return;
        _store.AddOrUpdate(text, _newExact.IsOn, _newCase.IsOn, _newDel.IsOn);
        _newWord.Clear();
    }

    public void FocusAddBox()
    {
        _newWord.Focus();
    }

    private static void DrawPlaceholder(TextBox tb, string text)
    {
        tb.Paint += (_, e) =>
        {
            if (tb.TextLength == 0)
            {
                var sf = new StringFormat(StringFormatFlags.NoWrap);
                e.Graphics.DrawString(text, tb.Font, new SolidBrush(Ui.Muted),
                    new RectangleF(1, (tb.Height - e.Graphics.MeasureString(text, tb.Font).Height) / 2f,
                        tb.Width - 4, tb.Height), sf);
            }
        };
    }
}

class GlyphButton : Button
{
    private bool _hover;
    private bool _pressed;
    private int _radius = 5;

    public GlyphButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        BackColor = Ui.Card;
        Cursor = Cursors.Hand;
    }

    // No black focus rectangle around the button after clicking.
    protected override bool ShowFocusCues => false;

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (Width <= 2 || Height <= 2) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_pressed || _hover)
        {
            using var brush = new SolidBrush(_pressed ? Ui.DangerPressed : Ui.DangerBg);
            using var path = Ui.RoundRect(new RectangleF(0, 0, Width, Height), _radius);
            g.FillPath(brush, path);
        }
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        // Draw the × as two strokes centered exactly on the button center
        // (font-based glyphs sit off-center inside their layout cells).
        float s = Math.Min(Width, Height) / 8f;
        float cx = Width / 2f;
        float cy = Height / 2f;
        using var pen = new Pen(_hover || _pressed ? Ui.Danger : Ui.Muted, 1.8f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
        };
        g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
        g.DrawLine(pen, cx + s, cy - s, cx - s, cy + s);
    }
}

class RoundBox : Panel
{
    private Color _border = Ui.InputBorder;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color BorderColor
    {
        get => _border;
        set
        {
            if (_border == value) return;
            _border = value;
            Invalidate();
        }
    }

    public RoundBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        BackColor = Ui.Card;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Card);
        if (Width <= 4 || Height <= 4) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // The border is a filled 2px ring (outer rounded rect minus inner
        // rounded rect) rather than a stroked path, so all four sides
        // rasterize to exactly the same thickness and the corners stay
        // anti-aliased.
        using var outer = Ui.RoundRect(new RectangleF(0, 0, Width, Height), 6.5f);
        using var inner = Ui.RoundRect(new RectangleF(2, 2, Width - 4, Height - 4), 4.5f);
        outer.AddPath(inner, true);
        outer.FillMode = System.Drawing.Drawing2D.FillMode.Alternate;
        using var brush = new SolidBrush(_border);
        g.FillPath(brush, outer);
    }
}

class FlatButton : Button
{
    // No black focus rectangle around the button after clicking.
    protected override bool ShowFocusCues => false;
}

// An accent button whose rounded fill is painted (not region-clipped), so the
// rounded edge is anti-aliased and blends smoothly into the card behind it.
class RoundButton : Button
{
    private int _radius = 5;
    private bool _hover;
    private bool _pressed;

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

    public RoundButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        TextAlign = ContentAlignment.MiddleCenter;
        BackColor = Ui.Card;
        Cursor = Cursors.Hand;
    }

    // No black focus rectangle around the button after clicking.
    protected override bool ShowFocusCues => false;

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (Width <= 2 || Height <= 2) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color c = _pressed ? Ui.AccentPressed : _hover ? Ui.AccentHover : Ui.Accent;
        using var brush = new SolidBrush(c);
        using var path = Ui.RoundRect(new RectangleF(0, 0, Width, Height), _radius);
        g.FillPath(brush, path);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

class ThumbStrip : Panel
{
    public ThumbStrip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
    }
}

class CardPanel : Panel
{
    private int _cornerRadius = 6;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int CornerRadius
    {
        get => _cornerRadius;
        set
        {
            if (_cornerRadius == value) return;
            _cornerRadius = value;
            UpdateRegion();
            Invalidate();
        }
    }

    public CardPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        BackColor = Ui.Card;
    }

    private void UpdateRegion()
    {
        Region = new Region(Ui.RoundRect(
            new RectangleF(0, 0, Math.Max(Width, 1), Math.Max(Height, 1)), _cornerRadius));
    }

    protected override void OnResize(EventArgs e)
    {
        UpdateRegion();
        base.OnResize(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Ui.Card);
        if (Width > 2 && Height > 2 && _cornerRadius > 0)
        {
            // The border path is inset half a pixel so the 1px stroke lands on the
            // same edge as the region clip; the radius matches the region's.
            using var path = Ui.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), _cornerRadius - 0.5f);
            using var pen = new Pen(Ui.CardBorder);
            g.DrawPath(pen, path);
        }
    }
}

// A custom-styled tooltip: a small rounded card with a 1px border and wrapped
// text, painted with the current theme colors. SettingsForm shows it below
// the hovered control (the WinForms ToolTip class would give the plain
// system-styled bubble).
class HoverTip : Panel
{
    private readonly int _cornerRadius;

    public HoverTip(string text, Font font, int radius, int padX, int padY,
                    Color fg, int maxTextWidth)
    {
        _cornerRadius = radius;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Ui.Card;
        var size = TextRenderer.MeasureText(text, font, new Size(maxTextWidth, 0), TextFormatFlags.WordBreak);
        Size = new Size(size.Width + 2 * padX + 2, size.Height + 2 * padY + 2);
        Controls.Add(new Label
        {
            Text = text,
            Font = font,
            ForeColor = fg,
            BackColor = Color.Transparent,
            AutoSize = false,
            Location = new Point(padX + 1, padY),
            Size = size,
        });
        UpdateRegion();
    }

    private void UpdateRegion()
    {
        Region = new Region(Ui.RoundRect(
            new RectangleF(0, 0, Math.Max(Width, 1), Math.Max(Height, 1)), _cornerRadius));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Ui.Card);
        using var path = Ui.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), _cornerRadius - 0.5f);
        using var pen = new Pen(Ui.CardBorder);
        g.DrawPath(pen, path);
    }
}