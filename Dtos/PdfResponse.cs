using System;

public class PdfResponse
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public byte[]? FileBytes { get; set; }
}

public class PdfRequest
{
    public string Html { get; set; }
    public string Css { get; set; }
}