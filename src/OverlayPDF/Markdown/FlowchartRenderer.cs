using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OverlayPDF.Markdown;

/// <summary>
/// Natively renders Mermaid flowchart diagrams to inline SVG without requiring the external mmdc CLI.
/// Supports <c>flowchart LR</c>, <c>TD</c>, <c>TB</c>, <c>RL</c>, and <c>BT</c> directions,
/// styled nodes, edge labels, dashed/thick edges, and subgraph grouping.
/// </summary>
public partial class FlowchartRenderer
{
    private const double CharWidth = 7.2;
    private const double FontSize = 13;
    private const double LineHeight = 17;
    private const double NodePadH = 24;
    private const double NodePadV = 12;
    private const double NodeMinWidth = 60;
    private const double NodeMinHeight = 36;
    private const double LayerSpacing = 100;
    private const double NodeSpacing = 40;
    private const double SvgMargin = 30;
    private const double SubgraphPad = 24;
    private const double SubgraphLabelHeight = 22;
    /// <summary>Y-radius of the top and bottom ellipse caps on a cylinder node.</summary>
    private const double CylinderCapRadius = 12.0;

    #region Types

    private enum NodeShape { Rectangle, RoundedRect, Diamond, Stadium, Subroutine, Circle, Asymmetric, Hexagon, Cylinder }

    private enum EdgeLine { Solid, Dashed, Thick }

    private enum FlowDir { TD, LR, RL, BT }

    private sealed class FlowNode
    {
        public required string Id { get; init; }
        public required string Text { get; init; }
        public NodeShape Shape { get; init; }
        public string Fill { get; set; } = "#e8e8e8";
        public string Stroke { get; set; } = "#555";
        public string Color { get; set; } = "#333";
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
        public int Layer { get; set; } = -1;
        public int Order { get; set; }
    }

    private sealed record FlowEdge(string From, string To, string? Label, EdgeLine Line, bool Arrow);

    private sealed record FlowSubgraph(string Title, List<string> NodeIds);

    private sealed class FlowModel
    {
        public FlowDir Direction { get; set; } = FlowDir.TD;
        public Dictionary<string, FlowNode> Nodes { get; } = new(StringComparer.Ordinal);
        public List<FlowEdge> Edges { get; } = [];
        public List<FlowSubgraph> Subgraphs { get; } = [];
    }

    #endregion

    #region Regex

    [GeneratedRegex(@"^flowchart\s+(LR|RL|TD|TB|BT)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DirectionLineRegex();

    [GeneratedRegex(@"\s+((?:-\.->|-->|---|-\.-|==>|===)(?:\|[^|]*\|)?)\s+")]
    private static partial Regex EdgeSplitRegex();

    #endregion

    /// <summary>
    /// Returns <c>true</c> when the definition is a flowchart this renderer can handle natively.
    /// </summary>
    public static bool CanRender(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition)) return false;
        return definition.TrimStart().StartsWith("flowchart", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Renders a Mermaid flowchart definition to an inline SVG string.
    /// </summary>
    public string RenderToSvg(string definition)
    {
        var model = Parse(definition);
        if (model.Nodes.Count == 0)
            return "<pre>No nodes found in flowchart</pre>";

        SizeNodes(model.Nodes);
        AssignLayers(model);
        PositionNodes(model);
        return BuildSvg(model);
    }

    #region Parsing

    private static FlowModel Parse(string definition)
    {
        var model = new FlowModel();
        var lines = definition.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var sgStack = new Stack<FlowSubgraph>();
        var deferredStyles = new List<(string Id, string Props)>();

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("%%")) continue;

            var dm = DirectionLineRegex().Match(line);
            if (dm.Success)
            {
                model.Direction = dm.Groups[1].Value.ToUpperInvariant() switch
                {
                    "LR" => FlowDir.LR,
                    "RL" => FlowDir.RL,
                    "BT" => FlowDir.BT,
                    _ => FlowDir.TD
                };
                continue;
            }

            if (line.StartsWith("direction ", StringComparison.OrdinalIgnoreCase)) continue;

            if (line.StartsWith("style ", StringComparison.OrdinalIgnoreCase))
            {
                var spaceIdx = line.IndexOf(' ', 6);
                if (spaceIdx > 6)
                    deferredStyles.Add((line[6..spaceIdx].Trim(), line[(spaceIdx + 1)..]));
                continue;
            }

            if (line.StartsWith("subgraph ", StringComparison.OrdinalIgnoreCase))
            {
                var sg = new FlowSubgraph(line[9..].Trim(), []);
                model.Subgraphs.Add(sg);
                sgStack.Push(sg);
                continue;
            }

            if (string.Equals(line, "end", StringComparison.OrdinalIgnoreCase))
            {
                if (sgStack.Count > 0) sgStack.Pop();
                continue;
            }

            ParseEdgeLine(line, model, sgStack.Count > 0 ? sgStack.Peek() : null);
        }

        foreach (var (id, props) in deferredStyles)
        {
            if (!model.Nodes.TryGetValue(id, out var node)) continue;
            foreach (var prop in props.Split(','))
            {
                var kv = prop.Split(':', 2);
                if (kv.Length < 2) continue;
                switch (kv[0].Trim().ToLowerInvariant())
                {
                    case "fill": node.Fill = kv[1].Trim(); break;
                    case "color": node.Color = kv[1].Trim(); break;
                    case "stroke": node.Stroke = kv[1].Trim(); break;
                }
            }
        }

        return model;
    }

    private static void ParseEdgeLine(string line, FlowModel model, FlowSubgraph? sg)
    {
        var parts = EdgeSplitRegex().Split(line);

        if (parts.Length < 2)
        {
            var nodeRef = parts[0].Trim();
            if (!string.IsNullOrEmpty(nodeRef))
            {
                var n = EnsureNode(model, nodeRef);
                sg?.NodeIds.Add(n.Id);
            }
            return;
        }

        string? prevId = null;
        for (var i = 0; i < parts.Length; i++)
        {
            var seg = parts[i].Trim();
            if (string.IsNullOrEmpty(seg)) continue;

            if (IsEdgeOp(seg))
            {
                var (style, label, arrow) = ParseEdgeOp(seg);
                string? targetId = null;

                if (i + 1 < parts.Length)
                {
                    var tRef = parts[i + 1].Trim();
                    if (!string.IsNullOrEmpty(tRef) && !IsEdgeOp(tRef))
                    {
                        var tn = EnsureNode(model, tRef);
                        sg?.NodeIds.Add(tn.Id);
                        targetId = tn.Id;
                    }
                }

                if (prevId is not null && targetId is not null)
                {
                    model.Edges.Add(new FlowEdge(prevId, targetId, label, style, arrow));
                    prevId = targetId;
                }
            }
            else
            {
                var n = EnsureNode(model, seg);
                sg?.NodeIds.Add(n.Id);
                prevId = n.Id;
            }
        }
    }

    private static bool IsEdgeOp(string s) =>
        s.StartsWith("-->", StringComparison.Ordinal) ||
        s.StartsWith("---", StringComparison.Ordinal) ||
        s.StartsWith("-.->", StringComparison.Ordinal) ||
        s.StartsWith("-.-", StringComparison.Ordinal) ||
        s.StartsWith("==>", StringComparison.Ordinal) ||
        s.StartsWith("===", StringComparison.Ordinal);

    private static (EdgeLine Style, string? Label, bool Arrow) ParseEdgeOp(string op)
    {
        string? label = null;
        var pi = op.IndexOf('|');
        if (pi >= 0)
        {
            var pe = op.IndexOf('|', pi + 1);
            if (pe > pi) label = op[(pi + 1)..pe];
        }

        EdgeLine style;
        if (op.StartsWith("-.->", StringComparison.Ordinal) || op.StartsWith("-.-", StringComparison.Ordinal))
            style = EdgeLine.Dashed;
        else if (op.StartsWith("==>", StringComparison.Ordinal) || op.StartsWith("===", StringComparison.Ordinal))
            style = EdgeLine.Thick;
        else
            style = EdgeLine.Solid;

        var arrow = op.StartsWith("-->", StringComparison.Ordinal) ||
                    op.StartsWith("-.->", StringComparison.Ordinal) ||
                    op.StartsWith("==>", StringComparison.Ordinal);

        return (style, label, arrow);
    }

    private static FlowNode EnsureNode(FlowModel model, string reference)
    {
        var (id, text, shape) = ParseNodeRef(reference);
        if (!model.Nodes.TryGetValue(id, out var node))
        {
            node = new FlowNode { Id = id, Text = text, Shape = shape };
            model.Nodes[id] = node;
        }
        return node;
    }

    private static (string Id, string Text, NodeShape Shape) ParseNodeRef(string s)
    {
        s = s.Trim();
        var i = 0;
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_'))
            i++;

        if (i == 0) return (s, s, NodeShape.Rectangle);

        var id = s[..i];
        if (i >= s.Length) return (id, id, NodeShape.Rectangle);

        var rest = s[i..];

        // Multi-character bracket patterns checked first (order matters: "[(..." must precede "[..."
        // so that the cylinder syntax is not accidentally matched as a plain rectangle).
        if (rest.StartsWith("([") && rest.EndsWith("])")) return (id, rest[2..^2], NodeShape.Stadium);
        if (rest.StartsWith("[[") && rest.EndsWith("]]")) return (id, rest[2..^2], NodeShape.Subroutine);
        if (rest.StartsWith("((") && rest.EndsWith("))")) return (id, rest[2..^2], NodeShape.Circle);
        if (rest.StartsWith("{{") && rest.EndsWith("}}")) return (id, rest[2..^2], NodeShape.Hexagon);
        if (rest.StartsWith("[(") && rest.EndsWith(")]")) return (id, rest[2..^2], NodeShape.Cylinder);
        if (rest.StartsWith('[') && rest.EndsWith(']')) return (id, rest[1..^1], NodeShape.Rectangle);
        if (rest.StartsWith('(') && rest.EndsWith(')')) return (id, rest[1..^1], NodeShape.RoundedRect);
        if (rest.StartsWith('{') && rest.EndsWith('}')) return (id, rest[1..^1], NodeShape.Diamond);
        if (rest.StartsWith('>') && rest.EndsWith(']')) return (id, rest[1..^1], NodeShape.Asymmetric);

        return (id, id, NodeShape.Rectangle);
    }

    #endregion

    #region Layout

    private static void SizeNodes(Dictionary<string, FlowNode> nodes)
    {
        foreach (var n in nodes.Values)
        {
            var textLines = n.Text.Split(["<br/>", "<br>", "<br />"], StringSplitOptions.None);
            var maxLen = textLines.Max(l => l.Length);
            var tw = maxLen * CharWidth;
            var th = textLines.Length * LineHeight;

            n.W = Math.Max(NodeMinWidth, tw + NodePadH * 2);
            n.H = Math.Max(NodeMinHeight, th + NodePadV * 2);

            if (n.Shape == NodeShape.Diamond)
            {
                n.W *= 1.4;
                n.H *= 1.4;
            }
            else if (n.Shape == NodeShape.Cylinder)
            {
                // Extra height for the top and bottom ellipse caps (CylinderCapRadius each).
                n.H += CylinderCapRadius * 2;
            }
        }
    }

    private static void AssignLayers(FlowModel model)
    {
        if (model.Nodes.Count == 0) return;

        var outgoing = new Dictionary<string, List<string>>();
        var incoming = new Dictionary<string, List<string>>();
        foreach (var nid in model.Nodes.Keys) { outgoing[nid] = []; incoming[nid] = []; }

        foreach (var e in model.Edges)
        {
            if (model.Nodes.ContainsKey(e.From) && model.Nodes.ContainsKey(e.To))
            {
                outgoing[e.From].Add(e.To);
                incoming[e.To].Add(e.From);
            }
        }

        // Roots: nodes with no incoming edges
        var roots = model.Nodes.Keys.Where(n => incoming[n].Count == 0).ToList();
        if (roots.Count == 0) roots.Add(model.Nodes.Keys.First());

        foreach (var r in roots) model.Nodes[r].Layer = 0;

        // BFS longest-path layer assignment
        var queue = new Queue<string>();
        foreach (var r in roots) queue.Enqueue(r);

        var safetyLimit = model.Nodes.Count * model.Edges.Count + model.Nodes.Count;
        var iter = 0;
        while (queue.Count > 0 && iter++ < safetyLimit)
        {
            var cur = queue.Dequeue();
            var next = model.Nodes[cur].Layer + 1;
            foreach (var adj in outgoing[cur])
            {
                if (model.Nodes[adj].Layer < next)
                {
                    model.Nodes[adj].Layer = next;
                    queue.Enqueue(adj);
                }
            }
        }

        // Assign remaining unvisited nodes to layer 0
        foreach (var n in model.Nodes.Values.Where(n => n.Layer < 0))
            n.Layer = 0;
    }

    private static void PositionNodes(FlowModel model)
    {
        var layers = model.Nodes.Values
            .GroupBy(n => n.Layer)
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var layer in layers)
        {
            var ordered = layer.ToList();
            for (var i = 0; i < ordered.Count; i++) ordered[i].Order = i;
        }

        var horiz = model.Direction is FlowDir.LR or FlowDir.RL;
        // When subgraphs are present their title label extends SubgraphPad + SubgraphLabelHeight
        // above the top of the nodes inside them. Add this extra offset so the label stays visible.
        var subgraphTopMargin = model.Subgraphs.Count > 0 ? SubgraphPad + SubgraphLabelHeight : 0;
        double primary = SvgMargin + (horiz ? 0 : subgraphTopMargin);

        // Pre-compute the total secondary span (x-width for TD, y-height for LR) of every layer
        // so that narrower layers can be centered relative to the widest one.
        var layerNodes = layers
            .Select(g => g.OrderBy(n => n.Order).ToList())
            .ToList();
        var layerSpans = layerNodes
            .Select(nodes => nodes.Count == 0
                ? 0.0
                : nodes.Sum(n => horiz ? n.H : n.W) + NodeSpacing * (nodes.Count - 1))
            .ToList();
        var maxSpan = layerSpans.Count > 0 ? layerSpans.Max() : 0;

        for (var li = 0; li < layerNodes.Count; li++)
        {
            var nodes = layerNodes[li];
            // Center this layer within the widest layer.
            double secondary = SvgMargin
                + (horiz ? subgraphTopMargin : 0)
                + (maxSpan - layerSpans[li]) / 2;
            double maxPrimary = 0;

            foreach (var n in nodes)
            {
                if (horiz)
                {
                    n.X = primary;
                    n.Y = secondary;
                    secondary += n.H + NodeSpacing;
                    maxPrimary = Math.Max(maxPrimary, n.W);
                }
                else
                {
                    n.X = secondary;
                    n.Y = primary;
                    secondary += n.W + NodeSpacing;
                    maxPrimary = Math.Max(maxPrimary, n.H);
                }
            }

            primary += maxPrimary + LayerSpacing;
        }

        // Mirror positions for RL / BT directions
        if (model.Direction is FlowDir.RL)
        {
            var maxX = model.Nodes.Values.Max(n => n.X + n.W);
            foreach (var n in model.Nodes.Values)
                n.X = maxX - n.X - n.W + 2 * SvgMargin;
        }
        else if (model.Direction is FlowDir.BT)
        {
            var maxY = model.Nodes.Values.Max(n => n.Y + n.H);
            foreach (var n in model.Nodes.Values)
                n.Y = maxY - n.Y - n.H + 2 * SvgMargin;
        }
    }

    #endregion

    #region SVG generation

    private static string BuildSvg(FlowModel model)
    {
        // Compute bounds including subgraph padding
        var minX = model.Nodes.Values.Min(n => n.X);
        var minY = model.Nodes.Values.Min(n => n.Y);
        var maxX = model.Nodes.Values.Max(n => n.X + n.W);
        var maxY = model.Nodes.Values.Max(n => n.Y + n.H);

        foreach (var sg in model.Subgraphs)
        {
            var sgNodes = sg.NodeIds.Distinct()
                .Where(id => model.Nodes.ContainsKey(id))
                .Select(id => model.Nodes[id])
                .ToList();
            if (sgNodes.Count == 0) continue;

            minX = Math.Min(minX, sgNodes.Min(n => n.X) - SubgraphPad);
            minY = Math.Min(minY, sgNodes.Min(n => n.Y) - SubgraphPad - SubgraphLabelHeight);
            maxX = Math.Max(maxX, sgNodes.Max(n => n.X + n.W) + SubgraphPad);
            maxY = Math.Max(maxY, sgNodes.Max(n => n.Y + n.H) + SubgraphPad);
        }

        var svgW = maxX + SvgMargin;
        var svgH = maxY + SvgMargin;

        var sb = new StringBuilder();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{svgW:0.#}\" height=\"{svgH:0.#}\" viewBox=\"0 0 {svgW:0.#} {svgH:0.#}\">"));

        sb.AppendLine("""
          <defs>
            <marker id="fc-arrow" markerWidth="10" markerHeight="7" refX="9" refY="3.5" orient="auto" markerUnits="strokeWidth">
              <polygon points="0 0, 10 3.5, 0 7" fill="#555" />
            </marker>
          </defs>
        """);

        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"<style>.fc-text {{ font: {FontSize:0}px sans-serif; }}</style>"));

        // Subgraph backgrounds
        foreach (var sg in model.Subgraphs)
        {
            var sgNodes = sg.NodeIds.Distinct()
                .Where(id => model.Nodes.ContainsKey(id))
                .Select(id => model.Nodes[id])
                .ToList();
            if (sgNodes.Count == 0) continue;

            var rx = sgNodes.Min(n => n.X) - SubgraphPad;
            var ry = sgNodes.Min(n => n.Y) - SubgraphPad - SubgraphLabelHeight;
            var rw = sgNodes.Max(n => n.X + n.W) - sgNodes.Min(n => n.X) + SubgraphPad * 2;
            var rh = sgNodes.Max(n => n.Y + n.H) - sgNodes.Min(n => n.Y) + SubgraphPad * 2 + SubgraphLabelHeight;

            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"<rect x=\"{rx:0.#}\" y=\"{ry:0.#}\" width=\"{rw:0.#}\" height=\"{rh:0.#}\" rx=\"6\" fill=\"#f5f5f5\" stroke=\"#bbb\" stroke-dasharray=\"6 3\" />"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"<text x=\"{rx + 8:0.#}\" y=\"{ry + 16:0.#}\" class=\"fc-text\" fill=\"#666\" font-weight=\"bold\">{Esc(sg.Title)}</text>"));
        }

        // Edges (rendered before nodes so nodes draw on top)
        var horiz = model.Direction is FlowDir.LR or FlowDir.RL;
        foreach (var edge in model.Edges)
        {
            if (!model.Nodes.TryGetValue(edge.From, out var src) ||
                !model.Nodes.TryGetValue(edge.To, out var dst))
                continue;
            AppendEdge(sb, src, dst, edge, horiz);
        }

        // Nodes
        foreach (var node in model.Nodes.Values)
            AppendNode(sb, node);

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private static void AppendNode(StringBuilder sb, FlowNode n)
    {
        var cx = n.X + n.W / 2;
        var cy = n.Y + n.H / 2;

        switch (n.Shape)
        {
            case NodeShape.RoundedRect:
            case NodeShape.Stadium:
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"<rect x=\"{n.X:0.#}\" y=\"{n.Y:0.#}\" width=\"{n.W:0.#}\" height=\"{n.H:0.#}\" rx=\"{n.H / 2:0.#}\" fill=\"{n.Fill}\" stroke=\"{n.Stroke}\" stroke-width=\"1.5\" />"));
                break;
            case NodeShape.Diamond:
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"<polygon points=\"{cx:0.#},{n.Y:0.#} {n.X + n.W:0.#},{cy:0.#} {cx:0.#},{n.Y + n.H:0.#} {n.X:0.#},{cy:0.#}\" fill=\"{n.Fill}\" stroke=\"{n.Stroke}\" stroke-width=\"1.5\" />"));
                break;
            case NodeShape.Circle:
                var r = Math.Min(n.W, n.H) / 2;
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"<circle cx=\"{cx:0.#}\" cy=\"{cy:0.#}\" r=\"{r:0.#}\" fill=\"{n.Fill}\" stroke=\"{n.Stroke}\" stroke-width=\"1.5\" />"));
                break;
            case NodeShape.Subroutine:
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"<rect x=\"{n.X:0.#}\" y=\"{n.Y:0.#}\" width=\"{n.W:0.#}\" height=\"{n.H:0.#}\" rx=\"2\" fill=\"{n.Fill}\" stroke=\"{n.Stroke}\" stroke-width=\"1.5\" />"));
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"<line x1=\"{n.X + 6:0.#}\" y1=\"{n.Y:0.#}\" x2=\"{n.X + 6:0.#}\" y2=\"{n.Y + n.H:0.#}\" stroke=\"{n.Stroke}\" stroke-width=\"1\" />"));
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"<line x1=\"{n.X + n.W - 6:0.#}\" y1=\"{n.Y:0.#}\" x2=\"{n.X + n.W - 6:0.#}\" y2=\"{n.Y + n.H:0.#}\" stroke=\"{n.Stroke}\" stroke-width=\"1\" />"));
                break;
            case NodeShape.Hexagon:
            {
                var off = n.W * 0.15;
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"<polygon points=\"{n.X + off:0.#},{n.Y:0.#} {n.X + n.W - off:0.#},{n.Y:0.#} {n.X + n.W:0.#},{cy:0.#} {n.X + n.W - off:0.#},{n.Y + n.H:0.#} {n.X + off:0.#},{n.Y + n.H:0.#} {n.X:0.#},{cy:0.#}\" fill=\"{n.Fill}\" stroke=\"{n.Stroke}\" stroke-width=\"1.5\" />"));
                break;
            }
            case NodeShape.Asymmetric:
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"<polygon points=\"{n.X:0.#},{n.Y:0.#} {n.X + n.W - 10:0.#},{n.Y:0.#} {n.X + n.W:0.#},{cy:0.#} {n.X + n.W - 10:0.#},{n.Y + n.H:0.#} {n.X:0.#},{n.Y + n.H:0.#}\" fill=\"{n.Fill}\" stroke=\"{n.Stroke}\" stroke-width=\"1.5\" />"));
                break;
            case NodeShape.Cylinder:
            {
                var ry = CylinderCapRadius; // y-radius of the top/bottom ellipse caps
                var rx = n.W / 2;
                // Body: left side down, bottom arc, right side up, top arc (back).
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"<path d=\"M {n.X:0.#},{n.Y + ry:0.#} A {rx:0.#},{ry:0.#} 0 0,0 {n.X + n.W:0.#},{n.Y + ry:0.#} V {n.Y + n.H - ry:0.#} A {rx:0.#},{ry:0.#} 0 0,1 {n.X:0.#},{n.Y + n.H - ry:0.#} Z\" fill=\"{n.Fill}\" stroke=\"{n.Stroke}\" stroke-width=\"1.5\" />"));
                // Top ellipse drawn last so it appears on top (gives the 3-D cap illusion).
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"<ellipse cx=\"{cx:0.#}\" cy=\"{n.Y + ry:0.#}\" rx=\"{rx:0.#}\" ry=\"{ry:0.#}\" fill=\"{n.Fill}\" stroke=\"{n.Stroke}\" stroke-width=\"1.5\" />"));
                break;
            }
            default: // Rectangle
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"<rect x=\"{n.X:0.#}\" y=\"{n.Y:0.#}\" width=\"{n.W:0.#}\" height=\"{n.H:0.#}\" rx=\"4\" fill=\"{n.Fill}\" stroke=\"{n.Stroke}\" stroke-width=\"1.5\" />"));
                break;
        }

        // Text (split on <br/> for multi-line)
        var lines = n.Text.Split(["<br/>", "<br>", "<br />"], StringSplitOptions.None);
        var totalH = lines.Length * LineHeight;
        var baseY = cy - totalH / 2 + FontSize * 0.85;
        for (var li = 0; li < lines.Length; li++)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"<text x=\"{cx:0.#}\" y=\"{baseY + li * LineHeight:0.#}\" text-anchor=\"middle\" class=\"fc-text\" fill=\"{n.Color}\">{Esc(lines[li])}</text>"));
        }
    }

    private static void AppendEdge(StringBuilder sb, FlowNode src, FlowNode dst, FlowEdge edge, bool horiz)
    {
        var srcCx = src.X + src.W / 2;
        var srcCy = src.Y + src.H / 2;
        var dstCx = dst.X + dst.W / 2;
        var dstCy = dst.Y + dst.H / 2;

        double x1, y1, x2, y2;
        if (horiz)
        {
            if (srcCx <= dstCx)
            { x1 = src.X + src.W; y1 = srcCy; x2 = dst.X; y2 = dstCy; }
            else
            { x1 = src.X; y1 = srcCy; x2 = dst.X + dst.W; y2 = dstCy; }
        }
        else
        {
            if (srcCy <= dstCy)
            { x1 = srcCx; y1 = src.Y + src.H; x2 = dstCx; y2 = dst.Y; }
            else
            { x1 = srcCx; y1 = src.Y; x2 = dstCx; y2 = dst.Y + dst.H; }
        }

        // Cubic bezier control points for smooth curves
        var dx = (x2 - x1) / 2;
        var dy = (y2 - y1) / 2;
        double cx1, cy1, cx2, cy2;
        if (horiz) { cx1 = x1 + dx; cy1 = y1; cx2 = x2 - dx; cy2 = y2; }
        else { cx1 = x1; cy1 = y1 + dy; cx2 = x2; cy2 = y2 - dy; }

        var sw = edge.Line == EdgeLine.Thick ? "3" : "1.5";
        var dash = edge.Line == EdgeLine.Dashed ? " stroke-dasharray=\"6 4\"" : "";
        var marker = edge.Arrow ? " marker-end=\"url(#fc-arrow)\"" : "";

        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"<path d=\"M{x1:0.#},{y1:0.#} C{cx1:0.#},{cy1:0.#} {cx2:0.#},{cy2:0.#} {x2:0.#},{y2:0.#}\" fill=\"none\" stroke=\"#555\" stroke-width=\"{sw}\"{dash}{marker} />"));

        // Edge label with background
        if (!string.IsNullOrWhiteSpace(edge.Label))
        {
            var lx = (x1 + x2) / 2;
            var ly = (y1 + y2) / 2 - 4;
            var lw = edge.Label.Length * CharWidth + 12;
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"<rect x=\"{lx - lw / 2:0.#}\" y=\"{ly - 12:0.#}\" width=\"{lw:0.#}\" height=\"18\" rx=\"3\" fill=\"#fff\" stroke=\"#ccc\" stroke-width=\"1\" />"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"<text x=\"{lx:0.#}\" y=\"{ly + 2:0.#}\" text-anchor=\"middle\" font-size=\"11\" fill=\"#555\">{Esc(edge.Label)}</text>"));
        }
    }

    private static string Esc(string s) => System.Security.SecurityElement.Escape(s) ?? string.Empty;

    #endregion
}
