using SD = System.Drawing;
using WF = System.Windows.Forms;

namespace SmartWater.App;

/// <summary>أيقونة منطقة الإشعارات: فتح النافذة، بدء/إيقاف المراقبة، خروج. التطبيق يبقى شغّالًا عند إغلاق النافذة.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WF.NotifyIcon _icon;
    private readonly WF.ToolStripMenuItem _toggle;
    private readonly AppServices _s;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public TrayIcon(AppServices s)
    {
        _s = s;
        _toggle = new WF.ToolStripMenuItem("إيقاف المراقبة");
        _toggle.Click += (_, _) =>
        {
            if (_s.Engine.Running) _s.StopMonitoring(); else _s.StartMonitoring();
            UpdateMenu();
        };
        var open = new WF.ToolStripMenuItem("فتح النافذة");
        open.Click += (_, _) => OpenRequested?.Invoke();
        var exit = new WF.ToolStripMenuItem("خروج (إيقاف المراقبة)");
        exit.Click += (_, _) => ExitRequested?.Invoke();
        var menu = new WF.ContextMenuStrip { RightToLeft = WF.RightToLeft.Yes };
        menu.Items.AddRange(new WF.ToolStripItem[] { open, _toggle, new WF.ToolStripSeparator(), exit });
        menu.Opening += (_, _) => UpdateMenu();
        _icon = new WF.NotifyIcon { Icon = MakeIcon(), Text = "مراقب المياه", Visible = true, ContextMenuStrip = menu };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    private void UpdateMenu() => _toggle.Text = _s.Engine.Running ? "إيقاف المراقبة" : "بدء المراقبة";

    /// <summary>أيقونة قطرة زرقاء مرسومة برمجيًا (بلا ملف أيقونة).</summary>
    private static SD.Icon MakeIcon()
    {
        using var bmp = new SD.Bitmap(32, 32);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(SD.Color.Transparent);
            using var b = new SD.SolidBrush(SD.Color.FromArgb(255, 30, 136, 229));
            g.FillEllipse(b, 6, 11, 20, 20);
            g.FillPolygon(b, new[] { new SD.Point(16, 1), new SD.Point(8, 18), new SD.Point(24, 18) });
        }
        return SD.Icon.FromHandle(bmp.GetHicon());
    }

    public void SetTooltip(string text) => _icon.Text = text.Length > 60 ? text[..60] : text;

    public void Balloon(string text) => _icon.ShowBalloonTip(4000, "مراقب المياه", text, WF.ToolTipIcon.Info);

    public void Dispose() { _icon.Visible = false; _icon.Dispose(); }
}
