using System.Windows.Media.Imaging;
using MiniCodex.Models;

namespace MiniCodex.Services;

public static class AttachmentStore
{
    public const long MaxImageBytes = 25 * 1024 * 1024;
    private static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp";
    public static ChatAttachment FromFile(string path)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) throw new IOException("Attach files, or use the file explorer to mention a folder.");
        var image = IsImage(path);
        if (image && new FileInfo(path).Length > MaxImageBytes) throw new IOException("Images must be 25 MB or smaller.");
        return new(path, image);
    }
    public static void Validate(IReadOnlyList<ChatAttachment> attachments)
    {
        foreach (var attachment in attachments) _ = FromFile(attachment.Path);
    }
    public static ChatAttachment SaveClipboardImage(BitmapSource image)
    {
        if ((long)image.PixelWidth * image.PixelHeight > 40_000_000) throw new IOException("This image is too large. Attach a smaller image.");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var buffer = new MemoryStream();
        encoder.Save(buffer);
        if (buffer.Length > MaxImageBytes) throw new IOException("Images must be 25 MB or smaller.");
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniCodex", "attachments");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"clipboard-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, buffer.ToArray());
        return new(path, true);
    }
}
