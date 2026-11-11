namespace SmartWater.Licensing;

/// <summary>
/// ساعة موثوقة لا تتأثر بتغيير ساعة الكمبيوتر: وقت الخادم (ترويسة Date) عند آخر اتصال + المدة المنقضية على ساعة
/// رتيبة (Environment.TickCount64). تُصفَّر الرتيبة عند إعادة التشغيل، فإن رجعت القراءة إلى الوراء تصير NeedsSync
/// حتى يتصل الجهاز بالخادم من جديد.
/// </summary>
public sealed class TrustedClock
{
    public const long ClockToleranceMs = 120_000L;
    private readonly Func<long> _wall, _mono;
    private readonly Func<string> _bootId;

    public sealed record Anchor(long ServerMs, long MonoMs, long WallMs, string Boot);
    public enum State { Trusted, NeedsSync }

    public sealed record Reading(State State, long Ms = 0, long SkewMs = 0)
    {
        public bool Trusted => State == State.Trusted;
        public bool Tampered => Trusted && Math.Abs(SkewMs) > ClockToleranceMs;
    }

    public Anchor? Current { get; private set; }

    public TrustedClock(Func<long> wall, Func<long> mono, Func<string>? bootId = null)
    {
        _wall = wall; _mono = mono; _bootId = bootId ?? (() => "");
    }

    public void AnchorNow(long serverMs) => Current = new Anchor(serverMs, _mono(), _wall(), _bootId());

    public Reading Read()
    {
        var a = Current;
        if (a == null) return new Reading(State.NeedsSync);
        long m = _mono();
        if (m < a.MonoMs || a.Boot != _bootId()) return new Reading(State.NeedsSync);
        long t = a.ServerMs + (m - a.MonoMs);
        return new Reading(State.Trusted, t, _wall() - t);
    }

    public string? Encode() => Current is { } a ? $"TC1|{a.Boot.Replace('|', '_')}|{a.ServerMs}|{a.MonoMs}|{a.WallMs}" : null;

    public void Restore(string? text)
    {
        var p = text?.Split('|');
        if (p == null || p.Length != 5 || p[0] != "TC1") return;
        if (!long.TryParse(p[2], out var s) || !long.TryParse(p[3], out var m) || !long.TryParse(p[4], out var w)) return;
        Current = new Anchor(s, m, w, p[1]);
    }
}
