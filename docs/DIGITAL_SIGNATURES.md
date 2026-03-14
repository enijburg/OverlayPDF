# Implementing Cryptographic Digital Signatures

## Current Implementation

The current `SignatureFieldTagWorker` creates **text input form fields** that can be filled with typed text (e.g., "John Doe"). These are suitable for:
- Simple signature workflows where users type their name
- Print-and-sign scenarios
- Internal documents that don't require legal digital signatures

## Enabling Cryptographic Digital Signatures

To implement true cryptographic digital signatures with certificates, you would need to:

### 1. Post-Process the PDF After Generation

After generating the PDF with signature fields, convert them to digital signature fields:

```csharp
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Pdf;
using iText.Signatures;

public void ConvertToDigitalSignatureFields(string inputPdf, string outputPdf)
{
    using var reader = new PdfReader(inputPdf);
    using var writer = new PdfWriter(outputPdf);
    using var pdfDoc = new PdfDocument(reader, writer);
    
    var form = PdfAcroForm.GetAcroForm(pdfDoc, false);
    if (form == null) return;
    
    var fields = form.GetAllFormFields();
    foreach (var fieldEntry in fields)
    {
        var fieldName = fieldEntry.Key;
        var field = fieldEntry.Value;
        
        // Convert fields with "Signature" in the name to signature fields
        if (fieldName.Contains("Signature", StringComparison.OrdinalIgnoreCase))
        {
            // Remove the old text field
            form.RemoveField(fieldName);
            
            // Create a new signature field
            var signatureField = new PdfSignatureFormField(pdfDoc);
            signatureField.SetFieldName(fieldName);
            
            // Get the widget annotation from the original field
            var widgets = field.GetWidgets();
            if (widgets != null && widgets.Count > 0)
            {
                var widget = widgets[0];
                var rect = widget.GetRectangle().ToRectangle();
                var newWidget = new PdfWidgetAnnotation(rect);
                
                signatureField.AddKid(newWidget);
                newWidget.SetPage(widget.GetPage());
            }
            
            // Add the signature field back to the form
            form.AddField(signatureField);
        }
    }
}
```

### 2. Sign the PDF with a Digital Certificate

Once you have proper signature fields, sign them with a certificate:

```csharp
using iText.Kernel.Pdf;
using iText.Signatures;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Pkcs;
using System.Security.Cryptography.X509Certificates;

public void SignPdf(string inputPdf, string outputPdf, string certificatePath, 
    string password, string fieldName)
{
    // Load the certificate
    using var certStream = new FileStream(certificatePath, FileMode.Open, FileAccess.Read);
    var pkcs12 = new Pkcs12Store(certStream, password.ToCharArray());
    
    string alias = null;
    foreach (string a in pkcs12.Aliases)
    {
        if (pkcs12.IsKeyEntry(a))
        {
            alias = a;
            break;
        }
    }
    
    var pk = pkcs12.GetKey(alias).Key;
    var chain = pkcs12.GetCertificateChain(alias);
    var cert = chain[0].Certificate;
    
    // Create the signer
    using var reader = new PdfReader(inputPdf);
    using var writer = new FileStream(outputPdf, FileMode.Create);
    
    var signer = new PdfSigner(reader, writer, new StampingProperties());
    
    // Create the signature appearance
    var appearance = signer.GetSignatureAppearance()
        .SetReason("Document Approval")
        .SetLocation("Office")
        .SetSignatureCreator("PDF Generator");
    
    signer.SetFieldName(fieldName);
    
    // Create external signature and sign
    IExternalSignature pks = new PrivateKeySignature(pk, DigestAlgorithms.SHA256);
    signer.SignDetached(pks, chain, null, null, null, 0, PdfSigner.CryptoStandard.CMS);
}
```

### 3. Modify SignatureFieldTagWorker for Native Support

For native cryptographic signature field creation during HTML-to-PDF conversion, you would need to:

1. Implement a custom renderer that creates `PdfSignatureFormField` instead of text fields
2. Handle the complexity of iText's rendering pipeline
3. Deal with timing issues (signature fields need the document structure to be complete)

This approach is complex and not recommended for HTML-to-PDF workflows. The post-processing approach (option 1) is cleaner and more maintainable.

## Recommended Workflow

1. **Generate PDF** with current implementation (creates text input fields)
2. **Convert to signature fields** using the post-processing script above
3. **Sign the PDF** programmatically with certificates, or
4. **Distribute to users** who can sign using PDF readers (Adobe Acrobat, etc.)

## Testing Digital Signatures

For testing cryptographic signatures, you would need:

```csharp
[Fact]
public void SignatureField_CanBeDigitallySigned()
{
    // 1. Generate PDF with signature fields
    var pdfPath = GeneratePdfWithSignatureFields();
    
    // 2. Convert to digital signature fields
    var convertedPath = ConvertToDigitalSignatureFields(pdfPath);
    
    // 3. Sign with certificate
    var signedPath = SignWithCertificate(convertedPath, "test.pfx", "password");
    
    // 4. Verify signature
    using var pdfDoc = new PdfDocument(new PdfReader(signedPath));
    var form = PdfAcroForm.GetAcroForm(pdfDoc, false);
    var signatureField = form.GetField("Signature_Field");
    
    Assert.NotNull(signatureField);
    Assert.True(signatureField is PdfSignatureFormField);
    
    // Verify the signature is valid
    var sigUtil = new SignatureUtil(pdfDoc);
    var names = sigUtil.GetSignatureNames();
    Assert.Contains("Signature_Field", names);
    Assert.True(sigUtil.SignatureCoversWholeDocument("Signature_Field"));
}
```

## Additional Resources

- [iText Digital Signatures Documentation](https://kb.itextpdf.com/itext/digital-signatures)
- [PDF Digital Signature Standards](https://www.adobe.com/devnet-docs/acrobatetk/tools/DigSig/index.html)
- [BouncyCastle Cryptography](https://www.bouncycastle.org/csharp/)

##Conclusion

The current implementation provides a foundation for signature workflows. For cryptographic digital signatures:
- Use post-processing to convert text fields to signature fields
- Implement signing with certificates using iText's `PdfSigner` API
- Consider workflow requirements (who signs, when, verification needs)

Native cryptographic signature field creation during HTML-to-PDF conversion is possible but adds significant complexity. The post-processing approach is recommended for most use cases.
