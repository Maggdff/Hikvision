using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SmartWater.Core;

namespace SmartWater.App;

public partial class MainWindow
{
    // ===================================================== المعايرة
    private List<MeterConfig> _calMeters = new();
    private double _calScale = 1.0;
    private bool _calUi;

    private void LoadCalibration()
    {
        var cfg = _s.Engine.Store.Load();
        _calMeters = cfg.Meters.ToList();
        CalM2.IsEnabled = cfg.MeterCount >= 2;
        if (cfg.MeterCount < 2) CalM1.IsChecked = true;
        CalM1.Checked += (_, _) => SyncRadiusBox();
        CalM2.Checked += (_, _) => SyncRadiusBox();
        _calUi = true;
        SyncRadiusBox();
        CalInfo.Text = "شغّل المراقبة ثم اضغط «التقاط من البث» لتظهر صورة الكاميرا.";
    }

    private int SelMeter => CalM2.IsChecked == true && _calMeters.Count > 1 ? 1 : 0;

    private void SyncRadiusBox()
    {
        if (!_calUi || _calMeters.Count == 0) return;
        _calUi = false;
        CalRadius.Text = Ui.N(_calMeters[SelMeter].Radius);
        _calUi = true;
        DrawMeters();
    }

    private void CalCaptureBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!_s.Engine.SetStillFromLatest()) { MessageBox.Show("لا يوجد إطار حي بعد. شغّل المراقبة وانتظر اتصال الكاميرا."); return; }
        RenderCalib();
    }

    private void RenderCalib()
    {
        int w = _s.Engine.StillWidth, h = _s.Engine.StillHeight;
        if (w == 0) return;
        var crop = _s.Engine.StillCropJpeg(0, 0, w, h, 1100);
        if (crop is not { } c) return;
        var bi = new BitmapImage();
        using (var ms = new MemoryStream(c.Jpeg)) { bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit(); }
        bi.Freeze();
        _calScale = bi.PixelWidth / (double)w;
        CalCanvas.Children.Clear();
        CalCanvas.Width = bi.PixelWidth; CalCanvas.Height = bi.PixelHeight;
        CalCanvas.Children.Add(new Image { Source = bi, Width = bi.PixelWidth, Height = bi.PixelHeight });
        DrawMeters();
        CalInfo.Text = $"إطار {w}×{h}. انقر مركز محور العقرب للعدّاد المحدّد، ثم اضبط نصف القطر ليطابق حافة القرص الصغير.";
    }

    private void DrawMeters()
    {
        if (_s.Engine.StillWidth == 0) return;
        for (int i = CalCanvas.Children.Count - 1; i >= 1; i--) CalCanvas.Children.RemoveAt(i);   // نُبقي الصورة (الأول)
        int n = Math.Min(_calMeters.Count, _s.Engine.Store.Load().MeterCount);
        for (int i = 0; i < n; i++)
        {
            var m = _calMeters[i];
            var brush = i == 0 ? Brushes.Red : Brushes.DeepSkyBlue;
            double r = m.Radius * _calScale, cx = m.Cx * _calScale, cy = m.Cy * _calScale;
            void Ring(double rad, double thick, DoubleCollection? dash = null)
            {
                var el = new Ellipse { Width = rad * 2, Height = rad * 2, Stroke = brush, StrokeThickness = thick, IsHitTestVisible = false };
                if (dash != null) el.StrokeDashArray = dash;
                Canvas.SetLeft(el, cx - rad); Canvas.SetTop(el, cy - rad);
                CalCanvas.Children.Add(el);
            }
            Ring(r, i == SelMeter ? 3 : 1.5);
            Ring(r * 0.33, 1, new DoubleCollection { 3, 3 });     // حلقة قياس العقرب (0.33R–0.52R)
            Ring(r * 0.52, 1, new DoubleCollection { 3, 3 });
            var cross = new TextBlock { Text = $"✚ {m.Name}", Foreground = brush, FontWeight = FontWeights.Bold, IsHitTestVisible = false };
            Canvas.SetLeft(cross, cx - 6); Canvas.SetTop(cross, cy - 10);
            CalCanvas.Children.Add(cross);
        }
    }

    private void CalCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_s.Engine.StillWidth == 0 || _calMeters.Count == 0) return;
        var p = e.GetPosition(CalCanvas);
        int i = SelMeter;
        _calMeters[i] = _calMeters[i] with { Cx = Math.Round(p.X / _calScale, 1), Cy = Math.Round(p.Y / _calScale, 1) };
        DrawMeters();
    }

    private void SetRadius(double r)
    {
        if (_calMeters.Count == 0) return;
        r = Math.Clamp(r, 10, 600);
        int i = SelMeter;
        _calMeters[i] = _calMeters[i] with { Radius = r };
        SyncRadiusBox();
    }

    private void CalRadiusMinus_Click(object sender, RoutedEventArgs e) { if (_calMeters.Count > 0) SetRadius(_calMeters[SelMeter].Radius - 2); }
    private void CalRadiusPlus_Click(object sender, RoutedEventArgs e) { if (_calMeters.Count > 0) SetRadius(_calMeters[SelMeter].Radius + 2); }

    private void CalRadius_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_calUi || _calMeters.Count == 0) return;
        if (Ui.TryNum(CalRadius.Text, out var r) && r >= 10 && r <= 600)
        {
            int i = SelMeter;
            _calMeters[i] = _calMeters[i] with { Radius = r };
            DrawMeters();
        }
    }

    private void CalSaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var cfg = _s.Engine.Store.Load() with { Meters = _calMeters.ToList() };
        var err = _s.Engine.SaveCalibration(cfg);
        if (err != null) { MessageBox.Show(err, "المعايرة", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        StatusText.Text = "حُفظت المعايرة — أُعيد تشغيل المراقبة.";
        _s.StartMonitoring();
    }

    // ===================================================== الإعدادات
    private void LoadSettingsUi()
    {
        var c = _s.Engine.Store.Load();
        SetIp.Text = c.Ip; SetPort.Text = c.HttpPort.ToString(CultureInfo.InvariantCulture);
        SetUser.Text = c.User; SetPass.Password = c.Pass;
        SetChannel.Text = c.Channel.ToString(CultureInfo.InvariantCulture);
        SetTruckCh.Text = c.TruckChannel.ToString(CultureInfo.InvariantCulture);
        SetMeters.SelectedIndex = Math.Clamp(c.MeterCount, 1, 2) - 1;
        SetBpt.Text = Ui.N(c.BarrelsPerTurn);
        SetWiring.SelectedIndex = c.Wiring == 1 ? 1 : 0;
        SetZone.Text = c.TruckZone;
        SetAuto.IsChecked = c.AutoRecognize;
        SetAutoStart.IsChecked = AutoStart.IsEnabled();
    }

    private void SetSaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!Ui.TryNum(SetPort.Text, out var port) || port < 1 || port > 65535) { MessageBox.Show("منفذ HTTP غير صالح."); return; }
        if (!Ui.TryNum(SetChannel.Text, out var ch) || ch < 1) { MessageBox.Show("قناة العدّادات غير صالحة."); return; }
        if (!Ui.TryNum(SetTruckCh.Text, out var tch) || tch < 0) { MessageBox.Show("قناة الوايتات غير صالحة (0 للتعطيل)."); return; }
        if (!Ui.TryNum(SetBpt.Text, out var bpt) || bpt <= 0 || bpt > 10) { MessageBox.Show("«براميل لكل دورة» يجب أن تكون بين 0 و10 (الافتراضي 0.5)."); return; }
        var zone = SetZone.Text.Trim();
        var zp = zone.Split(',').Select(x => Ui.TryNum(x, out var d) ? (double?)d : null).ToList();
        if (zp.Count != 4 || zp.Any(x => x == null) || zp[2] <= 0 || zp[3] <= 0) { MessageBox.Show("منطقة الوايت: أربع نسب مفصولة بفواصل، مثل 44,28,46,47"); return; }
        if (string.IsNullOrWhiteSpace(SetIp.Text)) { MessageBox.Show("اكتب عنوان الكاميرا."); return; }

        var cfg = _s.Engine.Store.Load() with
        {
            Ip = SetIp.Text.Trim(), HttpPort = (int)port, User = SetUser.Text.Trim(), Pass = SetPass.Password,
            Channel = (int)ch, TruckChannel = (int)tch, MeterCount = SetMeters.SelectedIndex + 1, BarrelsPerTurn = bpt,
            Wiring = SetWiring.SelectedIndex, TruckZone = zone, AutoRecognize = SetAuto.IsChecked == true,
        };
        _s.Engine.Store.Save(cfg);
        try { AutoStart.Set(SetAutoStart.IsChecked == true); } catch (Exception ex) { MessageBox.Show("تعذّر ضبط التشغيل التلقائي: " + ex.Message); }
        LoadCalibration();
        StatusText.Text = "حُفظت الإعدادات — أُعيد تشغيل المراقبة.";
        _s.StartMonitoring();
    }

    // ===================================================== الترخيص
    private void RefreshLicenseUi()
    {
        LicInfo.Text = _s.License.Describe();
        if (_s.License.LicenseKeyText is { } k) LicKey.Text = k;
    }

    private async void LicActivateBtn_Click(object sender, RoutedEventArgs e)
    {
        LicActivateBtn.IsEnabled = false;
        try
        {
            var err = await _s.License.ActivateAsync(LicKey.Text);
            RefreshLicenseUi();
            if (err != null) { MessageBox.Show(err, "التفعيل", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            StatusText.Text = "تم التفعيل.";
            if (_s.DesiredRunning) _s.StartMonitoring();
        }
        finally { LicActivateBtn.IsEnabled = true; }
    }

    private async void LicRefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        LicRefreshBtn.IsEnabled = false;
        try { await _s.License.RefreshAsync(force: true); RefreshLicenseUi(); }
        finally { LicRefreshBtn.IsEnabled = true; }
    }

    private void LicDeactivateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("إلغاء التفعيل على هذا الجهاز يوقف المراقبة حتى إدخال مفتاح. السجلات لا تُمسّ. متابعة؟", "تأكيد", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _s.License.Deactivate();
        RefreshLicenseUi(); LicKey.Text = "";
    }
}
