namespace SmartWater.Engine;

/// <summary>مصدر لقطات JPEG لقناة معيّنة (قناة الوايتات). null = لا لقطة.</summary>
public interface ISnapshotProvider
{
    Task<byte[]?> SnapshotJpegAsync(int channel, CancellationToken ct = default);
}

public sealed class IsapiSnapshotProvider : ISnapshotProvider
{
    private readonly IsapiSnapshotClient _client;
    private readonly int _stream;
    public IsapiSnapshotProvider(IsapiSnapshotClient client, int stream = 1) { _client = client; _stream = stream; }
    public Task<byte[]?> SnapshotJpegAsync(int channel, CancellationToken ct = default) => _client.GetJpegAsync(channel, _stream, ct);
}
