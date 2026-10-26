using System.Text;
using System.Text.RegularExpressions;

namespace PsySite.Logging;

/// <summary>Одна запись лога (строка + описание ошибки, если оно было).</summary>
public record LogEntry(DateTime Time, string Level, string Source, string Message, string? Details)
{
    public bool IsError => Level is "ERR" or "FTL";
    public bool IsWarning => Level == "WRN";
}

public record LogFileInfo(string Name, long Size, DateTime Modified);

/// <summary>Читает файлы логов для страницы «Журнал» в админке.</summary>
public partial class LogReader(string directory)
{
    /// <summary>Сколько последних мегабайт файла читать (на случай очень больших файлов).</summary>
    private const int MaxReadBytes = 8 * 1024 * 1024;

    public string Directory { get; } = directory;

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(?:\.\d+)?) \[(\w{3})\] ([^:]+): (.*)$")]
    private static partial Regex EntryStart();

    public List<LogFileInfo> ListFiles()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        return new DirectoryInfo(Directory).GetFiles("*.log")
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f => new LogFileInfo(f.Name, f.Length, f.LastWriteTime))
            .ToList();
    }

    /// <summary>
    /// Полный путь к файлу — только если такой файл действительно есть в папке логов.
    /// Так через адресную строку нельзя открыть другие файлы сервера.
    /// </summary>
    public string? Resolve(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return ListFiles().Any(f => f.Name == name) ? Path.Combine(Directory, name) : null;
    }

    public List<LogEntry> Read(string path)
    {
        var text = ReadTail(path);
        var entries = new List<LogEntry>();
        DateTime time = default; string? level = null, src = null, msg = null;
        var details = new StringBuilder();

        void Flush()
        {
            if (level is null) return;
            entries.Add(new LogEntry(time, level, src!, msg!, details.Length > 0 ? details.ToString().TrimEnd() : null));
            details.Clear();
        }

        foreach (var line in text.Split('\n'))
        {
            var l = line.TrimEnd('\r');
            var m = EntryStart().Match(l);
            if (m.Success)
            {
                Flush();
                DateTime.TryParse(m.Groups[1].Value, out time);
                level = m.Groups[2].Value; src = m.Groups[3].Value; msg = m.Groups[4].Value;
            }
            else if (level is not null && l.Length > 0)
            {
                details.AppendLine(l); // продолжение: текст исключения, стек вызовов
            }
        }
        Flush();
        return entries;
    }

    /// <summary>Сколько ошибок записано за сегодня (для сводки в админке).</summary>
    public int CountTodayErrors()
    {
        var path = Path.Combine(Directory, $"errors-{DateTime.Now:yyyyMMdd}.log");
        if (!File.Exists(path)) return 0;
        try { return Read(path).Count(e => e.IsError); }
        catch (IOException) { return 0; }
    }

    private static string ReadTail(string path)
    {
        // FileShare.ReadWrite — файл в этот момент может дописываться самим сайтом.
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length > MaxReadBytes) fs.Seek(-MaxReadBytes, SeekOrigin.End);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
