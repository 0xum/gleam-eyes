namespace Gleam.Engine.Processing;

public sealed class FrameProcessResult
{
    public FrameProcessResult(string? label, float? confidence)
    {
        Label = label;
        Confidence = confidence;
    }

    public string? Label { get; }

    public float? Confidence { get; }
}