namespace Gleam.Engine.Overlays;

public sealed class OverlayScene
{
    private readonly List<OverlayPrimitive> _primitives = new();

    public IReadOnlyList<OverlayPrimitive> Primitives => _primitives;

    public void Add(OverlayPrimitive primitive) => _primitives.Add(primitive);

    public void AddRange(IEnumerable<OverlayPrimitive> primitives) => _primitives.AddRange(primitives);
}