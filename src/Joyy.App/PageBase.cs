using System.Drawing.Drawing2D;
using Joyy.Core;
using Joyy.Platform;

namespace Joyy.App;

/// <summary>Live bridge state handed to pages ~20 times a second.</summary>
internal readonly record struct LiveView(BridgeSnapshot Snapshot, StatusView Status, Profile Active, bool HostRunning);

/// <summary>Slower-changing environment: drivers, hiding, other tools, startup options.</summary>
internal sealed record EnvView(
    HidingView Hiding,
    IReadOnlyList<string> Conflicts,
    bool ViGEmInstalled,
    bool HidHideInstalled,
    bool HideEnabled,
    bool HidingActive,
    bool ReplugNeeded,
    bool StartWithWindows,
    bool StartMinimized)
{
    public static readonly EnvView Empty = new(new HidingView("", false, false, false), Array.Empty<string>(),
        true, false, true, false, false, false, false);
}

/// <summary>What pages share: actions, the profile session, navigation and controller pictures.</summary>
internal sealed class ShellContext
{
    public ShellContext(IAppActions actions, ProfileSession session)
    {
        Actions = actions;
        Session = session;
    }

    public IAppActions Actions { get; }
    public ProfileSession Session { get; }
    // Safe placeholder until the first tick arrives (a default LiveView has a null profile).
    public LiveView Live { get; set; } = new(default, new StatusView(Theme.Idle, Icons.Waiting, "Starting…", ""), new Profile { Name = "Global" }, false);
    public EnvView Env { get; set; } = EnvView.Empty;
    public PhotoSkin? DualSenseSkin { get; set; }
    public PhotoSkin? XboxSkin { get; set; }

    public event Action<string>? NavigateRequested;
    public event Action? SkinsChanged;

    public void Navigate(string page) => NavigateRequested?.Invoke(page);
    public void RaiseSkinsChanged() => SkinsChanged?.Invoke();

    public void ApplySkins(PadView view)
    {
        view.SetSkin(PadView.PadKind.DualSense, DualSenseSkin);
        view.SetSkin(PadView.PadKind.Xbox, XboxSkin);
    }
}

/// <summary>A page in the shell. Pages are transparent so the window gradient shows between cards.</summary>
internal abstract class Page : Panel
{
    protected Page(ShellContext ctx, string title, string subtitle)
    {
        Ctx = ctx;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
        Dock = DockStyle.Fill;
        Padding = new Padding(0, 0, 0, 20);
        Title = title;
        Subtitle = subtitle;
    }

    protected ShellContext Ctx { get; }
    public string Title { get; }
    public string Subtitle { get; }

    public virtual void OnLive(in LiveView live) { }
    public virtual void OnEnvironment(EnvView env) { }
    public virtual void OnActivated() { }

    /// <summary>Docks controls to the top so they appear in the given order.</summary>
    protected static void StackTop(Control parent, params Control[] topToBottom)
    {
        // WinForms docks the last-added control first (outermost), so add in reverse.
        for (int i = topToBottom.Length - 1; i >= 0; i--)
        {
            topToBottom[i].Dock = DockStyle.Top;
            parent.Controls.Add(topToBottom[i]);
        }
    }

    protected static Control Spacer(int height, Color? back = null) =>
        new Panel { Height = height, BackColor = back ?? Theme.Card };

    protected static Label Caption(string text) => new()
    {
        Text = text.ToUpperInvariant(), AutoSize = false, Height = 24, ForeColor = Theme.Muted, BackColor = Theme.Card,
        Font = new Font("Segoe UI Semibold", 8f), TextAlign = ContentAlignment.BottomLeft,
    };

    protected static Label CardTitle(string text) => new()
    {
        Text = text, AutoSize = false, Height = 28, ForeColor = Theme.Text, BackColor = Theme.Card,
        Font = new Font("Segoe UI Semibold", 11f), TextAlign = ContentAlignment.MiddleLeft,
    };

    protected static Label Body(string text, int height = 20) => new()
    {
        Text = text, AutoSize = false, Height = height, ForeColor = Theme.Muted, BackColor = Theme.Card,
        Font = new Font("Segoe UI", 9f),
    };

    /// <summary>A list row with a toggle on the right; clicking the row toggles too.</summary>
    protected static (ActionRow Row, ToggleSwitch Toggle) ToggleRow(string glyph, string title, string subtitle)
    {
        var row = new ActionRow(glyph, title, subtitle, chevron: false);
        var toggle = new ToggleSwitch { BackColor = Theme.Card };
        row.Controls.Add(toggle);
        row.TrailingWidth = 60;
        row.Layout += (_, _) => toggle.Location = new Point(row.Width - toggle.Width - row.LogicalToDeviceUnits(14), (row.Height - toggle.Height) / 2);
        // Enable/disable the row (its toggle follows as a child), never the toggle alone:
        // a disabled parent would keep the toggle disabled for good.
        row.Click += (_, _) => { if (toggle.Enabled) toggle.Checked = !toggle.Checked; };
        return (row, toggle);
    }

    protected static TableLayoutPanel Grid(int columns, params float[] percents)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, BackColor = Color.Transparent, Margin = Padding.Empty };
        foreach (var p in percents)
            grid.ColumnStyles.Add(p > 0 ? new ColumnStyle(SizeType.Percent, p) : new ColumnStyle(SizeType.Absolute, -p));
        return grid;
    }
}

/// <summary>Small rounded status chip (icon + text) on a card.</summary>
internal sealed class Chip : Control
{
    string _glyph;
    Color _color;

    public Chip(string glyph, string text, Color color)
    {
        _glyph = glyph;
        _color = color;
        Text = text;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        Font = new Font("Segoe UI Semibold", 8.5f);
        Height = 28;
        Margin = new Padding(0, 0, 8, 0);
        FitWidth();
    }

    public void Set(string glyph, string text, Color color)
    {
        if (_glyph == glyph && Text == text && _color == color)
            return;
        _glyph = glyph;
        _color = color;
        Text = text;
        FitWidth();
        Invalidate();
    }

    void FitWidth() => Width = TextRenderer.MeasureText(Text, Font).Width + LogicalToDeviceUnits(40);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        using (var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height / 2f))
        using (var fill = new SolidBrush(Color.FromArgb(40, _color)))
        using (var pen = new Pen(Color.FromArgb(120, _color)))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }
        using (var icon = Theme.IconFont(9f))
            TextRenderer.DrawText(g, _glyph, icon, new Rectangle(LogicalToDeviceUnits(8), 0, LogicalToDeviceUnits(18), Height), _color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(LogicalToDeviceUnits(28), 0, Width - LogicalToDeviceUnits(30), Height), Theme.Text,
            TextFormatFlags.VerticalCenter);
    }
}

/// <summary>
/// "Unsaved changes" bar for the editing pages, plus a chip to pick which profile is being edited.
/// </summary>
internal sealed class EditHeader : Panel
{
    readonly ProfileSession _session;
    readonly OwnerProfilePicker _picker;
    readonly Label _state = new() { AutoSize = true, ForeColor = Theme.Warn, BackColor = Color.Transparent, Font = new Font("Segoe UI Semibold", 9f), Margin = new Padding(0, 8, 12, 0) };
    readonly PillButton _save = new("Save", primary: true);
    readonly PillButton _revert = new("Revert");

    public EditHeader(ProfileSession session)
    {
        _session = session;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 44;

        _picker = new OwnerProfilePicker(session) { Location = new Point(0, 4) };
        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, AutoSize = true, BackColor = Color.Transparent, WrapContents = false,
            Padding = new Padding(0, 4, 0, 0),
        };
        _save.BackColor = Color.Transparent;
        _revert.BackColor = Color.Transparent;
        right.Controls.Add(_state);
        right.Controls.Add(_revert);
        right.Controls.Add(_save);
        Controls.Add(right);
        Controls.Add(_picker);

        _save.Click += (_, _) =>
        {
            if (_session.Save() is { } error)
                MessageBox.Show(FindForm(), error, "Joyy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        _revert.Click += (_, _) => _session.Revert();
        _session.Changed += UpdateState;
        UpdateState();
    }

    void UpdateState()
    {
        _save.Enabled = _session.Dirty;
        _revert.Enabled = _session.Dirty;
        _state.Text = _session.ExternalChangePending ? "config.json changed on disk — Save keeps your edits"
            : _session.Dirty ? "Unsaved changes" : "";
        _picker.Invalidate();
    }
}

/// <summary>Chip showing which profile is being edited; click to switch.</summary>
internal sealed class OwnerProfilePicker : OwnerButton
{
    readonly ProfileSession _session;

    public OwnerProfilePicker(ProfileSession session)
    {
        _session = session;
        BackColor = Color.Transparent;
        Font = new Font("Segoe UI Semibold", 9.25f);
        Size = new Size(300, 34);
        AccessibleName = "Profile being edited";
    }

    protected override void OnClick(EventArgs e)
    {
        var menu = new ContextMenuStrip { Renderer = new Theme.MenuRenderer(), BackColor = Theme.Card, ForeColor = Theme.Text };
        for (int i = 0; i < _session.Profiles.Count; i++)
        {
            int index = i;
            var p = _session.Profiles[i];
            var item = new ToolStripMenuItem(ProfileSession.NameOf(p)) { Checked = i == _session.Current };
            item.Click += (_, _) => _session.Select(index);
            menu.Items.Add(item);
        }
        menu.Show(this, new Point(0, Height));
        base.OnClick(e);
    }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        FillRounded(g, r, Px(8), Hovered ? Theme.Hover : Theme.Card);
        using (var path = Theme.RoundedRect(r, Px(8)))
        using (var pen = new Pen(Theme.Border))
            g.DrawPath(pen, path);
        DrawGlyph(g, Theme.Glyph.Profiles, new Rectangle(Px(8), 0, Px(24), Height), Theme.Accent, 10f);
        using var small = new Font("Segoe UI", 8.5f);
        TextRenderer.DrawText(g, "Editing", small, new Rectangle(Px(36), 0, Px(60), Height), Theme.Muted, TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, ProfileSession.NameOf(_session.Editing), Font, new Rectangle(Px(92), 0, Width - Px(120), Height),
            Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        DrawGlyph(g, "", new Rectangle(Width - Px(28), 0, Px(20), Height), Theme.Muted, 8f);
    }
}
