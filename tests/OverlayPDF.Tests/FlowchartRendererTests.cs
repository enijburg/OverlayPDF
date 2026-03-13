using System.Globalization;
using System.Text.RegularExpressions;
using OverlayPDF.Markdown;
using Xunit;

namespace OverlayPDF.Tests;

/// <summary>
/// Tests for FlowchartRenderer native Mermaid flowchart SVG generation.
/// </summary>
public class FlowchartRendererTests
{
    [Fact]
    public void CanRender_WithFlowchartDefinition_ReturnsTrue()
    {
        Assert.True(FlowchartRenderer.CanRender("flowchart LR\n    A --> B"));
        Assert.True(FlowchartRenderer.CanRender("flowchart TD\n    A --> B"));
        Assert.True(FlowchartRenderer.CanRender("flowchart TB\n    A --> B"));
        Assert.True(FlowchartRenderer.CanRender("flowchart RL\n    A --> B"));
        Assert.True(FlowchartRenderer.CanRender("flowchart BT\n    A --> B"));
    }

    [Fact]
    public void CanRender_WithNonFlowchart_ReturnsFalse()
    {
        Assert.False(FlowchartRenderer.CanRender("sequenceDiagram\n    A->>B: Hello"));
        Assert.False(FlowchartRenderer.CanRender("gantt\n    title Test"));
        Assert.False(FlowchartRenderer.CanRender(""));
        Assert.False(FlowchartRenderer.CanRender("   "));
    }

    [Fact]
    public void RenderToSvg_SimpleChain_LR_GeneratesSvg()
    {
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart LR
                A[Source] --> B[Conversion]
                B --> C[Validation]
                C --> D[Destination]
                D --> E[Finalizers]
            """;

        var result = renderer.RenderToSvg(definition);

        Assert.Contains("<svg", result);
        Assert.Contains("</svg>", result);
        Assert.Contains("Source", result);
        Assert.Contains("Conversion", result);
        Assert.Contains("Validation", result);
        Assert.Contains("Destination", result);
        Assert.Contains("Finalizers", result);
    }

    [Fact]
    public void RenderToSvg_WithStyles_AppliesColors()
    {
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart LR
                A[Source] --> B[Conversion]
                B --> C[Validation]

                style A fill:#4A90D9,color:#fff
                style B fill:#F5A623,color:#fff
                style C fill:#7B68EE,color:#fff
            """;

        var result = renderer.RenderToSvg(definition);

        Assert.Contains("#4A90D9", result);
        Assert.Contains("#F5A623", result);
        Assert.Contains("#7B68EE", result);
    }

    [Fact]
    public void RenderToSvg_WithEdgeLabels_RendersLabels()
    {
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart TD
                S1[Source] -->|conversion: xml1| C1[Converter]
                S1 -->|destination: dest1| D1[Destination]
            """;

        var result = renderer.RenderToSvg(definition);

        Assert.Contains("<svg", result);
        Assert.Contains("conversion: xml1", result);
        Assert.Contains("destination: dest1", result);
        Assert.Contains("Source", result);
        Assert.Contains("Converter", result);
        Assert.Contains("Destination", result);
    }

    [Fact]
    public void RenderToSvg_WithDashedEdge_RendersDashArray()
    {
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart TD
                A[Source] -.->|optional| B[Target]
            """;

        var result = renderer.RenderToSvg(definition);

        Assert.Contains("stroke-dasharray", result);
        Assert.Contains("optional", result);
    }

    [Fact]
    public void RenderToSvg_WithSubgraph_RendersGrouping()
    {
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart TD
                subgraph Configuration File
                    direction TB
                    S1[Source: database]
                    C1[Conversion: xml]
                    D1[Destination: ftp]
                end
                S1 --> C1
                C1 --> D1
            """;

        var result = renderer.RenderToSvg(definition);

        Assert.Contains("<svg", result);
        Assert.Contains("Configuration File", result);
        Assert.Contains("Source: database", result);
        Assert.Contains("Conversion: xml", result);
        Assert.Contains("Destination: ftp", result);
        // Subgraph background rectangle
        Assert.Contains("stroke-dasharray=\"6 3\"", result);
    }

    [Fact]
    public void RenderToSvg_TD_TreeLayout_GeneratesSvg()
    {
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart TD
                EXE[Extractor.exe] -->|loads| CORE[Built-in Modules]
                EXE -->|loads| P1[Plugin: Excel Source]
                EXE -->|loads| P2[Plugin: OnGuard Validation]

                CORE --> SRC_DB[database]
                CORE --> SRC_FILE[file]

                style EXE fill:#4A90D9,color:#fff
            """;

        var result = renderer.RenderToSvg(definition);

        Assert.Contains("<svg", result);
        Assert.Contains("Extractor.exe", result);
        Assert.Contains("Built-in Modules", result);
        Assert.Contains("Plugin: Excel Source", result);
        Assert.Contains("database", result);
        Assert.Contains("file", result);
        Assert.Contains("#4A90D9", result);
    }

    [Fact]
    public void RenderToSvg_WithMultiLineNodeText_RendersSeparateTextElements()
    {
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart TD
                S1[Source: database<br/>name: erp<br/>destination: dest1]
                D1[Destination: ftp]
                S1 --> D1
            """;

        var result = renderer.RenderToSvg(definition);

        Assert.Contains("Source: database", result);
        Assert.Contains("name: erp", result);
        Assert.Contains("destination: dest1", result);
    }

    [Fact]
    public void RenderToSvg_EmptyDefinition_ReturnsFallback()
    {
        var renderer = new FlowchartRenderer();
        var definition = "flowchart LR\n";

        var result = renderer.RenderToSvg(definition);

        Assert.Contains("<pre>", result);
    }

    [Fact]
    public void RenderToSvg_ThickEdge_RendersThickerStroke()
    {
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart LR
                A[Start] ==> B[End]
            """;

        var result = renderer.RenderToSvg(definition);

        Assert.Contains("<svg", result);
        Assert.Contains("stroke-width=\"3\"", result);
    }

    [Fact]
    public void RenderToSvg_ArrowMarker_IsPresent()
    {
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart LR
                A[Source] --> B[Target]
            """;

        var result = renderer.RenderToSvg(definition);

        Assert.Contains("marker-end=\"url(#fc-arrow)\"", result);
        Assert.Contains("<marker id=\"fc-arrow\"", result);
    }

    [Fact]
    public void RenderToSvg_NoArrowEdge_OmitsMarker()
    {
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart LR
                A[Source] --- B[Target]
            """;

        var result = renderer.RenderToSvg(definition);

        Assert.Contains("<svg", result);
        Assert.DoesNotContain("marker-end", result);
    }

    [Fact]
    public void RenderToSvg_FullConfigExample_GeneratesValidSvg()
    {
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart TD
                subgraph Configuration File
                    direction TB
                    S1[Source: database<br/>name: erp<br/>conversion: xml1<br/>destination: dest1]
                    S2[Source: file<br/>name: reports<br/>destination: dest1]
                    C1[Conversion: xml<br/>name: xml1]
                    D1[Destination: ftp<br/>name: dest1]
                    V1[Validation: onguard<br/>name: val1]
                    F1[Finalizer: StatusReport<br/>name: status1<br/>destination: mail1]
                    D2[Destination: mail<br/>name: mail1]
                end

                S1 -->|conversion: xml1| C1
                S1 -->|destination: dest1| D1
                S2 -->|destination: dest1| D1
                S1 -.->|validation: val1| V1
                F1 -->|destination: mail1| D2
            """;

        var result = renderer.RenderToSvg(definition);

        Assert.Contains("<svg", result);
        Assert.Contains("</svg>", result);
        Assert.Contains("Configuration File", result);
        Assert.Contains("conversion: xml1", result);
        Assert.Contains("destination: dest1", result);
        Assert.Contains("validation: val1", result);
        Assert.Contains("destination: mail1", result);
        Assert.Contains("stroke-dasharray=\"6 4\"", result);
    }

    [Fact]
    public void RenderToSvg_SecurityPermissions_GeneratesValidSvg()
    {
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart TD
                ADMIN[Administrator<br/>Read+Write config/]
                SVC[Service Account<br/>Read+Write all folders<br/>Read cert private key]
                OPERATOR[Operator<br/>Read config/<br/>Run add/config commands]

                ADMIN -->|edits| CFG[config/appsettings.json]
                SVC -->|runs extraction| EXE[Extractor.exe]
                OPERATOR -->|scaffolds| CFG
                EXE -->|reads| CFG
                EXE -->|writes| LOGS[logs/]
                EXE -->|writes| OUTPUT[output/]
                EXE -->|reads| INPUT[input/]

                style ADMIN fill:#CD5C5C,color:#fff
                style SVC fill:#4A90D9,color:#fff
                style OPERATOR fill:#50C878,color:#fff
            """;

        var result = renderer.RenderToSvg(definition);

        Assert.Contains("<svg", result);
        Assert.Contains("Administrator", result);
        Assert.Contains("Service Account", result);
        Assert.Contains("Operator", result);
        Assert.Contains("#CD5C5C", result);
        Assert.Contains("#4A90D9", result);
        Assert.Contains("#50C878", result);
    }

    [Fact]
    public void CanRender_CaseInsensitive_ReturnsTrue()
    {
        Assert.True(FlowchartRenderer.CanRender("Flowchart LR\n    A --> B"));
        Assert.True(FlowchartRenderer.CanRender("FLOWCHART TD\n    A --> B"));
    }

    [Fact]
    public void RenderToSvg_SubgraphLabel_IsNotClipped()
    {
        // Regression test: subgraph title labels used to extend above y=0,
        // making them invisible because the SVG viewBox started at y=0.
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart TD
                subgraph Configuration File
                    direction TB
                    A[Node A]
                    B[Node B]
                end
                A --> B
            """;

        var result = renderer.RenderToSvg(definition);

        Assert.Contains("<svg", result);
        Assert.Contains("Configuration File", result);

        Assert.True(TryFindSubgraphRectY(result, out var yValue),
            "Subgraph background rect not found in SVG output.");
        Assert.True(yValue >= 0,
            $"Subgraph rect y={yValue} is negative – the title label would be clipped by the SVG viewport.");
    }

    [Fact]
    public void RenderToSvg_SubgraphLabel_InLrLayout_IsNotClipped()
    {
        // Same regression check for left-to-right layouts with subgraphs.
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart LR
                subgraph My Group
                    A[Node A]
                    B[Node B]
                end
                A --> B
            """;

        var result = renderer.RenderToSvg(definition);

        Assert.True(TryFindSubgraphRectY(result, out var yValue),
            "Subgraph background rect not found in SVG output.");
        Assert.True(yValue >= 0,
            $"Subgraph rect y={yValue} is negative – the title label would be clipped by the SVG viewport.");
    }

    /// <summary>
    /// Parses the y-coordinate of the subgraph background rect from an SVG string.
    /// The subgraph rect is identified by its <c>stroke-dasharray="6 3"</c> border style.
    /// </summary>
    private static bool TryFindSubgraphRectY(string svg, out double yValue)
    {
        yValue = 0;
        // Use a lookahead so the two attributes can appear in any order.
        // RegexOptions.Singleline allows [^>] to span newlines within a tag.
        var m = Regex.Match(svg,
            @"<rect\b(?=[^>]*stroke-dasharray=""6 3"")[^>]*\by=""(-?[\d.]+)""",
            RegexOptions.Singleline);
        if (!m.Success) return false;
        yValue = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return true;
    }

    [Fact]
    public void RenderToSvg_FullPipelineFlow_GeneratesValidSvg()
    {
        // Diagram 1 from the issue: simple LR pipeline with all five styled nodes.
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart LR
                A[Source] --> B[Conversion]
                B --> C[Validation]
                C --> D[Destination]
                D --> E[Finalizers]

                style A fill:#4A90D9,color:#fff
                style B fill:#F5A623,color:#fff
                style C fill:#7B68EE,color:#fff
                style D fill:#50C878,color:#fff
                style E fill:#CD5C5C,color:#fff
            """;

        var result = renderer.RenderToSvg(definition);

        Assert.Contains("<svg", result);
        Assert.Contains("</svg>", result);
        Assert.Contains("Source", result);
        Assert.Contains("Conversion", result);
        Assert.Contains("Validation", result);
        Assert.Contains("Destination", result);
        Assert.Contains("Finalizers", result);
        Assert.Contains("#4A90D9", result);
        Assert.Contains("#F5A623", result);
        Assert.Contains("#7B68EE", result);
        Assert.Contains("#50C878", result);
        Assert.Contains("#CD5C5C", result);
        // All five nodes are present as rect elements
        Assert.Equal(5, Regex.Matches(result, @"<rect[^>]+rx=""4""").Count);
    }

    [Fact]
    public void RenderToSvg_LR_SingleNodeLayers_AllNodesShareSameY()
    {
        // In a LR diagram where every layer has exactly one node the centering
        // offset is zero for every layer, so all nodes should share the same Y
        // coordinate – i.e. they must be vertically aligned (same row).
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart LR
                A[Source] --> B[Conversion]
                B --> C[Validation]
                C --> D[Destination]
                D --> E[Finalizers]
            """;

        var result = renderer.RenderToSvg(definition);

        // Collect all y-values of rect elements (node bodies) using attribute-order-independent pattern.
        // RegexOptions.Singleline handles SVG tags that may span multiple lines.
        var yValues = Regex.Matches(result, @"<rect\b(?=[^>]*\brx=""4"")[^>]*>", RegexOptions.Singleline)
            .Select(m =>
            {
                var ym = Regex.Match(m.Value, @"\by=""([\d.]+)""", RegexOptions.Singleline);
                return ym.Success ? double.Parse(ym.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
            })
            .ToList();

        Assert.Equal(5, yValues.Count);
        // All five nodes must share the same Y (single-node layers → centering offset = 0).
        Assert.All(yValues, y => Assert.Equal(yValues[0], y));
    }

    [Fact]
    public void RenderToSvg_TD_SingleRootNode_IsCenteredOverWiderLeafLayer()
    {
        // When the root layer has one node and the leaf layer has many nodes,
        // the root node's x-center should equal the leaf layer's x-center.
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart TD
                ROOT[Root]
                ROOT --> A[Alpha]
                ROOT --> B[Beta]
                ROOT --> C[Gamma]
                ROOT --> D[Delta]
            """;

        var result = renderer.RenderToSvg(definition);

        // Collect all node rects (identified by rx="4") using attribute-order-independent patterns.
        // RegexOptions.Singleline handles SVG tags that may span multiple lines.
        var allRects = Regex.Matches(result, @"<rect\b(?=[^>]*\brx=""4"")[^>]*>", RegexOptions.Singleline)
            .Select(m =>
            {
                double Attr(string name)
                {
                    var am = Regex.Match(m.Value, $@"\b{name}=""([\d.]+)""", RegexOptions.Singleline);
                    return am.Success ? double.Parse(am.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                }
                return (x: Attr("x"), y: Attr("y"), w: Attr("width"));
            })
            .ToList();

        Assert.True(allRects.Count == 5, $"Expected 5 node rects, got {allRects.Count}");

        // The root node is the one at the smallest y value (layer 0 in TD layout).
        var rootRect = allRects.OrderBy(r => r.y).First();
        var rootCx = rootRect.x + rootRect.w / 2;

        // Leaf nodes (layer 1, larger y) – compute their collective centre.
        var leafRects = allRects.OrderBy(r => r.y).Skip(1).ToList();
        var leafLeft  = leafRects.Min(r => r.x);
        var leafRight = leafRects.Max(r => r.x + r.w);
        var leafCx = (leafLeft + leafRight) / 2;

        // Root centre should be within 2 px of the leaf-layer centre.
        Assert.True(Math.Abs(rootCx - leafCx) < 2,
            $"Root centre x={rootCx:0.#} but leaf layer centre x={leafCx:0.#} – root is not centred.");
    }

    [Fact]
    public void RenderToSvg_FullExtractorPluginDiagram_GeneratesValidSvg()
    {
        // Diagram 3 from the issue: Extractor.exe with built-in modules and plugins.
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart TD
                EXE[Extractor.exe] -->|loads| CORE[Built-in Modules]
                EXE -->|loads| P1[Plugin: Excel Source]
                EXE -->|loads| P2[Plugin: OnGuard Validation]
                EXE -->|loads| P3[Plugin: Custom Conversion]

                CORE --> SRC_DB[database]
                CORE --> SRC_FILE[file]
                CORE --> SRC_CSV[csv]
                CORE --> SRC_XML[xml]
                CORE --> CONV_XML[xml]
                CORE --> CONV_CSV[csv]
                CORE --> CONV_XSLT[xslt]
                CORE --> CONV_ZIP[zip]
                CORE --> DEST_FLD[folder]
                CORE --> DEST_FTP[ftp]
                CORE --> DEST_MAIL[mail]

                style EXE fill:#4A90D9,color:#fff
            """;

        var result = renderer.RenderToSvg(definition);

        Assert.Contains("<svg", result);
        Assert.Contains("</svg>", result);
        Assert.Contains("Extractor.exe", result);
        Assert.Contains("Built-in Modules", result);
        Assert.Contains("Plugin: Excel Source", result);
        Assert.Contains("Plugin: OnGuard Validation", result);
        Assert.Contains("Plugin: Custom Conversion", result);
        Assert.Contains("database", result);
        Assert.Contains("xslt", result);
        Assert.Contains("folder", result);
        Assert.Contains("#4A90D9", result);
        // Three layers: EXE, CORE+Plugins, leaf nodes
        Assert.Contains("marker-end=\"url(#fc-arrow)\"", result);
        // Edge labels are rendered
        Assert.Contains("loads", result);
    }

    [Fact]
    public void RenderToSvg_CylinderNode_UsesCylinderSvgPath()
    {
        // The [(..)] Mermaid syntax must produce a cylinder (path + ellipse), not a rectangle.
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart LR
                DB[(Database)]
                APP[Application]
                APP --> DB
            """;

        var result = renderer.RenderToSvg(definition);

        Assert.Contains("<svg", result);
        Assert.Contains("Database", result);
        // Cylinder is rendered as a <path> element (body) and an <ellipse> (top cap).
        Assert.Contains("<path ", result);
        Assert.Contains("<ellipse ", result);
        // The cylinder text must not contain stray parentheses from the [(..)] delimiters.
        Assert.DoesNotContain("(Database)", result);
    }

    [Fact]
    public void RenderToSvg_XsltDiagram_CylinderAndDiamondRendered()
    {
        // Full XSLT pipeline from the issue: verifies [(..)] cylinder and {..} diamond shapes.
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart LR
                SRC[Source Data] --> INT[Internal XML<br/>Representation]
                INT --> LOAD[Load &amp; Compile<br/>Stylesheet]
                XSL[(XSLT File<br/>on disk)] --> LOAD
                SD[Static Data /<br/>Config Variables] --> PARAMS[XSLT Parameters]
                LOAD --> TRANSFORM[XslCompiledTransform]
                PARAMS --> TRANSFORM
                INT --> TRANSFORM
                TRANSFORM --> OUT{xsl:output method?}
                OUT -->|xml| XML[.xml file]
                OUT -->|html| HTML[.htm file]
                OUT -->|text| TXT[.csv / .txt file]
                XML --> DEST[Destination]
                HTML --> DEST
                TXT --> DEST

                style XSL fill:#F5A623,color:#fff
                style TRANSFORM fill:#4A90D9,color:#fff
            """;

        var result = renderer.RenderToSvg(definition);

        Assert.Contains("<svg", result);
        Assert.Contains("</svg>", result);
        // Cylinder node
        Assert.Contains("XSLT File", result);
        Assert.Contains("<ellipse ", result);
        Assert.Contains("<path ", result);
        // Diamond node
        Assert.Contains("xsl:output method?", result);
        Assert.Contains("<polygon ", result);
        // Colours applied
        Assert.Contains("#F5A623", result);
        Assert.Contains("#4A90D9", result);
        // All plain rectangular nodes present
        Assert.Contains("Source Data", result);
        Assert.Contains("XslCompiledTransform", result);
        Assert.Contains("Destination", result);
    }

    [Fact]
    public void RenderToSvg_NodeTextWithBackslash_EscapedAsXmlEntity()
    {
        // Backslashes in node text must be rendered as &#92; so that \< is never
        // produced in SVG text content, which would be treated as a Markdown
        // backslash-escape and cause the </text> closing tag to appear as visible text.
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart LR
                A[(C:\Stylesheets\)] --> B[output\reports\]
            """;

        var result = renderer.RenderToSvg(definition);

        Assert.Contains("<svg", result);
        // Backslash must be present as XML entity, not as a raw character.
        Assert.Contains("&#92;", result);
        // The text content must not contain a raw backslash followed immediately by
        // the SVG closing tag (which would be misinterpreted as \< Markdown escape).
        Assert.DoesNotMatch(@"\\</text>", result);
    }

    [Fact]
    public void RenderToSvg_LongEdgeLabel_IsWordWrapped()
    {
        // Edge labels longer than EdgeLabelWrapChars should be split at a word boundary
        // so they don't overflow into adjacent nodes and get hidden by node fills.
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart LR
                SRC[Source] -->|DataReader → DataSet → XML| DEST[Destination]
            """;

        var result = renderer.RenderToSvg(definition);

        Assert.Contains("<svg", result);
        // The full label text must be present (split across two <text> elements).
        Assert.Contains("DataReader", result);
        Assert.Contains("DataSet", result);
        Assert.Contains("XML", result);
        // The label must NOT appear as a single unbroken text line (it should be
        // split since "DataReader → DataSet → XML" is 26 chars > EdgeLabelWrapChars=22).
        Assert.DoesNotContain(">DataReader → DataSet → XML<", result);
    }

    [Fact]
    public void RenderToSvg_EdgeLabelsRenderedAfterNodes()
    {
        // Edge labels must be rendered AFTER nodes in the SVG output so that
        // the white label background (fill="#fff") appears on top of node fills
        // rather than being covered by them.
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart LR
                A[Source] -->|label| B[Destination]
            """;

        var result = renderer.RenderToSvg(definition);

        // Find position of last node rect and first edge label rect.
        // We use simple string searches (IndexOf / LastIndexOf) rather than regex
        // to avoid any ReDoS risk when scanning the SVG output.
        var lastNodePos = result.LastIndexOf("rx=\"4\"", StringComparison.Ordinal);
        var firstLabelPos = result.IndexOf("stroke=\"#ccc\"", StringComparison.Ordinal);

        Assert.True(lastNodePos >= 0, "No node rect found.");
        Assert.True(firstLabelPos >= 0, "No edge label rect found.");
        Assert.True(firstLabelPos > lastNodePos,
            $"Edge label rect (pos {firstLabelPos}) must appear after last node rect (pos {lastNodePos}).");
    }

    [Fact]
    public void RenderToSvg_XsltPipelineDiagram_BackslashAndLongLabelRenderedCorrectly()
    {
        // Full smoke test for the XSLT pipeline diagram from the issue:
        // covers cylinder backslash text, long edge label wrapping, and node ordering.
        var renderer = new FlowchartRenderer();
        var definition = """
            flowchart LR
                DB[(SQL Server<br/>ERP Database)] -->|query| SRC[Database Source<br/>OpenOrders]
                SRC -->|DataReader → DataSet → XML| XSLT[XSLT Conversion<br/>order-transform]
                XSL[(orders-to-report.xslt<br/>C:\Stylesheets\)] -->|compiled| XSLT
                PARAMS[Parameters<br/>companyid=acme<br/>date=20250115<br/>department=Finance<br/>report-title=Open Orders Report] -->|xsl:param| XSLT
                XSLT -->|html output| FILE[acme_Orders_20250115.html]
                FILE --> DEST[Folder Destination<br/>output\order-reports\]

                style XSLT fill:#F5A623,color:#fff
                style XSL fill:#F5A623,color:#fff
                style DB fill:#4A90D9,color:#fff
            """;

        var result = renderer.RenderToSvg(definition);

        Assert.Contains("<svg", result);
        Assert.Contains("</svg>", result);
        // Cylinders are rendered correctly.
        Assert.Contains("<ellipse ", result);
        // Backslashes in node text are escaped as XML entities.
        Assert.Contains("&#92;", result);
        Assert.DoesNotMatch(@"\\</text>", result);
        // Long edge label is split (not one long line).
        Assert.Contains("DataReader", result);
        Assert.Contains("DataSet", result);
        Assert.DoesNotContain(">DataReader → DataSet → XML<", result);
        // Styles applied.
        Assert.Contains("#F5A623", result);
        Assert.Contains("#4A90D9", result);
        // Other nodes present.
        Assert.Contains("SQL Server", result);
        Assert.Contains("ERP Database", result);
        Assert.Contains("XSLT Conversion", result);
        Assert.Contains("Folder Destination", result);
        Assert.Contains("Parameters", result);
    }
}
