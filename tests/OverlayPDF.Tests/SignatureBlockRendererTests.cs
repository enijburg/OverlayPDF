using iText.Html2pdf;
using iText.Kernel.Pdf;
using iText.Forms;
using OverlayPDF.Markdown;
using Xunit;

namespace OverlayPDF.Tests;

/// <summary>
/// Tests for converting HTML with signature blocks to PDF with AcroForm fields.
/// </summary>
public class SignatureBlockRendererTests
{
    [Fact]
    public void ConvertSignatureBlockHtmlToPdf_WithAcroForm_Success()
    {
        // Arrange
        var html = GetTestSignatureBlockHtml();
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_signature_block_{Guid.NewGuid()}.pdf");

        try
        {
            // Act
            ConvertHtmlToPdfWithAcroForm(html, outputPath);

            // Assert
            Assert.True(File.Exists(outputPath), "PDF file should be created");

            // Verify the PDF contains AcroForm fields
            using var pdfDoc = new PdfDocument(new PdfReader(outputPath));
            var acroForm = PdfAcroForm.GetAcroForm(pdfDoc, false);

            Assert.NotNull(acroForm);

            var fields = acroForm.GetAllFormFields();
            Assert.NotEmpty(fields);

            // Verify expected fields exist
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

            // Verify field count matches expected (10 fields total)
            Assert.Equal(10, fields.Count);
        }
        finally
        {
            // Cleanup
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public void ConvertSignatureBlockHtmlToPdf_CanFillFields()
    {
        // Arrange
        var html = GetTestSignatureBlockHtml();
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_signature_filled_{Guid.NewGuid()}.pdf");
        var filledOutputPath = Path.Combine(Path.GetTempPath(), $"test_signature_filled_result_{Guid.NewGuid()}.pdf");

        try
        {
            // Act - Create PDF with form fields
            ConvertHtmlToPdfWithAcroForm(html, outputPath);

            // Fill in the form fields
            using (var pdfDoc = new PdfDocument(new PdfReader(outputPath), new PdfWriter(filledOutputPath)))
            {
                var acroForm = PdfAcroForm.GetAcroForm(pdfDoc, false);
                Assert.NotNull(acroForm);

                // Fill in some test data
                acroForm.GetField("ApprovalSignatures_ProjectManager_Name")?.SetValue("Alice Johnson");
                acroForm.GetField("ApprovalSignatures_ProjectManager_Signature")?.SetValue("A. Johnson");
                acroForm.GetField("ApprovalSignatures_ProjectManager_Date")?.SetValue("2025-01-20");
                acroForm.GetField("ApprovalSignatures_Director_Name")?.SetValue("Bob Williams");
                acroForm.GetField("ApprovalSignatures_Director_Signature")?.SetValue("B. Williams");
                acroForm.GetField("ApprovalSignatures_Director_Date")?.SetValue("2025-01-20");
            }

            // Assert - Verify filled values
            using (var pdfDoc = new PdfDocument(new PdfReader(filledOutputPath)))
            {
                var acroForm = PdfAcroForm.GetAcroForm(pdfDoc, false);
                Assert.NotNull(acroForm);

                var fields = acroForm.GetAllFormFields();
                Assert.Equal("Alice Johnson", fields["ApprovalSignatures_ProjectManager_Name"].GetValueAsString());
                Assert.Equal("A. Johnson", fields["ApprovalSignatures_ProjectManager_Signature"].GetValueAsString());
                Assert.Equal("2025-01-20", fields["ApprovalSignatures_ProjectManager_Date"].GetValueAsString());
                Assert.Equal("Bob Williams", fields["ApprovalSignatures_Director_Name"].GetValueAsString());
                Assert.Equal("B. Williams", fields["ApprovalSignatures_Director_Signature"].GetValueAsString());
                Assert.Equal("2025-01-20", fields["ApprovalSignatures_Director_Date"].GetValueAsString());
            }
        }
        finally
        {
            // Cleanup
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
            if (File.Exists(filledOutputPath))
            {
                File.Delete(filledOutputPath);
            }
        }
    }

    /// <summary>
    /// Converts HTML to PDF with AcroForm support enabled.
    /// </summary>
    private static void ConvertHtmlToPdfWithAcroForm(string html, string outputPath)
    {
        using var writer = new PdfWriter(outputPath);
        using var pdfDoc = new PdfDocument(writer);

        var properties = new ConverterProperties();
        properties.SetCreateAcroForm(true);

        HtmlConverter.ConvertToPdf(html, pdfDoc, properties);
    }

    /// <summary>
    /// Returns the test signature block HTML generated from markdown using SignatureBlockRenderer.
    /// </summary>
    private static string GetTestSignatureBlockHtml()
    {
        var markdown = @"
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
| **Date** | 2025-01-15 | ... |
";

        var renderer = new SignatureBlockRenderer();
        return renderer.ProcessSignatureBlock(markdown);
    }

    [Fact]
    public void ProcessSignatureBlock_WithEmptyHeaderPlaceholders_Success()
    {
        // Arrange
        var markdown = @"
## Approval Signatures

|               | Project Manager  | Director |
|---------------|------------------|----------|
| **Name**      |                  |          |
| **Signature** |                  |          |
| **Date**      |                  |          |

---

## Client Signatures

|               | Client Representative | Witness |
|---------------|-----------------------|---------|
| **Name**      | John Smith            |         |
| **Signature** |                       |         |
| **Date**      | 2025-01-15            |         |
";
        var renderer = new SignatureBlockRenderer();

        // Act
        var html = renderer.ProcessSignatureBlock(markdown);

        // Assert
        Assert.Contains("<table", html);
        Assert.Contains("ApprovalSignatures_ProjectManager_Name", html);
        Assert.Contains("ApprovalSignatures_Director_Name", html);
        Assert.Contains("ClientSignatures_ClientRepresentative_Name", html);
        Assert.Contains("ClientSignatures_Witness_Name", html);

        // Verify empty headers are handled with non-breaking spaces
        Assert.Contains("&nbsp;", html);

        // Convert to PDF and verify fields exist
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_empty_headers_{Guid.NewGuid()}.pdf");
        try
        {
            ConvertHtmlToPdfWithAcroForm(html, outputPath);

            using var pdfDoc = new PdfDocument(new PdfReader(outputPath));
            var acroForm = PdfAcroForm.GetAcroForm(pdfDoc, false);
            Assert.NotNull(acroForm);

            var fields = acroForm.GetAllFormFields();

            // All fields should be created even with empty headers in first column
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
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }
}
