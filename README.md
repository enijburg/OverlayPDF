# OverlayPDF

## Author

Ewart Nijburg<br>
Troolean

## Project Overview

OverlayPDF is a .NET 10 application designed to overlay text PDFs with predefined template PDFs. It uses the iText library to merge PDFs, applying a specific template to the first page and a different template to subsequent pages. This is useful for creating consistent document formats, such as adding letterheads or footers to documents.

## Key Features

- Overlay text PDFs with template PDFs.
- Apply different templates to the first page and continuation pages.
- Configurable template paths via `appsettings.json`.
- Pass a markdown file (*.md) as argument and it will render as pdf based on the templates

![PDF Overlay Sample](assets/PDFOverlay_Sample.png)

## Installation

1. Clone the repository
2. In the project root, run `dotnet publish`
3. Navigate to the publish directory and Configure the application by editing `appsettings.json`

```
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "PdfOverlayOptions": {
    "TemplateDirectory": "C:\\Temp\\Templates",
    "FirstPageTemplate": "InitialPageTemplate.pdf",
    "ContinuationPageTemplate": "ContinuationPageTemplate.pdf",
    "DefaultFontFamily": "Arial",
    "ContinuationTopMarginPoints": 72,
    "ContinuationBottomMarginPoints": 36
  }
}
```

4. Run the application with `OverlayPDF.exe "path to pdf with text"`
5. Create a shortcut on the desktop and next time drop a PDF file on the shortcut to overlay it with the templates.
6. A new pdf will be created in the same directory as the input pdf with the suffix `_overlay`.

## The project uses the following libraries

- The awesome iText library - https://itextpdf.com/
