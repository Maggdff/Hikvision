using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using SmartWater.Core;
using SmartWater.Data;
using SmartWater.Engine;

namespace SmartWater.App;

public partial class MainWindow : Window
{
    private readonly AppServices _s;
    private readonly Dictionary<long, TruckPromptWindow> _prompts = new();
    private bool _ready;
    private UiState? _state;

    public bool ReallyClosing { get; set; }
    /// <summary>نص يُعرض كإشعار عند اعتماد زفة (يستعمله شريط المهام).</summary>
    public event Action<string>? Swung;

    public MainWindow(AppServices s)
    {
        _s = s;
        InitializeComponent();
        RecDate.SelectedDate = DateTime.Today;
        WireEngine();
        LoadSettingsUi(); LoadEbsUi(); RefreshLicenseUi(); LoadCalibration();
        RefreshTrucks(); RefreshRecords(); RefreshInvoices();
        _ready = true;
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        t.Tick += (_, _) => UiTick();
        t.Start();
        Closing += (_, e) => { if (!ReallyClosing) { e.Cancel = true; Hide(); } };   // الإغلاق = إخفاء إلى شريط المهام
    }

    private void UI(Action a) => Dispatcher.BeginInvoke(a);

    private void WireEngine()
    {
        var e = _s.Engine;
        e.StateChanged += st => UI(() => ShowState(st));
        e.Fill += f => UI(() => ShowFill(f));
        e.Swing += txt => UI(() => { LastSwingText.Text = txt; RefreshRecords(); Swung?.Invoke(txt.Split('\n')[0]); });
        e.Status += m => UI(() => StatusText.Text = m);
        e.TruckFlow.Prompt += p => UI(() => ShowPrompt(p));
        e.TruckFlow.PromptClosed += id => UI(() => ClosePrompt(id));
    }

    public string TooltipText()
    {
        var e = _s.Engine;
        string st = !e.Running ? "متوقف" : e.Connected ? "يعمل" : "غير متصل";
        return $"مراقب المياه — {st} — اليوم {Ui.N(_s.Engine.Swings.DayTotal(_s.Engine.Swings.Today()))} برميل";
    }

    // ===================================================== المراقبة
    private void ShowState(UiState st)
    {
        _state = st;
        double dpb = _s.Engine.Config.DegPerBarrel;
        MetersText.Text = string.Join("\n", st.Meters.Select(m =>
            $"{m.Name}: {(m.Valid ? "قراءة سليمة" : "لا قراءة")}{(m.Active ? " — يعدّ الآن" : "")} — الزفة الحالية {(m.EpisodeDeg / dpb).ToString("0.00", CultureInfo.InvariantCulture)} برميل"));
    }

    private void ShowFill(FillStatus f)
    {
        FillLabel.Text = f.Label;
        var parts = new List<string>();
        if (f.TruckName.Length > 0) parts.Add($"الوايت: {f.TruckName}");
        if (f.Total > 0) parts.Add($"المقاس: {f.Total.ToString("0.00", CultureInfo.InvariantCulture)} برميل");
        if (f.GraceRemainingSec is { } g) parts.Add($"المهلة المتبقية: {g:0} ث");
        if (f.Note.Length > 0) parts.Add(f.Note);
        FillNote.Text = string.Join(" • ", parts);
    }

    private void UiTick()
    {
        var e = _s.Engine;
        ConnText.Text = !e.Running ? "المراقبة متوقفة"
            : e.Connected ? $"متصل بالكاميرا — {(_state?.Fps ?? 0).ToString("0.0", CultureInfo.InvariantCulture)} إطار/ث"
            : "جارٍ الاتصال بالكاميرا (يعيد المحاولة تلقائيًا)";
        TodayText.Text = $"إجمالي اليوم: {Ui.N(e.Swings.DayTotal(e.Swings.Today()))} برميل";
        var v = _s.License.Verdict();
        string banner = v.Blocked ? $"{v.Title}: {v.Message}" : v.Banner;
        BannerBox.Visibility = banner.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        BannerBox.Background = v.Blocked ? System.Windows.Media.Brushes.MistyRose : System.Windows.Media.Brushes.LemonChiffon;
        BannerText.Text = banner;
    }

    private void StartBtn_Click(object sender, RoutedEventArgs e) => _s.StartMonitoring();
    private void StopBtn_Click(object sender, RoutedEventArgs e) => _s.StopMonitoring();

    private void ResetBgBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!_s.Engine.ResetPresenceBackground()) { MessageBox.Show("شغّل المراقبة أولًا."); return; }
        StatusText.Text = "سيُعتمد إطار قناة الوايتات التالي خلفيةً فارغة — تأكد أن منطقة القصبة خالية الآن.";
    }

    private void ShowPrompt(TruckPrompt p)
    {
        if (_prompts.ContainsKey(p.EpisodeId)) return;
        var w = new TruckPromptWindow(p, _s.Engine.TruckFlow);
        _prompts[p.EpisodeId] = w;
        w.Closed += (_, _) => { _prompts.Remove(p.EpisodeId); RefreshRecords(); RefreshTrucks(); };
        w.Show();
    }

    private void ClosePrompt(long id) { if (_prompts.TryGetValue(id, out var w)) w.Close(); }

    // ===================================================== السجل
    private string? SelectedDay() => AllDaysCheck.IsChecked == true ? null : (RecDate.SelectedDate ?? DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private void RecFilter_Changed(object sender, RoutedEventArgs e) { if (_ready) RefreshRecords(); }
    private void RecDate_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) RefreshRecords(); }
    private void RecRefreshBtn_Click(object sender, RoutedEventArgs e) => RefreshRecords();

    private void RefreshRecords()
    {
        if (!_ready && RecGrid == null) return;
        var recs = _s.Engine.Swings.Query(SelectedDay());
        RecGrid.ItemsSource = recs.Select(r =>
        {
            var inv = _s.Ebs.InvoiceOfSwing(r.Id);
            return new RecordRow(r.Id, r.Seq, r.Day, r.TypeAr, r.Meters, Ui.Time(r.StartMs), Ui.Time(r.EndMs),
                Ui.N(Math.Round(r.M1Barrels, 3)), Ui.N(Math.Round(r.M2Barrels, 3)), Ui.N(Math.Round(r.Total, 3)),
                Ui.N(Math.Round(r.Measured, 3)), r.TruckName, inv == null ? "" : Ui.InvStatus(inv.Status), r.Notes, r.TruckId);
        }).ToList();
        RecTotalText.Text = $"الإجمالي: {Ui.N(Math.Round(recs.Sum(r => r.Total), 3))} برميل";
    }

    private async void AssignBtn_Click(object sender, RoutedEventArgs e)
    {
        if (RecGrid.SelectedItem is not RecordRow row) { MessageBox.Show("اختر زفة من الجدول."); return; }
        if (RecTruckCombo.SelectedItem is not TruckChoice tc) { MessageBox.Show("اختر وايتًا من القائمة."); return; }
        StatusText.Text = await _s.Engine.TruckFlow.AssignRecordAsync(row.Id, tc.Id);
        RefreshRecords(); RefreshTrucks();
    }

    private void ExportBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"زفات-{DateTime.Today:yyyy-MM-dd}.csv" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            using var fs = File.Create(dlg.FileName);
            _s.Engine.Swings.ExportCsv(fs);
            StatusText.Text = "صُدّر السجل: " + dlg.FileName;
        }
        catch (Exception ex) { MessageBox.Show("تعذّر التصدير: " + ex.Message); }
    }

    // ===================================================== الوايتات
    private void RefreshTrucks()
    {
        var list = _s.Engine.Trucks.List();
        TruckGrid.ItemsSource = list.Select(t => new TruckRow(t.Id, t.Name, Ui.N(t.Capacity), t.Photos, t.Capacity)).ToList();
        RecTruckCombo.ItemsSource = list.Select(t => new TruckChoice(t.Id, $"{t.Name} — {Ui.N(t.Capacity)}")).ToList();
        TruckName.ItemsSource = _s.Ebs.Customers().Select(c => c.Name).ToList();      // أسماء عملاء EBS للاختيار الدقيق
    }

    private void TruckGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TruckGrid.SelectedItem is TruckRow r) { TruckName.Text = r.Name; TruckCap.Text = Ui.N(r.CapacityValue); }
    }

    private void TruckAddBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!Ui.TryNum(TruckCap.Text, out var cap)) { MessageBox.Show("اكتب الكمية بالبراميل."); return; }
        try { _s.Engine.Trucks.Add(TruckName.Text, cap); RefreshTrucks(); TruckName.Text = ""; TruckCap.Text = ""; }
        catch (TruckInputError ex) { MessageBox.Show(ex.Message); }
    }

    private void TruckUpdBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TruckGrid.SelectedItem is not TruckRow r) { MessageBox.Show("اختر وايتًا من الجدول."); return; }
        if (!Ui.TryNum(TruckCap.Text, out var cap)) { MessageBox.Show("اكتب الكمية بالبراميل."); return; }
        try
        {
            var t = _s.Engine.Trucks.Update(r.Id, TruckName.Text, cap);
            _s.Engine.TruckFlow.Renamed(t);
            RefreshTrucks(); RefreshRecords();
            StatusText.Text = "الكمية الجديدة تُطبَّق على الزفات القادمة فقط؛ السجلات القديمة لا تتغيّر.";
        }
        catch (TruckInputError ex) { MessageBox.Show(ex.Message); }
    }

    private void TruckDelBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TruckGrid.SelectedItem is not TruckRow r) { MessageBox.Show("اختر وايتًا من الجدول."); return; }
        if (MessageBox.Show($"حذف «{r.Name}»؟ تبقى أسماؤه في السجلات القديمة.", "تأكيد", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _s.Engine.TruckFlow.Deleted(r.Id);
        _s.Engine.Trucks.Delete(r.Id);
        RefreshTrucks(); RefreshRecords();
    }
}
