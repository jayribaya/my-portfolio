namespace Portfolio.Application;
public static class UploadRules
{
    public const int MaxBytes = 8 * 1024 * 1024;
    public static string Validate(byte[] data, string mime)
    {
        if (data.Length < 12 || data.Length > MaxBytes) throw new InputException("Upload a file between 12 bytes and 8 MB.");
        if (mime == "image/png" && data.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) return ".png";
        if (mime == "image/jpeg" && data[0] == 255 && data[1] == 216 && data[2] == 255) return ".jpg";
        if (mime == "image/webp" && System.Text.Encoding.ASCII.GetString(data, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(data, 8, 4) == "WEBP") return ".webp";
        if (mime == "application/pdf" && System.Text.Encoding.ASCII.GetString(data, 0, 5) == "%PDF-") return ".pdf";
        throw new InputException("Only signature-verified PNG, JPEG, WebP and PDF files are supported. SVG and executable content are prohibited.");
    }
}
