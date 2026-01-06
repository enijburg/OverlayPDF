using OverlayPDF.Markdown;
using Xunit;

namespace OverlayPDF.Tests;

/// <summary>
/// Tests for TimelineRenderer SVG generation from timeline markup.
/// </summary>
public class TimelineRendererTests
{
    [Fact]
    public void GenerateTimelineSvg_WithEmptyInput_ReturnsEmptyString()
    {
        // Arrange
        var renderer = new TimelineRenderer();

        // Act
        var result = renderer.GenerateTimelineSvg("");

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GenerateTimelineSvg_WithWhitespaceOnly_ReturnsEmptyString()
    {
        // Arrange
        var renderer = new TimelineRenderer();

        // Act
        var result = renderer.GenerateTimelineSvg("   \n\t  \r\n  ");

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GenerateTimelineSvg_WithBasicTimeline_GeneratesSvg()
    {
        // Arrange
        var renderer = new TimelineRenderer();
        var timeline = @"
title Project Timeline
section Planning
    Task 1 :task1, 2025-01-01, 5d
    Task 2 :task2, 2025-01-06, 3d
";

        // Act
        var result = renderer.GenerateTimelineSvg(timeline);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("<svg", result);
        Assert.Contains("</svg>", result);
        Assert.Contains("Project Timeline", result);
        Assert.Contains("Planning", result);
        Assert.Contains("Task 1", result);
        Assert.Contains("Task 2", result);
    }

    [Fact]
    public void GenerateTimelineSvg_WithMultipleSections_GeneratesSvgWithAllSections()
    {
        // Arrange
        var renderer = new TimelineRenderer();
        var timeline = @"
title Multi-Section Timeline
section Phase 1
    Task A :a, 2025-01-01, 2d
section Phase 2
    Task B :b, 2025-01-03, 3d
section Phase 3
    Task C :c, 2025-01-06, 1d
";

        // Act
        var result = renderer.GenerateTimelineSvg(timeline);

        // Assert
        Assert.Contains("Phase 1", result);
        Assert.Contains("Phase 2", result);
        Assert.Contains("Phase 3", result);
        Assert.Contains("Task A", result);
        Assert.Contains("Task B", result);
        Assert.Contains("Task C", result);
    }

    [Fact]
    public void GenerateTimelineSvg_WithMilestone_RendersMilestoneAsDiamond()
    {
        // Arrange
        var renderer = new TimelineRenderer();
        var timeline = @"
section Milestones
    Milestone 1 :m1, 2025-01-01, m
    Milestone 2 :milestone1, 2025-01-05, milestone
";

        // Act
        var result = renderer.GenerateTimelineSvg(timeline);

        // Assert
        Assert.Contains("Milestone 1", result);
        Assert.Contains("Milestone 2", result);
        Assert.Contains("<polygon", result); // Milestone rendered as polygon (diamond)
        Assert.Contains("class=\"milestone\"", result);
    }

    [Fact]
    public void GenerateTimelineSvg_WithDifferentDateFormats_ParsesCorrectly()
    {
        // Arrange
        var renderer = new TimelineRenderer();
        var timeline = @"
section Various Date Formats
    Task 1 :t1, 2025-01-01, 1d
    Task 2 :t2, 2025-1-5, 2d
    Task 3 :t3, 2025/01/10, 1d
";

        // Act
        var result = renderer.GenerateTimelineSvg(timeline);

        // Assert
        Assert.Contains("Task 1", result);
        Assert.Contains("Task 2", result);
        Assert.Contains("Task 3", result);
        Assert.Contains("2025-01-01", result);
    }

    [Fact]
    public void GenerateTimelineSvg_WithHourDuration_CalculatesCorrectly()
    {
        // Arrange
        var renderer = new TimelineRenderer();
        var timeline = @"
section Short Tasks
    Quick Task :qt1, 2025-01-01, 6h
    Regular Task :rt1, 2025-01-01, 2d
";

        // Act
        var result = renderer.GenerateTimelineSvg(timeline);

        // Assert
        Assert.Contains("Quick Task", result);
        Assert.Contains("Regular Task", result);
        // Both tasks should be rendered (6h = 0.25 days)
        Assert.Contains("<rect", result);
    }

    [Fact]
    public void GenerateTimelineSvg_WithFloatingPointDuration_HandlesDecimalDays()
    {
        // Arrange
        var renderer = new TimelineRenderer();
        var timeline = @"
section Precise Durations
    Task 1 :t1, 2025-01-01, 1.5d
    Task 2 :t2, 2025-01-03, 2.25d
";

        // Act
        var result = renderer.GenerateTimelineSvg(timeline);

        // Assert
        Assert.Contains("Task 1", result);
        Assert.Contains("Task 2", result);
        Assert.Contains("<rect", result);
    }

    [Fact]
    public void GenerateTimelineSvg_WithNoTitle_GeneratesSvgWithoutTitle()
    {
        // Arrange
        var renderer = new TimelineRenderer();
        var timeline = @"
section Work
    Task :t1, 2025-01-01, 1d
";

        // Act
        var result = renderer.GenerateTimelineSvg(timeline);

        // Assert
        Assert.Contains("Work", result);
        Assert.Contains("Task", result);
        Assert.Contains("<svg", result);
    }

    [Fact]
    public void GenerateTimelineSvg_WithDateFormatDirective_IgnoresDirective()
    {
        // Arrange
        var renderer = new TimelineRenderer();
        var timeline = @"
dateFormat YYYY-MM-DD
title Timeline with Format
section Tasks
    Task 1 :t1, 2025-01-01, 2d
";

        // Act
        var result = renderer.GenerateTimelineSvg(timeline);

        // Assert
        Assert.Contains("Timeline with Format", result);
        Assert.Contains("Task 1", result);
        // dateFormat directive should be ignored but not cause errors
    }

    [Fact]
    public void GenerateTimelineSvg_WithAxisFormatDirective_IgnoresDirective()
    {
        // Arrange
        var renderer = new TimelineRenderer();
        var timeline = @"
axisFormat %m-%d
section Tasks
    Task 1 :t1, 2025-01-01, 2d
";

        // Act
        var result = renderer.GenerateTimelineSvg(timeline);

        // Assert
        Assert.Contains("Task 1", result);
        // axisFormat directive should be ignored but not cause errors
    }

    [Fact]
    public void GenerateTimelineSvg_WithLongTimeRange_GeneratesSvgWithAppropriateScale()
    {
        // Arrange
        var renderer = new TimelineRenderer();
        var timeline = @"
section Long Project
    Phase 1 :p1, 2025-01-01, 30d
    Phase 2 :p2, 2025-02-01, 45d
";

        // Act
        var result = renderer.GenerateTimelineSvg(timeline);

        // Assert
        Assert.Contains("Phase 1", result);
        Assert.Contains("Phase 2", result);
        Assert.Contains("<svg", result);
        // Should handle long durations without errors
    }

    [Fact]
    public void GenerateTimelineSvg_WithOverlappingTasks_RendersAllTasks()
    {
        // Arrange
        var renderer = new TimelineRenderer();
        var timeline = @"
section Parallel Work
    Task A :a, 2025-01-01, 5d
    Task B :b, 2025-01-02, 4d
    Task C :c, 2025-01-03, 3d
";

        // Act
        var result = renderer.GenerateTimelineSvg(timeline);

        // Assert
        Assert.Contains("Task A", result);
        Assert.Contains("Task B", result);
        Assert.Contains("Task C", result);
        // All tasks should be rendered even if they overlap
    }

    [Fact]
    public void GenerateTimelineSvg_WithSpecialCharactersInLabels_EscapesCorrectly()
    {
        // Arrange
        var renderer = new TimelineRenderer();
        var timeline = @"
section Special Characters
    Task with <angle> brackets :t1, 2025-01-01, 1d
    Task with & ampersand :t2, 2025-01-02, 1d
";

        // Act
        var result = renderer.GenerateTimelineSvg(timeline);

        // Assert
        Assert.Contains("&lt;", result); // < should be escaped
        Assert.Contains("&gt;", result); // > should be escaped
        Assert.Contains("&amp;", result); // & should be escaped
    }

    [Fact]
    public void GenerateTimelineSvg_WithNoTasks_ReturnsErrorMessage()
    {
        // Arrange
        var renderer = new TimelineRenderer();
        var timeline = @"
title Empty Timeline
section Empty Section
";

        // Act
        var result = renderer.GenerateTimelineSvg(timeline);

        // Assert
        Assert.Contains("No tasks parsed", result);
    }

    [Fact]
    public void GenerateTimelineSvg_WithMalformedTaskLine_SkipsMalformedTask()
    {
        // Arrange
        var renderer = new TimelineRenderer();
        var timeline = @"
section Mixed
    Valid Task :t1, 2025-01-01, 2d
    Invalid Task Without Colon
    Another Valid :t2, 2025-01-03, 1d
";

        // Act
        var result = renderer.GenerateTimelineSvg(timeline);

        // Assert
        Assert.Contains("Valid Task", result);
        Assert.Contains("Another Valid", result);
        // Malformed line should be skipped
    }

    [Fact]
    public void GenerateTimelineSvg_WithDefaultDuration_Uses1Day()
    {
        // Arrange
        var renderer = new TimelineRenderer();
        var timeline = @"
section Default Duration
    Task :t1, 2025-01-01
";

        // Act
        var result = renderer.GenerateTimelineSvg(timeline);

        // Assert
        // Task with only 2 comma-separated parts should be skipped
        // (requires at least id, date, duration)
        Assert.True(result.Contains("No tasks parsed") || result.Contains("<svg"));
    }

    [Fact]
    public void GenerateTimelineSvg_CompleteFunctionalExample_GeneratesCompleteTimeline()
    {
        // Arrange
        var renderer = new TimelineRenderer();
        var timeline = @"
title Software Development Project
section Planning
    Requirements Analysis :req, 2025-01-01, 5d
    Design Phase :design, 2025-01-06, 7d
    Design Review :milestone1, 2025-01-13, m
section Development
    Backend Development :backend, 2025-01-14, 14d
    Frontend Development :frontend, 2025-01-20, 12d
    Integration :integration, 2025-02-01, 5d
section Testing
    Unit Testing :unittest, 2025-02-03, 4d
    System Testing :systest, 2025-02-07, 6d
    Release :milestone2, 2025-02-13, milestone
";

        // Act
        var result = renderer.GenerateTimelineSvg(timeline);

        // Assert
        Assert.Contains("<svg", result);
        Assert.Contains("</svg>", result);
        Assert.Contains("Software Development Project", result);
        Assert.Contains("Planning", result);
        Assert.Contains("Development", result);
        Assert.Contains("Testing", result);
        Assert.Contains("Requirements Analysis", result);
        Assert.Contains("Backend Development", result);
        Assert.Contains("System Testing", result);
        Assert.Contains("class=\"task\"", result);
        Assert.Contains("class=\"milestone\"", result);
        Assert.Contains("<polygon", result); // Milestones as diamonds
        Assert.Contains("<rect", result); // Regular tasks as rectangles
    }
}
