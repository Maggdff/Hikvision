using System.Text;
using SmartWater.Core;
using SmartWater.Data;
using Xunit;

namespace SmartWater.Tests;

public class TruckDbTests
{
    [Fact]
    public void Add_rejects_bad_input_and_case_insensitive_duplicates()
    {
        using var db = new TruckDb(":memory:");
        Assert.Throws<TruckInputError>(() => db.Add("  ", 10));
        Assert.Throws<TruckInputError>(() => db.Add("وايت", 0));
        Assert.Throws<TruckInputError>(() => db.Add("وايت", 5000));
        var t = db.Add("  Abu   Ali ", 12.5);
        Assert.Equal("Abu Ali", t.Name);
        var e = Assert.Throws<TruckInputError>(() => db.Add("ABU ALI", 8));
        Assert.Contains("موجود مسبقًا", e.Message);
    }

    [Fact]
    public void Update_delete_and_list_order()
    {
        using var db = new TruckDb(":memory:");
        var b = db.Add("بيت", 10); var a = db.Add("ألف", 5);
        Assert.Equal(new[] { "ألف", "بيت" }, db.List().Select(t => t.Name).ToArray());
        var u = db.Update(b.Id, "بيت جديد", 11);
        Assert.Equal(11, u.Capacity);
        Assert.Throws<TruckInputError>(() => db.Update(a.Id, "بيت جديد", 5));          // اسم مكرر
        Assert.Throws<TruckInputError>(() => db.Update(999, "x", 5));                    // غير موجود
        db.Delete(a.Id);
        Assert.Single(db.List());
    }

    [Fact]
    public void Photos_keep_only_latest_10_and_never_duplicate_a_path()
    {
        using var db = new TruckDb(":memory:");
        var t = db.Add("وايت", 10);
        var f = new float[TruckFeatures.Dim]; f[0] = 1f;
        for (int i = 0; i < 14; i++) db.AddPhoto(t.Id, $"p{i}.jpg", f);
        db.AddPhoto(t.Id, "p13.jpg", f);
        Assert.Equal(TruckDb.MaxPhotosPerTruck, db.PhotoPaths(t.Id).Count);
        Assert.Equal("p13.jpg", db.PhotoPaths(t.Id)[0]);
        Assert.Equal(10, db.Samples().Count);
        Assert.Equal(10, db.Get(t.Id)!.Photos);
        db.Delete(t.Id);
        Assert.Empty(db.Samples());
    }
}

public class SwingDbTests
{
    private static SwingResult Res(double deg0, double? deg1 = null, string notes = "")
    {
        var parts = new List<MeterPart> { new(0, deg0, 0, 10) };
        if (deg1 != null) parts.Add(new MeterPart(1, deg1.Value, 0, 10));
        return new SwingResult(parts.Count > 1 ? SwingType.Full : SwingType.Meter, parts, notes);
    }

    private const long T0 = 1_800_000_000_000L;

    [Fact]
    public void Insert_numbers_swings_per_day_and_stores_barrels()
    {
        using var db = new SwingDb(":memory:");
        var a = db.Insert(Res(720), T0, T0 + 20_000);
        var b = db.Insert(Res(360, 360), T0 + 600_000, T0 + 620_000);
        Assert.Equal(1, a.Seq); Assert.Equal(2, b.Seq);
        Assert.Equal(1.0, a.Total, 3); Assert.Equal("1", a.Meters); Assert.False(a.Full);
        Assert.Equal("1+2", b.Meters); Assert.True(b.Full); Assert.Equal(1.0, b.Total, 3);
        Assert.Equal(2, db.Query(null).Count);
    }

    [Fact]
    public void Overlapping_insert_is_a_duplicate_but_a_different_meter_is_not()
    {
        using var db = new SwingDb(":memory:");
        var a = db.Insert(Res(720), T0, T0 + 20_000);
        var dup = db.Insert(Res(720), T0 + 2_000, T0 + 21_000);
        Assert.True(dup.Duplicate); Assert.Equal(a.Id, dup.Id);
        var other = new SwingResult(SwingType.Meter, new[] { new MeterPart(1, 720, 0, 10) }, "");
        Assert.False(db.Insert(other, T0 + 2_000, T0 + 21_000).Duplicate);    // العدّاد 2 ≠ العدّاد 1
        Assert.Equal(2, db.Query(null).Count);
    }

    [Fact]
    public void Session_id_updates_one_record_and_truck_keeps_entered_capacity()
    {
        using var db = new SwingDb(":memory:");
        var s1 = db.Insert(Res(720), T0, T0 + 20_000, sessionId: "S1");
        var s2 = db.Insert(Res(1440), T0, T0 + 60_000, sessionId: "S1");
        Assert.Equal(s1.Id, s2.Id); Assert.Equal(2.0, s2.Total, 3); Assert.Single(db.Query(null));
        var truck = new Truck(7, "وايت 7", 30.0);
        var s3 = db.Insert(Res(1440), T0, T0 + 70_000, truck, "ph.jpg", "S1");
        Assert.Equal(30.0, s3.Total); Assert.Equal(2.0, s3.Measured, 3); Assert.Equal(7L, s3.TruckId); Assert.True(s3.Full);
        var s4 = db.Insert(Res(2160), T0, T0 + 80_000, null, null, "S1");     // اعتماد لاحق بلا وايت: تبقى كمية الوايت
        Assert.Equal(30.0, s4.Total); Assert.Equal(3.0, s4.Measured, 3);
    }

    [Fact]
    public void AssignTruck_rename_detach_daytotal_and_clear()
    {
        using var db = new SwingDb(":memory:");
        var a = db.Insert(Res(720), T0, T0 + 20_000);
        var t = new Truck(3, "وايت", 20.0);
        db.AssignTruck(a.Id, t, "x.jpg");
        var r = db.ById(a.Id)!;
        Assert.Equal(20.0, r.Total); Assert.Equal(1.0, r.Measured, 3); Assert.Equal("x.jpg", r.Photo);
        db.AssignTruck(a.Id, new Truck(3, "وايت", 25.0), null);                 // تصحيح ثانٍ: القياس الأصلي يبقى
        Assert.Equal(1.0, db.ById(a.Id)!.Measured, 3);
        db.RenameTruck(3, "جديد"); Assert.Equal("جديد", db.ById(a.Id)!.TruckName);
        db.DetachTruck(3); Assert.Null(db.ById(a.Id)!.TruckId);
        Assert.Equal(25.0, db.DayTotal(a.Day), 3);
        Assert.Contains("x.jpg", db.PhotoPaths());
        db.Clear(); Assert.Empty(db.Query(null));
    }

    [Fact]
    public void Csv_export_has_bom_header_and_quoted_arabic_fields()
    {
        using var db = new SwingDb(":memory:");
        db.Insert(Res(720, notes: "ملاحظة \"مهمة\""), T0, T0 + 20_000);
        using var ms = new MemoryStream();
        db.ExportCsv(ms);
        var bytes = ms.ToArray();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var lines = text.TrimEnd('\n').Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("رقم الزفة (يومي),التاريخ", lines[0]);
        Assert.Contains("\"ملاحظة \"\"مهمة\"\"\"", lines[1]);
        Assert.Contains("1.000", lines[1]);
    }
}
