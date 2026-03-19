
public interface IPdfService
{
    Task InitAsync();
    Task<PdfResponse> GeneratePdfAsync(string htmlContent);
    string BuildFinalHtml(string htmlContent, string stylesheet);
    Task<string> ConvertImagesToBase64Async(string html);
    Task<byte[]> CompressImage(byte[] imageBytes);
    byte[] CompressPdf(byte[] pdfBytes);
    byte[] EncryptPdf(byte[] pdfBytes, string userPassword, string ownerPassword);
    byte[] ZipPdf(byte[] pdfBytes, string fileName);
    Task SendEmail(string to, byte[] pdf, string user);
}