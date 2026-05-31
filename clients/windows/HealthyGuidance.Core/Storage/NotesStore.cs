using System.Globalization;
using System.Text;

namespace HealthyGuidance.Core.Storage;

public sealed record DietNote(DateTime Timestamp, string Text);

public static class NotesStore
{
    private const string TimestampFormat = "yyyy-MM-dd HH:mm";

    public static DietNote Append(string text, DateTime localTime)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Note text cannot be empty.", nameof(text));

        var trimmed = text.Trim();
        var truncated = new DateTime(
            localTime.Year, localTime.Month, localTime.Day,
            localTime.Hour, localTime.Minute, 0);

        Directory.CreateDirectory(StorageRoot.NotesDir);
        var path = MonthFilePath(truncated);

        var sb = new StringBuilder();
        if (File.Exists(path) && new FileInfo(path).Length > 0)
            sb.Append('\n');
        sb.Append(truncated.ToString(TimestampFormat, CultureInfo.InvariantCulture));
        sb.Append('\n');
        sb.Append(trimmed);
        sb.Append('\n');
        File.AppendAllText(path, sb.ToString(), Encoding.UTF8);

        return new DietNote(truncated, trimmed);
    }

    public static IReadOnlyList<DietNote> ReadWindow(DateTime start, DateTime end)
    {
        var results = new List<DietNote>();
        foreach (var month in EnumerateMonths(start, end))
        {
            var path = Path.Combine(StorageRoot.NotesDir, month + ".txt");
            if (!File.Exists(path)) continue;
            results.AddRange(ParseMonthFile(path));
        }
        return results
            .Where(n => n.Timestamp >= start && n.Timestamp <= end)
            .OrderBy(n => n.Timestamp)
            .ToList();
    }

    public static IReadOnlyList<DietNote> ReadMonth(string yearMonth)
    {
        var path = Path.Combine(StorageRoot.NotesDir, yearMonth + ".txt");
        if (!File.Exists(path)) return Array.Empty<DietNote>();
        return ParseMonthFile(path).OrderBy(n => n.Timestamp).ToList();
    }

    private static string MonthFilePath(DateTime localTime) =>
        Path.Combine(StorageRoot.NotesDir, StorageRoot.MonthKey(localTime) + ".txt");

    private static IEnumerable<DietNote> ParseMonthFile(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var i = 0;
        while (i < lines.Length)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) { i++; continue; }

            if (!DateTime.TryParseExact(
                    lines[i].Trim(), TimestampFormat,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts))
            {
                i++;
                continue;
            }

            i++;
            var bodyLines = new List<string>();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i])
                   && !LooksLikeTimestamp(lines[i]))
            {
                bodyLines.Add(lines[i]);
                i++;
            }

            if (bodyLines.Count > 0)
                yield return new DietNote(ts, string.Join('\n', bodyLines).Trim());
        }
    }

    private static bool LooksLikeTimestamp(string line) =>
        DateTime.TryParseExact(
            line.Trim(), TimestampFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static IEnumerable<string> EnumerateMonths(DateTime start, DateTime end)
    {
        var cursor = new DateTime(start.Year, start.Month, 1);
        var stop = new DateTime(end.Year, end.Month, 1);
        while (cursor <= stop)
        {
            yield return StorageRoot.MonthKey(cursor);
            cursor = cursor.AddMonths(1);
        }
    }
}
