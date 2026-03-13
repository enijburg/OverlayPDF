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
}
