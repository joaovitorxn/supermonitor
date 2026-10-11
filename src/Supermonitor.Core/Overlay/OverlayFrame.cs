namespace Supermonitor.Core.Overlay;

public sealed record OverlayRow(string Label, string Value, Rgba LabelColor, Rgba ValueColor);

public sealed class OverlayFrame
{
    public OverlayFrame(OverlayRow[] rows, float[] graph, long graphVersion)
    {
        Rows = rows;
        Graph = graph;
        GraphVersion = graphVersion;
    }

    public OverlayRow[] Rows { get; }

    // Frametimes (ms), oldest to newest; empty when the mini-graph is off.
    public float[] Graph { get; }

    public long GraphVersion { get; }

    // Decides whether to redraw: same text and no new graph frame means no.
    public bool SameContentAs(OverlayFrame? other) =>
        other is not null
        && Rows.AsSpan().SequenceEqual(other.Rows)
        && Graph.Length == other.Graph.Length
        && (Graph.Length == 0 || GraphVersion == other.GraphVersion);
}
