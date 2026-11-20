using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SmartWater.Gate;

namespace SmartWater.App;

public partial class MainWindow
{
    private string? _learnedHash;
    private int _gateTick;
    private bool _gateRefreshing;   // لا نملأ نموذج الصندوق من التحديد أثناء التحديث الدوري (حتى لا يُمحى ما يكتبه المستخدم)

    private GateService? Gate => _s.Gate;

    private void LoadGateUi()
    {
        var gs = _s.GateSettings;
        GateEnabledBox.IsChecked = gs.Enabled; GateBindBox.Text = gs.BindIp; GatePortBox.Text = gs.Port.ToString(CultureInfo.InvariantCulture);
        GatePolicyBox.SelectedIndex = (int)gs.Policy; GateCreditBox.Text = Ui.N(gs.CreditLimit);
        RefreshGateUi(true);
        RefreshLimits();
    }

    /// <summary>تحديث الجداول (الصناديق/البطاقات/السجل) مع الحفاظ على التحديد؛ القوائم المنسدلة عند الطلب فقط.</summary>
    private void RefreshGateUi(bool lists = false)
    {
        bool on = Gate != null;
        GateStatusText.Text = on ? $"الجسر يعمل على {_s.GateSettings.BindIp}:{_s.GateSettings.Port}" : "الجسر متوقف";
        GateStartBtn.IsEnabled = !on; GateStopBtn.IsEnabled = on;
        var db = _s.GateDb;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        int? selBox = (BoxGrid.SelectedItem as BoxRow)?.Id;
        var rows = db.Boxes().Select(b =>
        {
            var dev = db.Device(b.DeviceId);
            var des = db.GetDesired(b.Id);
            var op = db.ActiveOpOfBox(b.Id);
            bool online = dev != null && dev.LastSeenMs > 0 && now - dev.LastSeenMs <= 20_000;
            string lockTxt = $"{LockAr(dev?.Actual ?? LockState.Unknown)} / {LockAr(des.State)}";
            return new BoxRow(b.Id, b.Name, b.StationId, b.TapNo, b.MeterIndex + 1, b.DeviceId, b.Enabled,
                dev == null ? "غير مسجَّل" : online ? "متصل" : "غير متصل", lockTxt,
                dev == null ? "" : dev.ReaderOk ? "سليم" : "عطل", op == null ? "" : $"{op.Customer} — {OpAr(op.State)}");
        }).ToList();
        _gateRefreshing = true;
        try
        {
            BoxGrid.ItemsSource = rows;
            if (selBox != null) BoxGrid.SelectedItem = rows.FirstOrDefault(r => r.Id == selBox);
        }
        finally { _gateRefreshing = false; }

        var trucks = _s.Engine.Trucks.List().ToDictionary(t => t.Id);
        CardGrid.ItemsSource = db.Cards().Select(c => new CardRow(c.Id, c.Label, c.Driver,
            trucks.TryGetValue(c.TruckId, out var t) ? t.Name : "(محذوف)", c.Status == CardStatus.Active ? "فعّالة" : "ملغاة")).ToList();

        bool faultsOnly = GateFaultsOnly.IsChecked == true;
        var boxNames = db.Boxes().ToDictionary(b => b.Id, b => b.Name);
        GateEventGrid.ItemsSource = db.Events(300).Where(e => !faultsOnly || e.Kind.StartsWith("fault") || e.Kind.StartsWith("deny")).Select(e =>
            new GateEventRow(e.Id, DateTimeOffset.FromUnixTimeMilliseconds(e.TsMs).ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                e.Kind.StartsWith("deny.") ? "رفض: " + GateService.ReasonArabic(e.Kind[5..]) : e.Kind,
                e.BoxId is int bid && boxNames.TryGetValue(bid, out var bn) ? bn : "", e.Data)).ToList();

        if (lists)
        {
            CardTruckCombo.ItemsSource = _s.Engine.Trucks.List().Select(t => new TruckChoice(t.Id, $"{t.Name} — {Ui.N(t.Capacity)}")).ToList();
        }
    }

    private static string LockAr(LockState s) => s switch { LockState.Locked => "مقفل", LockState.Unlocked => "مفتوح", _ => "؟" };
    private static string OpAr(OpState s) => s switch { OpState.Authorized => "مصرَّح", OpState.Filling => "تعبئة", OpState.Locking => "قيد القفل", _ => "مغلقة" };

    /// <summary>يُستدعى كل ثانية من UiTick: التحديث الدوري وقراءة بطاقة وضع التعلّم.</summary>
    private void GateTick()
    {
        if (Tabs.SelectedItem != GateTab) return;
        if (++_gateTick % 2 == 0) RefreshGateUi();
        if (Gate != null)
        {
            var (active, hash) = Gate.LearningState();
            if (hash != null && hash != _learnedHash)
            {
                _learnedHash = hash;
                CardLearnInfo.Text = $"التُقطت بطاقة (بصمتها ...{hash[^6..]}). اختر الوايت واضغط «تسجيل البطاقة الملتقطة».";
            }
            else if (active) CardLearnInfo.Text = "بانتظار تمرير البطاقة على الصندوق المحدد ...";
        }
    }

    private async void GateStartBtn_Click(object sender, RoutedEventArgs e)
    {
        GateStartBtn.IsEnabled = false;
        var err = await _s.StartGateAsync();
        if (err != null) MessageBox.Show("تعذّر تشغيل الجسر:\n" + err, "الجسر", MessageBoxButton.OK, MessageBoxImage.Warning);
        RefreshGateUi();
    }

    private async void GateStopBtn_Click(object sender, RoutedEventArgs e) { await _s.StopGateAsync(); RefreshGateUi(); }
    private void GateRefreshBtn_Click(object sender, RoutedEventArgs e) { RefreshGateUi(true); RefreshLimits(); }

    private void GateSettingsSaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!Ui.TryNum(GatePortBox.Text, out var port) || port < 1 || port > 65535) { MessageBox.Show("المنفذ غير صالح."); return; }
        if (!System.Net.IPAddress.TryParse(GateBindBox.Text.Trim(), out _)) { MessageBox.Show("عنوان الشبكة غير صالح (مثل 192.168.10.10)."); return; }
        if (!Ui.TryNum(GateCreditBox.Text, out var credit) || credit < 0) { MessageBox.Show("حد الائتمان غير صالح."); return; }
        var gs = _s.GateSettings with
        {
            Enabled = GateEnabledBox.IsChecked == true, BindIp = GateBindBox.Text.Trim(), Port = (int)port,
            Policy = (BalancePolicy)Math.Max(0, GatePolicyBox.SelectedIndex), CreditLimit = credit,
        };
        _s.SaveGateSettings(gs);
        StatusText.Text = "حُفظت إعدادات الجسر.";
        if (gs.Enabled) GateStartBtn_Click(sender, e); else GateStopBtn_Click(sender, e);
    }

    // ---------------- المحطات والصناديق
    private void StationSaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!Ui.TryNum(StationIdBox.Text, out var id) || id < 1 || string.IsNullOrWhiteSpace(StationNameBox.Text)) { MessageBox.Show("اكتب رقم المحطة واسمها."); return; }
        _s.GateDb.UpsertStation(new Station((int)id, StationNameBox.Text.Trim(), StationSeriesBox.IsChecked == true));
        StatusText.Text = "حُفظت المحطة.";
    }

    private void BoxSaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!Ui.TryNum(BoxIdBox.Text, out var id) || id < 1 || !Ui.TryNum(BoxStationBox.Text, out var st) || !Ui.TryNum(BoxTapBox.Text, out var tap)
            || !Ui.TryNum(BoxMeterBox.Text, out var meter) || meter < 1 || string.IsNullOrWhiteSpace(BoxDeviceBox.Text) || string.IsNullOrWhiteSpace(BoxNameBox.Text))
        { MessageBox.Show("أكمل الحقول: رقم الصندوق، الاسم، المحطة، الحنفية، العدّاد (1 أو 2)، معرّف الجهاز."); return; }
        if (!_s.GateDb.Stations().Any(s => s.Id == (int)st)) { MessageBox.Show("المحطة غير موجودة — احفظها أولًا."); return; }
        try
        {
            _s.GateDb.UpsertBox(new Box((int)id, BoxNameBox.Text.Trim(), (int)st, (int)tap, (int)meter - 1, BoxDeviceBox.Text.Trim(), BoxEnabledBox.IsChecked == true));
            RefreshGateUi();
        }
        catch (Exception ex) { MessageBox.Show("تعذّر الحفظ (معرّف الجهاز مستعمل لصندوق آخر؟):\n" + ex.Message); }
    }

    private void BoxGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_gateRefreshing || BoxGrid.SelectedItem is not BoxRow r) return;
        BoxIdBox.Text = r.Id.ToString(); BoxNameBox.Text = r.Name; BoxStationBox.Text = r.Station.ToString();
        BoxTapBox.Text = r.Tap.ToString(); BoxMeterBox.Text = r.Meter.ToString(); BoxDeviceBox.Text = r.Device; BoxEnabledBox.IsChecked = r.Enabled;
    }

    private void DeviceKeyBtn_Click(object sender, RoutedEventArgs e)
    {
        var id = BoxDeviceBox.Text.Trim();
        if (id.Length == 0) { MessageBox.Show("اكتب معرّف الجهاز أولًا."); return; }
        if (Gate == null) { MessageBox.Show("شغّل الجسر أولًا."); return; }
        if (_s.GateDb.Device(id) != null &&
            MessageBox.Show("الجهاز مسجَّل. المتابعة تستبدل مفتاحه فيتوقف حتى تُحقن المفتاح الجديد فيه. متابعة؟", "تأكيد", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var key = Gate.AddDevice(id);
        InputDialog.Ask("مفتاح الجهاز " + id, "انسخ المفتاح الآن واحقنه في الجهاز عبر الكبل التسلسلي. لن يُعرض مرة أخرى:", Convert.ToHexString(key).ToLowerInvariant(), readOnly: true, owner: this);
        RefreshGateUi();
    }

    private void BoxUnlockBtn_Click(object sender, RoutedEventArgs e) => Manual(true);
    private void BoxLockBtn_Click(object sender, RoutedEventArgs e) => Manual(false);

    private void Manual(bool unlock)
    {
        if (Gate == null) { MessageBox.Show("شغّل الجسر أولًا."); return; }
        if (BoxGrid.SelectedItem is not BoxRow r) { MessageBox.Show("اختر صندوقًا من الجدول."); return; }
        var reason = InputDialog.Ask(unlock ? "فتح يدوي" : "قفل يدوي", "اكتب سبب الإجراء (يُسجَّل في التدقيق):", "", owner: this);
        if (string.IsNullOrWhiteSpace(reason)) return;
        if (unlock) Gate.ManualUnlock(r.Id, reason.Trim(), Environment.UserName); else Gate.ManualLock(r.Id, reason.Trim(), Environment.UserName);
        RefreshGateUi();
    }

    // ---------------- البطاقات
    private void CardLearnBtn_Click(object sender, RoutedEventArgs e)
    {
        if (Gate == null) { MessageBox.Show("شغّل الجسر أولًا."); return; }
        if (BoxGrid.SelectedItem is not BoxRow r) { MessageBox.Show("اختر الصندوق من تبويب «الصناديق والمحطات» أولًا."); return; }
        _learnedHash = null;
        Gate.StartLearning(r.Id, 60);
        CardLearnInfo.Text = $"مرّر البطاقة على «{r.Name}» خلال 60 ثانية ...";
    }

    private void CardSaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (Gate == null) { MessageBox.Show("شغّل الجسر أولًا."); return; }
        if (_learnedHash == null) { MessageBox.Show("لم تُلتقط بطاقة بعد. اضغط «تعلّم بطاقة» ثم مرّرها."); return; }
        if (CardTruckCombo.SelectedItem is not TruckChoice t) { MessageBox.Show("اختر الوايت."); return; }
        try
        {
            Gate.RegisterCardByHash(_learnedHash, CardLabelBox.Text.Trim().Length > 0 ? CardLabelBox.Text.Trim() : "بطاقة", CardDriverBox.Text.Trim(), t.Id);
            _learnedHash = null; CardLearnInfo.Text = "سُجّلت البطاقة."; CardLabelBox.Text = ""; CardDriverBox.Text = "";
            RefreshGateUi();
        }
        catch (InvalidOperationException ex) { MessageBox.Show(ex.Message); }
    }

    private void CardRevokeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (Gate == null) { MessageBox.Show("شغّل الجسر أولًا."); return; }
        if (CardGrid.SelectedItem is not CardRow c) { MessageBox.Show("اختر بطاقة من الجدول."); return; }
        if (MessageBox.Show($"إلغاء البطاقة «{c.Label}»؟ لا يمكنها بعد ذلك فتح أي صندوق.", "تأكيد", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        Gate.RevokeCard(c.Id);
        RefreshGateUi();
    }

    // ---------------- سقوف العملاء
    private void RefreshLimits()
    {
        var db = _s.GateDb;
        long? cur = _s.Ebs.CurrencyId(_s.EbsSettings.Currency);
        LimitCustomerCombo.ItemsSource = _s.Ebs.Customers().Select(c => c.Name).ToList();
        LimitGrid.ItemsSource = db.CustomerLimits().Select(l =>
        {
            var cust = _s.Ebs.FindCustomer(l.Customer);
            double? bal = cust?.AccountId is long acc ? _s.Ebs.SnapshotCredit(acc, cur) : null;
            return new LimitRow(l.Customer, Ui.N(l.Limit), bal == null ? "—" : Ui.N(bal.Value));
        }).ToList();
    }

    private void LimitGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LimitGrid.SelectedItem is LimitRow r) { LimitCustomerCombo.Text = r.Customer; LimitValueBox.Text = r.Limit; }
    }

    private void LimitSaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var name = LimitCustomerCombo.Text.Trim();
        if (name.Length == 0) { MessageBox.Show("اختر العميل أو اكتب اسمه كما في EBS."); return; }
        if (!Ui.TryNum(LimitValueBox.Text, out var limit) || limit < 0) { MessageBox.Show("اكتب سقفًا صحيحًا (0 أو أكثر)."); return; }
        if (_s.Ebs.CustomerCount() > 0 && _s.Ebs.FindCustomer(name) == null &&
            MessageBox.Show($"«{name}» ليس في قائمة عملاء EBS المحمَّلة. تأكد من الاسم (يجب أن يطابق اسم الوايت). حفظ على أي حال؟", "تنبيه", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (Gate != null) Gate.SetCustomerLimit(name, limit);
        else { _s.GateDb.SetCustomerLimit(name, limit); _s.GateDb.Log(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "admin.limit_set", data: $"{name} = {Ui.N(limit)}"); }
        RefreshLimits();
        StatusText.Text = $"حُفظ سقف «{name}».";
    }

    private void LimitDeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        var name = LimitCustomerCombo.Text.Trim();
        if (name.Length == 0 || _s.GateDb.CustomerLimit(name) == null) { MessageBox.Show("اختر عميلًا له سقف خاص."); return; }
        if (Gate != null) Gate.RemoveCustomerLimit(name);
        else { _s.GateDb.RemoveCustomerLimit(name); _s.GateDb.Log(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "admin.limit_removed", data: name); }
        RefreshLimits();
        StatusText.Text = $"حُذف السقف الخاص لـ«{name}»؛ يُطبَّق الافتراضي.";
    }
}
