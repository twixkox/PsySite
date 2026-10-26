namespace PsySite.Services;

/// <summary>
/// Сохраняет загруженные в админке картинки в wwwroot/uploads.
/// Проверяется не только расширение, но и содержимое файла: страницу со скриптом,
/// переименованную в .jpg, загрузить не получится. Имя файла всегда случайное.
/// </summary>
public class ImageStorage(IWebHostEnvironment env, ILogger<ImageStorage> log)
{
    private const long MaxBytes = 10 * 1024 * 1024;

    public async Task<(string? Path, string? Error)> SaveAsync(IFormFile file)
    {
        if (file.Length > MaxBytes) return (null, "Файл больше 10 МБ.");

        // Читаем начало файла и определяем настоящий формат по «подписи» (первым байтам).
        var head = new byte[12];
        await using (var s = file.OpenReadStream())
        {
            var read = 0;
            while (read < head.Length)
            {
                var n = await s.ReadAsync(head.AsMemory(read));
                if (n == 0) break;
                read += n;
            }
        }

        var ext = DetectFormat(head);
        if (ext is null)
        {
            log.LogWarning("Отклонён файл «{FileName}»: это не картинка JPG, PNG, WEBP или GIF", file.FileName);
            return (null, "Можно загружать только картинки JPG, PNG, WEBP или GIF.");
        }

        var dir = Path.Combine(env.WebRootPath, "uploads");
        Directory.CreateDirectory(dir);
        var name = $"{Guid.NewGuid():N}{ext}"; // расширение — по настоящему формату, а не по имени файла
        await using var stream = File.Create(Path.Combine(dir, name));
        await file.CopyToAsync(stream);
        return ($"/uploads/{name}", null);
    }

    private static string? DetectFormat(byte[] b)
    {
        if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
        if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ".png";
        if (b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38) return ".gif";
        if (b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 &&
            b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return ".webp";
        return null;
    }
}
