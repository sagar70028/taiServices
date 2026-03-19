
using PuppeteerSharp;//dotnet add package PuppeteerSharp
using HtmlAgilityPack;//dotnet add package HtmlAgilityPack
using SixLabors.ImageSharp;//dotnet add package SixLabors.ImageSharp
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using iText.Kernel.Pdf;//dotnet add package itext7  && dotnet add package itext7.bouncy-castle-adapter
using System.IO.Compression;
using MailKit.Net.Smtp; //dotnet add package MailKit
using MailKit.Security;
using MimeKit;

public class PdfService : IPdfService
{
    private static IBrowser? _browser;
    private readonly IConfiguration _config;

    public PdfService(IConfiguration config)
    {
        _config = config;

    }
    //public async Task InitAsync()
    //{
    // if (_browser != null) return;
    //    // Downloads Chromium to default Puppeteer location
    //    var browserFetcher = new BrowserFetcher();
    //    await browserFetcher.DownloadAsync();

    //    _browser = await Puppeteer.LaunchAsync(new LaunchOptions
    //    {
    //        Headless = true
    //    });
    //}
    //starting point
    public async Task InitAsync()
    {

        if (_browser != null) return;
        var fetcher = new BrowserFetcher(new BrowserFetcherOptions
        {
            Path = @"C:\Users\Sagar Gupta\Desktop\learning\taiPoc\downlaods"
        });


        var revision = await fetcher.DownloadAsync();

        _browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            ExecutablePath = fetcher.GetExecutablePath(revision.BuildId) 
        });
    }

    public async Task<PdfResponse> GeneratePdfAsync(string htmlContent)
    {
        try
        {
          
            if (_browser == null)
            {
                await InitAsync();
            }

          // validation
            if (string.IsNullOrWhiteSpace(htmlContent))
            {
                return new PdfResponse
                {
                    IsSuccess = false,
                    Message = "HTML content is empty"
                };
            }

            htmlContent = EnsureHtmlStructure(htmlContent);
            htmlContent = FixHtml(htmlContent);
            htmlContent = await ConvertImagesToBase64Async(htmlContent);


            //generating pdf from request
            var page = await _browser.NewPageAsync();

            await page.SetContentAsync(htmlContent, new NavigationOptions
            {
                WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded },
                Timeout = 5000
            });
         //   await page.WaitForSelectorAsync("img");

            var pdfBytes = await page.PdfDataAsync(new PdfOptions
            {
                Format = PuppeteerSharp.Media.PaperFormat.A4,
                PrintBackground = true,
                PreferCSSPageSize = true
            });

            await page.CloseAsync();

            pdfBytes=CompressPdf(pdfBytes);
            pdfBytes = EncryptPdf(pdfBytes, "SART", "1223");

            return new PdfResponse
            {
                IsSuccess = true,
                Message = "PDF generated successfully",
                FileName = $"report_{DateTime.Now.Ticks}.pdf",
                FileBytes = pdfBytes
            };
        }
        catch (Exception ex)
        {
          
            return new PdfResponse
            {
                IsSuccess = false,
                Message = $"Error generating PDF: {ex.Message}"
            };
        }
    }
   //template building functions
    public string BuildFinalHtml(string htmlContent, string stylesheet)
    {
        return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
<meta charset='UTF-8'>
<meta http-equiv='X-UA-Compatible' content='IE=edge'>
<meta name='viewport' content='width=device-width, initial-scale=1.0'>
<title>PDF Document</title>

<style>
* {{
    font-family: 'Inter', sans-serif;
    box-sizing: border-box;
}}

body {{
    margin: 0;
    padding: 20px;
    line-height: 1.6;
    color: #333;
}}

html {{
    -webkit-print-color-adjust: exact;
    print-color-adjust: exact;
}}

table {{
    border-collapse: collapse;
    width: 100%;
}}

table, tr, td, div {{
    page-break-inside: avoid !important;
    break-inside: avoid !important;
}}

.noTableBreak {{
    page-break-inside: avoid;
}}

.page-break {{
    page-break-before: always;
}}

.page-footer {{
    text-align: center;
    padding: 10px;
}}

/* PRINT SETTINGS */
@media print {{
    @page {{
        size: A4;
        margin: 10mm;
    }}
}}


{stylesheet}

</style>
</head>

<body>
{htmlContent}
</body>
</html>";
    }
    //public async Task<string> ConvertImagesToBase64Async(string html)
    //{
    //    var doc = new HtmlDocument();
    //    doc.LoadHtml(html);

    //    var imgNodes = doc.DocumentNode.SelectNodes("//img");

    //    if (imgNodes == null) return html;

    //    using var httpClient = new HttpClient();

    //    foreach (var img in imgNodes)
    //    {
    //        var src = img.GetAttributeValue("src", "");

    //        if (string.IsNullOrEmpty(src)) continue;

    //        try
    //        {
    //            // Skip if already base64
    //            if (src.StartsWith("data:")) continue;

    //            // Download image
    //            var imageBytes = await httpClient.GetByteArrayAsync(src);
    //            imageBytes = await CompressImage(imageBytes);

    //            // Detect type
    //            var extension = Path.GetExtension(src).ToLower();

    //            var mimeType = extension switch
    //            {
    //                ".png" => "image/png",
    //                ".jpg" or ".jpeg" => "image/jpeg",
    //                ".bmp" => "image/bmp",
    //                ".gif" => "image/gif",
    //                _ => "image/png"
    //            };

    //            var base64 = Convert.ToBase64String(imageBytes);

    //            var newSrc = $"data:{mimeType};base64,{base64}";

    //            img.SetAttributeValue("src", newSrc);
    //        }
    //        catch
    //        {
    //            // if image fails → skip
    //            continue;
    //        }
    //    }

    //    return doc.DocumentNode.OuterHtml;
    //}

    public async Task<string> ConvertImagesToBase64Async(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var imgNodes = doc.DocumentNode.SelectNodes("//img");
        if (imgNodes == null) return html;

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3) // prevent slow requests
        };

        var semaphore = new SemaphoreSlim(5); // limit parallel tasks

        var results = new List<(HtmlNode node, string newSrc)>();

        var tasks = imgNodes.Select(async img =>
        {
            var src = img.GetAttributeValue("src", "");

            if (string.IsNullOrEmpty(src) || src.StartsWith("data:"))
                return;

            await semaphore.WaitAsync();

            try
            {
                var imageBytes = await httpClient.GetByteArrayAsync(src);

                //  compress image
                imageBytes = await CompressImage(imageBytes);

                var extension = Path.GetExtension(src).ToLower();

                var mimeType = extension switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".bmp" => "image/bmp",
                    ".gif" => "image/gif",
                    _ => "image/jpeg"
                };

                var base64 = Convert.ToBase64String(imageBytes);
                var newSrc = $"data:{mimeType};base64,{base64}";

                lock (results) //  thread-safe collection
                {
                    results.Add((img, newSrc));
                }
            }
            catch
            {
                // skip failed images
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        // Apply updates sequentially (SAFE)
        foreach (var (node, newSrc) in results)
        {
            node.SetAttributeValue("src", newSrc);
        }

        return doc.DocumentNode.OuterHtml;
    }

    //functions for structuring html content
    public string FixHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.OptionFixNestedTags = true;   
        doc.LoadHtml(html);

        return doc.DocumentNode.OuterHtml;
    }
    public string EnsureHtmlStructure(string html)
    {
        if (!html.Contains("<html"))
        {
            html = $"<html><body>{html}</body></html>";
        }
        return html;
    }
    //reduce image size
    public async Task<byte[]> CompressImage(byte[] imageBytes)
    {
        using var image = Image.Load(imageBytes);

        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(800, 800) 
        }));

        using var ms = new MemoryStream();

        image.Save(ms, new JpegEncoder
        {
            Quality = 60
        });

        return ms.ToArray();
    }
    //compresing pdf
    public byte[] CompressPdf(byte[] pdfBytes)
    {
        using var input = new MemoryStream(pdfBytes);
        using var output = new MemoryStream();

        var reader = new PdfReader(input);
        var writer = new PdfWriter(output, new WriterProperties()
            .SetFullCompressionMode(true));

        var pdfDoc = new PdfDocument(reader, writer);
        pdfDoc.Close();

        return output.ToArray();
    }

    //encrypt pdf
    public byte[] EncryptPdf(byte[] pdfBytes, string userPassword, string ownerPassword)
    {
    using var input = new MemoryStream(pdfBytes);
    using var output = new MemoryStream();

    var reader = new PdfReader(input);

    var writer = new PdfWriter(output, new WriterProperties()
        .SetStandardEncryption(
            System.Text.Encoding.UTF8.GetBytes(userPassword),
            System.Text.Encoding.UTF8.GetBytes(ownerPassword),
            EncryptionConstants.ALLOW_PRINTING,
            EncryptionConstants.ENCRYPTION_AES_256
        ));

    var pdfDoc = new PdfDocument(reader, writer);
    pdfDoc.Close();

      return output.ToArray();
    }

    //zip pdf
    public byte[] ZipPdf(byte[] pdfBytes, string fileName)
    {
        using var ms = new MemoryStream();

        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry(fileName);

            using var entryStream = entry.Open();
            entryStream.Write(pdfBytes, 0, pdfBytes.Length);
        }

        return ms.ToArray();
    }
    //SEDNING MAIL ...  
    public async Task SendEmail(string to, byte[] pdf, string user)
    {
        if (string.IsNullOrWhiteSpace(to))
            throw new ArgumentException("Recipient email is required.");

        if (pdf == null || pdf.Length == 0)
            throw new ArgumentException("PDF attachment is empty.");

        if (pdf.Length > 25 * 1024 * 1024)
            throw new Exception("Attachment exceeds 25MB limit.");

        var email = new MimeMessage();
        email.From.Add(MailboxAddress.Parse(_config["Email:SmtpUser"]));
        email.To.Add(MailboxAddress.Parse(to));
        email.Subject = "Your TAI Service Report (Password Protected)";

        var path = Path.Combine(Directory.GetCurrentDirectory(), "Template", "Email.html");
        var htmlTemplate = await File.ReadAllTextAsync(path);
      //  var htmlTemplate = File.ReadAllText("Template/Email.html");


        htmlTemplate = htmlTemplate
            .Replace("{{UserName}}", user)
            .Replace("{{PdfPasswordHint}}", $"Password is first 4 letters of {user.ToUpper()}")
            .Replace("{{SupportLink}}", "https://company.com/support")
            .Replace("{{Year}}", DateTime.Now.Year.ToString());

        var body = new BodyBuilder
        {
            HtmlBody = htmlTemplate
        };

        var fileName = $"report_{DateTime.Now.Ticks}.pdf";

        body.Attachments.Add(
            fileName,
            pdf,
            new ContentType("application", "pdf")
        );

        email.Body = body.ToMessageBody();

        using var smtp = new SmtpClient();

        await smtp.ConnectAsync(
            _config["Email:SmtpHost"],
            int.Parse(_config["Email:SmtpPort"]),
            SecureSocketOptions.StartTls
        );

        await smtp.AuthenticateAsync(
            _config["Email:SmtpUser"],
            _config["Email:SmtpPassword"]
        );

        await smtp.SendAsync(email);
        await smtp.DisconnectAsync(true);
    }
}