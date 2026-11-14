using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SmartWater.Ebs;

namespace SmartWater.App;

public partial class MainWindow
{
    // ===================================================== EBS: الإعدادات والتحميل والمزامنة
    private Button[] EbsButtons => new[] { EbsSaveBtn, EbsLoginBtn, EbsDownloadBtn, EbsQueueBtn, EbsSyncAllBtn, EbsSyncOneBtn, EbsConfirmBtn, EbsRefreshBtn };

    private void LoadEbsUi()
    {
        var s = _s.EbsSettings;
        EbsUrl.Text = s.BaseUrl; EbsUser.Text = s.UserName; EbsPass.Password = s.Password;
        EbsYear.Text = s.Year.ToString(CultureInfo.InvariantCulture);
        EbsBranchName.Text = s.BranchName;
        EbsBranchId.Text = s.BranchId > 0 ? s.BranchId.ToString(CultureInfo.InvariantCulture) : "";
        EbsUserId.Text = s.UserId > 0 ? s.UserId.ToString(CultureInfo.InvariantCulture) : "";
        EbsCashier.Text = s.CashierDisplayName; EbsCurrency.Text = s.Currency; EbsPayment.Text = s.DefaultPaymentType;
        EbsStore.Text = s.DefaultStoreName; EbsUnit.Text = s.WaterUnitName; EbsPrice.Text = s.DefaultBarrelPrice;
        UpdateEbsInfo();
    }

    private EbsSettings ReadEbsUi()
    {
        var old = _s.EbsSettings;
        int Int(string t, int dflt) => Ui.TryNum(t, out var v) ? (int)v : dflt;
        string price = Ui.TryNum(EbsPrice.Text, out var pv) ? Ui.N(pv) : EbsPrice.Text.Trim();
        return old with
        {
            BaseUrl = EbsUrl.Text.Trim(), UserName = EbsUser.Text.Trim(), Password = EbsPass.Password,
            Year = Int(EbsYear.Text, old.Year), BranchName = EbsBranchName.Text.Trim(),
            BranchId = Int(EbsBranchId.Text, 0), UserId = Int(EbsUserId.Text, 0), CashierDisplayName = EbsCashier.Text.Trim(),
            Currency = EbsCurrency.Text.Trim(), DefaultPaymentType = EbsPayment.Text.Trim(), DefaultStoreName = EbsStore.Text.Trim(),
            WaterUnitName = EbsUnit.Text.Trim(), DefaultBarrelPrice = price,
        };
    }

    private void UpdateEbsInfo() => EbsInfo.Text = $"العملاء المحمَّلون: {_s.Ebs.CustomerCount()}";

    private void SetEbsBusy(bool busy) { foreach (var b in EbsButtons) b.IsEnabled = !busy; }

    private async Task EbsRun(Func<Task> work)
    {
        SetEbsBusy(true);
        try { await work(); }
        catch (Exception ex) { FileLog.Write("ERROR", "EBS: " + ex); MessageBox.Show("حدث خطأ: " + ex.Message, "EBS", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetEbsBusy(false); UpdateEbsInfo(); RefreshInvoices(); RefreshRecords(); RefreshTrucks(); }
    }

    private void EbsSaveBtn_Click(object sender, RoutedEventArgs e)
    {
        _s.SaveEbsSettings(ReadEbsUi());
        EbsInfo.Text = "حُفظت الإعدادات (مشفّرة على هذا الجهاز).";
    }

    private async void EbsLoginBtn_Click(object sender, RoutedEventArgs e) => await EbsRun(async () =>
    {
        _s.SaveEbsSettings(ReadEbsUi());
        var r = await _s.EbsClient.LoginAsync(_s.EbsSettings);
        EbsInfo.Text = r.Success ? $"تم الدخول بنجاح — الموزع: {r.DistributorName}" : $"فشل الدخول: {r.ErrorMessage}";
    });

    private async void EbsDownloadBtn_Click(object sender, RoutedEventArgs e) => await EbsRun(async () =>
    {
        _s.SaveEbsSettings(ReadEbsUi());
        var r = await new CustomerDownloadService(_s.Ebs, _s.EbsClient).DownloadAsync(_s.EbsSettings);
        EbsInfo.Text = r.Success ? $"حُمّل {r.Count} عميلًا و{r.BalanceCount} رصيدًا." : $"فشل التحميل: {r.Error}";
    });

    private void EbsRefreshBtn_Click(object sender, RoutedEventArgs e) { RefreshInvoices(); UpdateEbsInfo(); }

    private void RefreshInvoices()
    {
        InvGrid.ItemsSource = _s.Ebs.Invoices(pendingOnly: false).Select(i =>
            new InvRow(i.LocalId, i.CustomerName, Ui.N(i.Barrels), Ui.N(i.Price), Ui.N(Math.Round(i.Total, 3)),
                       Ui.InvStatus(i.Status), i.Attempts, i.LastError ?? "")).ToList();
    }

    private async void EbsQueueBtn_Click(object sender, RoutedEventArgs e) => await EbsRun(() =>
    {
        _s.SaveEbsSettings(ReadEbsUi());
        var queue = new EbsInvoiceQueue(_s.Ebs);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var counts = new Dictionary<EnqueueStatus, int>();
        string? firstProblem = null;
        foreach (var r in _s.Engine.Swings.Query(null).Where(x => x.TruckId != null).OrderBy(x => x.EndMs))
        {
            var res = queue.Enqueue(r, _s.EbsSettings, now);
            counts[res.Status] = counts.GetValueOrDefault(res.Status) + 1;
            if (res.Status is not (EnqueueStatus.Queued or EnqueueStatus.AlreadyQueued or EnqueueStatus.TooRecent)) firstProblem ??= $"زفة #{r.Seq} ({r.Day}): {res.Message}";
        }
        EbsInfo.Text = $"أُضيف {counts.GetValueOrDefault(EnqueueStatus.Queued)} للطابور" +
                       $"، موجود مسبقًا {counts.GetValueOrDefault(EnqueueStatus.AlreadyQueued)}" +
                       $"، حديث جدًا {counts.GetValueOrDefault(EnqueueStatus.TooRecent)}" +
                       (firstProblem != null ? $"\nتنبيه: {firstProblem}" : "");
        return Task.CompletedTask;
    });

    private InvoiceSyncService NewSync() => new(_s.Ebs, _s.EbsClient, () => _s.EbsSettings, autoConfirmDuplicates: false);

    private async void EbsSyncAllBtn_Click(object sender, RoutedEventArgs e)
    {
        int pending = _s.Ebs.Invoices(pendingOnly: true).Count;
        if (pending == 0) { MessageBox.Show("لا توجد فواتير معلّقة."); return; }
        if (MessageBox.Show($"سيُرسَل {pending} فاتورة إلى EBS بالترتيب (تتوقف عند أول فشل). متابعة؟", "مزامنة EBS", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        await EbsRun(async () =>
        {
            _s.SaveEbsSettings(ReadEbsUi());
            var results = await NewSync().SyncAllOrderedAsync();
            int ok = results.Count(r => r.Success);
            var bad = results.FirstOrDefault(r => !r.Success);
            EbsInfo.Text = $"أُرسلت {ok} من {pending}." + (bad != null ? $"\nتوقفت عند خطأ: {bad.Error}" + (bad.IsDuplicateWarning ? "\n(سجل مكرر: راجع EBS ثم استعمل «تأكيد سجل مكرر»)" : "") : "");
        });
    }

    private string? SelectedInvoiceId() => (InvGrid.SelectedItem as InvRow)?.LocalId;

    private async void EbsSyncOneBtn_Click(object sender, RoutedEventArgs e)
    {
        var id = SelectedInvoiceId();
        if (id == null) { MessageBox.Show("اختر فاتورة من الجدول."); return; }
        await EbsRun(async () =>
        {
            _s.SaveEbsSettings(ReadEbsUi());
            var r = await NewSync().SyncOneAsync(id);
            EbsInfo.Text = r.Success ? "تمت المزامنة." : $"فشلت: {r.Error}";
        });
    }

    private async void EbsConfirmBtn_Click(object sender, RoutedEventArgs e)
    {
        var id = SelectedInvoiceId();
        if (id == null) { MessageBox.Show("اختر فاتورة من الجدول."); return; }
        if (_s.Ebs.Invoice(id)?.Status != InvoiceStatus.NeedsConfirm) { MessageBox.Show("هذه الفاتورة ليست بانتظار التأكيد."); return; }
        if (MessageBox.Show("EBS حذّر أن سجلًا بنفس البيانات موجود. إن كانت هذه فاتورة جديدة فعلًا (وليست إعادة لفاتورة أُرسلت قبل انقطاع) فأكّد الإرسال. هل تؤكد؟",
                "سجل مكرر", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await EbsRun(async () =>
        {
            var r = await NewSync().SyncOneAsync(id, confirmDuplicate: true);
            EbsInfo.Text = r.Success ? "تمت المزامنة." : $"فشلت: {r.Error}";
        });
    }
}
