using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Forms;
using Microsoft.Extensions.Options;
using OverlayPDF.Markdown;
using Xunit;
using Path = System.IO.Path;
using Microsoft.Extensions.Logging;

namespace OverlayPDF.Tests;

/// <summary>
/// Tests for PdfGenerator's signature block generation functionality.
/// </summary>
public class PdfGeneratorSignatureTests
{
    [Fact]
    public void GeneratePdfFromMarkdown_WithSignatureBlock_CreatesAcroFormFields()
    {
        // Arrange
        var markdownPath = Path.Combine(Path.GetTempPath(), $"test_signatures_{Guid.NewGuid()}.md");
        var outputPdfPath = Path.Combine(Path.GetTempPath(), $"test_signatures_output_{Guid.NewGuid()}.pdf");

        var markdownContent = @"# Test Document

## Introduction
This document contains signature blocks.

```signatures
## Approval Signatures

| Field | Project Manager | Director |
|-------|----------------|----------|
| **Name** | ... | ... |
| **Signature** | ... | ... |
| **Date** | ... | ... |

---

## Client Signatures

| Field | Client Representative | Witness |
|-------|----------------------|---------|
| **Name** | John Smith | ... |
| **Signature** | ... | ... |
| **Date** | [Date] | ... |
```

## Conclusion
Document ends here.
";

        File.WriteAllText(markdownPath, markdownContent);

        try
        {
            var pdfGenerator = CreatePdfGenerator();

            // Act
            pdfGenerator.GeneratePdfFromMarkdown(markdownPath, outputPdfPath, PageSize.A4);

            // Assert
            Assert.True(File.Exists(outputPdfPath), "PDF file should be created");

            using var pdfDoc = new PdfDocument(new PdfReader(outputPdfPath));
            var acroForm = PdfAcroForm.GetAcroForm(pdfDoc, false);

            Assert.NotNull(acroForm);

            var fields = acroForm.GetAllFormFields();
            Assert.NotEmpty(fields);

            // Verify expected signature fields exist
            Assert.True(fields.ContainsKey("ApprovalSignatures_ProjectManager_Name"));
            Assert.True(fields.ContainsKey("ApprovalSignatures_ProjectManager_Signature"));
            Assert.True(fields.ContainsKey("ApprovalSignatures_ProjectManager_Date"));
            Assert.True(fields.ContainsKey("ApprovalSignatures_Director_Name"));
            Assert.True(fields.ContainsKey("ApprovalSignatures_Director_Signature"));
            Assert.True(fields.ContainsKey("ApprovalSignatures_Director_Date"));
            Assert.True(fields.ContainsKey("ClientSignatures_Witness_Name"));
            Assert.True(fields.ContainsKey("ClientSignatures_ClientRepresentative_Signature"));
            Assert.True(fields.ContainsKey("ClientSignatures_Witness_Signature"));
            Assert.True(fields.ContainsKey("ClientSignatures_Witness_Date"));

            // Verify field count (10 editable fields)
            Assert.Equal(10, fields.Count);
        }
        finally
        {
            // Cleanup
            if (File.Exists(markdownPath))
            {
                File.Delete(markdownPath);
            }
            if (File.Exists(outputPdfPath))
            {
                File.Delete(outputPdfPath);
            }
        }
    }

    [Fact]
    public void GeneratePdfFromMarkdown_WithMultipleSignatureBlocks_CreatesAllFields()
    {
        // Arrange
        var markdownPath = Path.Combine(Path.GetTempPath(), $"test_multiple_sigs_{Guid.NewGuid()}.md");
        var outputPdfPath = Path.Combine(Path.GetTempPath(), $"test_multiple_sigs_output_{Guid.NewGuid()}.pdf");

        var markdownContent = @"# Multi-Signature Document

```signatures
## Internal Approval

| Field | Manager | Executive |
|-------|---------|-----------|
| **Name** | ... | ... |
| **Date** | ... | ... |
```

Some content between signature blocks.

```signatures
## External Approval

| Field | Partner | Auditor |
|-------|---------|---------|
| **Name** | ... | ... |
| **Signature** | ... | ... |
```
";

        File.WriteAllText(markdownPath, markdownContent);

        try
        {
            var pdfGenerator = CreatePdfGenerator();

            // Act
            pdfGenerator.GeneratePdfFromMarkdown(markdownPath, outputPdfPath, PageSize.A4);

            // Assert
            Assert.True(File.Exists(outputPdfPath), "PDF file should be created");

            using var pdfDoc = new PdfDocument(new PdfReader(outputPdfPath));
            var acroForm = PdfAcroForm.GetAcroForm(pdfDoc, false);

            Assert.NotNull(acroForm);

            var fields = acroForm.GetAllFormFields();
            Assert.NotEmpty(fields);

            // First block fields
            Assert.True(fields.ContainsKey("InternalApproval_Manager_Name"));
            Assert.True(fields.ContainsKey("InternalApproval_Executive_Name"));
            Assert.True(fields.ContainsKey("InternalApproval_Manager_Date"));
            Assert.True(fields.ContainsKey("InternalApproval_Executive_Date"));

            // Second block fields
            Assert.True(fields.ContainsKey("ExternalApproval_Partner_Name"));
            Assert.True(fields.ContainsKey("ExternalApproval_Auditor_Name"));
            Assert.True(fields.ContainsKey("ExternalApproval_Partner_Signature"));
            Assert.True(fields.ContainsKey("ExternalApproval_Auditor_Signature"));

            // Total: 8 fields (4 from each block)
            Assert.Equal(8, fields.Count);
        }
        finally
        {
            // Cleanup
            if (File.Exists(markdownPath))
            {
                File.Delete(markdownPath);
            }
            if (File.Exists(outputPdfPath))
            {
                File.Delete(outputPdfPath);
            }
        }
    }

    [Fact]
    public void GeneratePdfFromMarkdown_SignatureFieldsCanBeFilled()
    {
        // Arrange
        var markdownPath = Path.Combine(Path.GetTempPath(), $"test_fill_sigs_{Guid.NewGuid()}.md");
        var outputPdfPath = Path.Combine(Path.GetTempPath(), $"test_fill_sigs_output_{Guid.NewGuid()}.pdf");
        var filledPdfPath = Path.Combine(Path.GetTempPath(), $"test_fill_sigs_filled_{Guid.NewGuid()}.pdf");

        var markdownContent = @"# Fillable Signature Document

```signatures
## Approvals

| Field | Approver |
|-------|----------|
| **Name** | ... |
| **Title** | ... |
| **Signature** | ... |
| **Date** | ... |
```
";

        File.WriteAllText(markdownPath, markdownContent);

        try
        {
            var pdfGenerator = CreatePdfGenerator();

            // Act - Generate PDF
            pdfGenerator.GeneratePdfFromMarkdown(markdownPath, outputPdfPath, PageSize.A4);

            // Fill the form fields
            using (var pdfDoc = new PdfDocument(new PdfReader(outputPdfPath), new PdfWriter(filledPdfPath)))
            {
                var acroForm = PdfAcroForm.GetAcroForm(pdfDoc, false);
                Assert.NotNull(acroForm);

                acroForm.GetField("Approvals_Approver_Name")?.SetValue("Jane Doe");
                acroForm.GetField("Approvals_Approver_Title")?.SetValue("Senior Manager");
                acroForm.GetField("Approvals_Approver_Signature")?.SetValue("J. Doe");
                acroForm.GetField("Approvals_Approver_Date")?.SetValue("2025-01-20");
            }

            // Assert - Verify filled values
            using (var pdfDoc = new PdfDocument(new PdfReader(filledPdfPath)))
            {
                var acroForm = PdfAcroForm.GetAcroForm(pdfDoc, false);
                Assert.NotNull(acroForm);

                var fields = acroForm.GetAllFormFields();
                Assert.Equal("Jane Doe", fields["Approvals_Approver_Name"].GetValueAsString());
                Assert.Equal("Senior Manager", fields["Approvals_Approver_Title"].GetValueAsString());
                Assert.Equal("J. Doe", fields["Approvals_Approver_Signature"].GetValueAsString());
                Assert.Equal("2025-01-20", fields["Approvals_Approver_Date"].GetValueAsString());
            }
        }
        finally
        {
            // Cleanup
            if (File.Exists(markdownPath))
            {
                File.Delete(markdownPath);
            }
            if (File.Exists(outputPdfPath))
            {
                File.Delete(outputPdfPath);
            }
            if (File.Exists(filledPdfPath))
            {
                File.Delete(filledPdfPath);
            }
        }
    }

    [Fact]
    public void GeneratePdfFromMarkdown_WithPrefilledValues_MixesFieldsAndStaticText()
    {
        // Arrange
        var markdownPath = Path.Combine(Path.GetTempPath(), $"test_prefilled_{Guid.NewGuid()}.md");
        var outputPdfPath = Path.Combine(Path.GetTempPath(), $"test_prefilled_output_{Guid.NewGuid()}.pdf");

        var markdownContent = @"# Mixed Signature Document

```signatures
## Review

| Field | Reviewer | Manager |
|-------|----------|---------|
| **Name** | Alice Smith | ... |
| **Status** | Approved | ... |
| **Date** | 2025-01-15 | ... |
```
";

        File.WriteAllText(markdownPath, markdownContent);

        try
        {
            var pdfGenerator = CreatePdfGenerator();

            // Act
            pdfGenerator.GeneratePdfFromMarkdown(markdownPath, outputPdfPath, PageSize.A4);

            // Assert
            Assert.True(File.Exists(outputPdfPath), "PDF file should be created");

            using var pdfDoc = new PdfDocument(new PdfReader(outputPdfPath));
            var acroForm = PdfAcroForm.GetAcroForm(pdfDoc, false);

            Assert.NotNull(acroForm);

            var fields = acroForm.GetAllFormFields();

            // Only 3 fields should be editable (Manager columns)
            // The Reviewer column has pre-filled values, so no form fields
            Assert.Equal(3, fields.Count);
            Assert.True(fields.ContainsKey("Review_Manager_Name"));
            Assert.True(fields.ContainsKey("Review_Manager_Status"));
            Assert.True(fields.ContainsKey("Review_Manager_Date"));
        }
        finally
        {
            // Cleanup
            if (File.Exists(markdownPath))
            {
                File.Delete(markdownPath);
            }
            if (File.Exists(outputPdfPath))
            {
                File.Delete(outputPdfPath);
            }
        }
    }

    [Fact]
    public void GeneratePdfFromMarkdown_WithoutSignatureBlocks_NoAcroFormFields()
    {
        // Arrange
        var markdownPath = Path.Combine(Path.GetTempPath(), $"test_no_sigs_{Guid.NewGuid()}.md");
        var outputPdfPath = Path.Combine(Path.GetTempPath(), $"test_no_sigs_output_{Guid.NewGuid()}.pdf");

        var markdownContent = @"# Regular Document

## Section 1
This is a regular markdown document without any signature blocks.

## Section 2
Just normal content here.
";

        File.WriteAllText(markdownPath, markdownContent);

        try
        {
            var pdfGenerator = CreatePdfGenerator();

            // Act
            pdfGenerator.GeneratePdfFromMarkdown(markdownPath, outputPdfPath, PageSize.A4);

            // Assert
            Assert.True(File.Exists(outputPdfPath), "PDF file should be created");

            using var pdfDoc = new PdfDocument(new PdfReader(outputPdfPath));
            var acroForm = PdfAcroForm.GetAcroForm(pdfDoc, false);

            // No form fields should exist
            if (acroForm != null)
            {
                var fields = acroForm.GetAllFormFields();
                Assert.Empty(fields);
            }
        }
        finally
        {
            // Cleanup
            if (File.Exists(markdownPath))
            {
                File.Delete(markdownPath);
            }
            if (File.Exists(outputPdfPath))
            {
                File.Delete(outputPdfPath);
            }
        }
    }

    private static PdfGenerator CreatePdfGenerator()
    {
        var options = Options.Create(new PdfOverlayOptions
        {
            TemplateDirectory = Path.GetTempPath(),
            DefaultFontFamily = "Helvetica",
            FontsDirectory = null,
            FirstPageTemplate = "dummy.pdf",
            ContinuationPageTemplate = "dummy.pdf"
        });

        var timelineRenderer = new Markdown.TimelineRenderer();
        var signatureBlockRenderer = new SignatureBlockRenderer();
        var markdownProcessor = new Markdown.MarkdownProcessor(timelineRenderer, signatureBlockRenderer);

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<PdfGenerator>();

        return new PdfGenerator(options, markdownProcessor, logger);
    }
}
