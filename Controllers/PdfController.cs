using Microsoft.AspNetCore.Mvc;
[ApiController]
[Route("api/pdf")]
public class PdfController : ControllerBase
{
    private readonly IPdfService _pdfService;

    public PdfController(IPdfService pdfService)
    {
        _pdfService = pdfService;
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate(IFormFile htmlFile, IFormFile cssFile)
    {
        using var reader1 = new StreamReader(htmlFile.OpenReadStream());
        using var reader2 = new StreamReader(cssFile.OpenReadStream());

        var html = await reader1.ReadToEndAsync();
        var css = await reader2.ReadToEndAsync();

        var finalHtml = _pdfService.BuildFinalHtml(html, css);
        var result = await _pdfService.GeneratePdfAsync(finalHtml);

        if (!result.IsSuccess || result.FileBytes == null)
        {
            return BadRequest(result);
        }

        // Create folder
        var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "GeneratedPdfs");

        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        // File name
        var fileName = result.FileName ?? $"file_{DateTime.Now.Ticks}.pdf";
        var filePath = Path.Combine(folderPath, fileName);

        // Save PDF
        await System.IO.File.WriteAllBytesAsync(filePath, result.FileBytes);
        await _pdfService.SendEmail("sggupta8742@gmail.com", result.FileBytes, "Sarthak");
        var zipBytes = _pdfService.ZipPdf(result.FileBytes, fileName);

        var zipFileName = Path.ChangeExtension(fileName, ".zip");

        return File(zipBytes, "application/zip", zipFileName);
    }
}

