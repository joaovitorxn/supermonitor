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

    // Frametimes (ms), do mais antigo ao mais novo; vazio quando o mini-gráfico está desligado.
    public float[] Graph { get; }

    public long GraphVersion { get; }

    // Decide se vale redesenhar: texto igual e gráfico sem frame novo → não.
    public bool SameContentAs(OverlayFrame? other) =>
        other is not null
        && Rows.AsSpan().SequenceEqual(other.Rows)
        && Graph.Length == other.Graph.Length
        && (Graph.Length == 0 || GraphVersion == other.GraphVersion);
}
