namespace HealthyGuidance.Core.Storage;

public static class StorageRoot
{
    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HealthyGuidance");

    public static string RecordsDir { get; } = Path.Combine(DataRoot, "records");
    public static string FailedDir { get; } = Path.Combine(DataRoot, "failed");
    public static string NotesDir { get; } = Path.Combine(DataRoot, "notes");
    public static string ReportsDir { get; } = Path.Combine(DataRoot, "reports");
    public static string ConfigDir { get; } = Path.Combine(DataRoot, "config");

    public static string MonthKey(DateTime localTime) => localTime.ToString("yyyy-MM");

    public static string RecordsMonthDir(DateTime localTime) =>
        Path.Combine(RecordsDir, MonthKey(localTime));

    public static string FailedMonthDir(DateTime localTime) =>
        Path.Combine(FailedDir, MonthKey(localTime));
}
