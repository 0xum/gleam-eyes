namespace Gleam.Gestures.Models;

internal readonly record struct GestureLandmarkPoint(float X, float Y, float Z);

internal sealed record GestureLandmarkSet(IReadOnlyList<GestureLandmarkPoint> Points);

internal sealed class GestureLandmarkSnapshot
{
    public static readonly GestureLandmarkSnapshot Empty = new(0, Array.Empty<GestureLandmarkSet>());

    public GestureLandmarkSnapshot(long timestampUs, IReadOnlyList<GestureLandmarkSet> sets)
    {
        TimestampUs = timestampUs;
        Sets = sets;
    }

    public long TimestampUs { get; }

    public IReadOnlyList<GestureLandmarkSet> Sets { get; }

    public bool HasLandmarks => Sets.Count > 0;
}
