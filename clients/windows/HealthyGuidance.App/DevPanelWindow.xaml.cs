using HealthyGuidance.Core.AzureOpenAI;
using HealthyGuidance.Core.Prompts;
using HealthyGuidance.Core.Schemas;
using HealthyGuidance.Core.Settings;
using HealthyGuidance.Core.Storage;
using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace HealthyGuidance.App;

public sealed partial class MainWindow : Window
{
    private const string ApiVersion = "v1";

    private string _cachedSystemPrompt = string.Empty;
    private SettingsWindow? _settingsWindow;

    public MainWindow()
    {
        InitializeComponent();
        LoadPromptIntoUi();

        var now = DateTime.Now;
        BrowseMonthBox.Text = now.ToString("yyyy-MM");
        NotesMonthBox.Text = now.ToString("yyyy-MM");
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Saved += (_, _) => GlobalStatusText.Text = "设置已保存";
        _settingsWindow.Activate();
    }

    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(StorageRoot.DataRoot);
        Process.Start(new ProcessStartInfo
        {
            FileName = StorageRoot.DataRoot,
            UseShellExecute = true
        });
    }

    private void ReloadPromptButton_Click(object sender, RoutedEventArgs e)
    {
        LoadPromptIntoUi();
        GlobalStatusText.Text = "Prompt 已重新加载";
    }

    private void LoadPromptIntoUi()
    {
        try
        {
            var sharedRoot = Path.Combine(AppContext.BaseDirectory, "shared");
            _cachedSystemPrompt = PromptLoader.Load(sharedRoot, "parse.md");
            PromptBox.Text = _cachedSystemPrompt;
            PromptHeader.Text = $"System Prompt（{_cachedSystemPrompt.Length} 字符，点击展开）";
        }
        catch (Exception ex)
        {
            PromptBox.Text = "加载 prompt 失败：" + ex.Message;
            PromptHeader.Text = "System Prompt（加载失败）";
        }
    }

    // ---------- ① Parse only ----------
    private async void ParseOnlyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ParseOnlyButton.IsEnabled = false;
            var (settings, picked) = await PickFileWithSettingsAsync(ParseOnlyStatus);
            if (picked is null) return;

            ParseOnlyStatus.Text = $"调用 {settings.DeploymentName} 解析中...";
            var json = await CallParseAsync(settings, picked.Bytes, picked.Mime);
            ParseOnlyResultBox.Text = json;
            ParseOnlyStatus.Text = "完成";
        }
        catch (Exception ex)
        {
            ParseOnlyResultBox.Text = ex.ToString();
            ParseOnlyStatus.Text = "出错：" + ex.Message;
        }
        finally
        {
            ParseOnlyButton.IsEnabled = true;
        }
    }

    // ---------- ② Parse + persist ----------
    private async void ParseAndSaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ParseAndSaveButton.IsEnabled = false;
            var (settings, picked) = await PickFileWithSettingsAsync(ParseAndSaveStatus);
            if (picked is null) return;

            var importTime = DateTime.Now;

            var sha = RecordStore.ComputeSha256(picked.Bytes);
            var existing = RecordStore.TryFindExistingBySha(sha);
            if (existing is not null)
            {
                ParseAndSaveStatus.Text = "命中已有记录，跳过 GPT 调用";
                ParseAndSaveResultBox.Text =
                    $"DuplicateSkipped（未调用 GPT）\n" +
                    $"id: {existing.Id}\n" +
                    $"path: {existing.DirectoryPath}\n" +
                    $"sha256: {sha}";
                return;
            }

            ParseAndSaveStatus.Text = $"调用 {settings.DeploymentName} 解析中...";

            string json;
            try
            {
                json = await CallParseAsync(settings, picked.Bytes, picked.Mime);
            }
            catch (Exception apiEx)
            {
                var attempt = new FailureAttempt
                {
                    AttemptedAt = DateTime.Now,
                    Model = settings.DeploymentName,
                    ErrorType = ErrorType.ApiError,
                    ErrorMessage = apiEx.Message
                };
                var failResult = RecordStore.SaveFailure(picked.Bytes, picked.Extension, attempt, importTime);
                ParseAndSaveStatus.Text = $"API 失败，已落 failed/：{failResult.Outcome}";
                ParseAndSaveResultBox.Text =
                    $"id: {failResult.Record.Id}\n" +
                    $"path: {failResult.Record.DirectoryPath}\n" +
                    $"error: {apiEx.Message}";
                return;
            }

            var root = JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidOperationException("响应不是 JSON 对象");
            var kindStr = root["kind"]!.GetValue<string>();

            if (kindStr == "unknown")
            {
                var attempt = new FailureAttempt
                {
                    AttemptedAt = DateTime.Now,
                    Model = settings.DeploymentName,
                    ErrorType = ErrorType.KindUnknown,
                    ErrorMessage = root["error_reason"]?.GetValue<string>() ?? "kind=unknown"
                };
                var failResult = RecordStore.SaveFailure(picked.Bytes, picked.Extension, attempt, importTime);
                ParseAndSaveStatus.Text = $"kind=unknown，已落 failed/：{failResult.Outcome}";
                ParseAndSaveResultBox.Text =
                    $"id: {failResult.Record.Id}\n" +
                    $"path: {failResult.Record.DirectoryPath}\n" +
                    $"reason: {attempt.ErrorMessage}";
                return;
            }

            var kind = RecordKindExtensions.FromSlug(kindStr);
            var dataKey = kind == RecordKind.Workout ? "workout" : "body_metrics";
            var data = (JsonObject)root[dataKey]!.DeepClone();
            var eventTime = ExtractEventTime(kind, data);
            var (tsSource, missingFields, confidence) = AnalyzeData(kind, data);

            var parseMeta = new ParseMeta
            {
                Model = settings.DeploymentName,
                ApiVersion = ApiVersion,
                ParsedAt = DateTime.Now,
                TimestampSource = eventTime is null ? TimestampSource.Import : TimestampSource.Extracted,
                MissingFields = missingFields,
                Confidence = confidence
            };

            var result = RecordStore.SaveSuccess(
                picked.Bytes, picked.Extension, kind, data, parseMeta, eventTime, importTime);

            ParseAndSaveStatus.Text = $"完成：{result.Outcome}";
            ParseAndSaveResultBox.Text =
                $"id: {result.Record.Id}\n" +
                $"path: {result.Record.DirectoryPath}\n" +
                $"confidence: {result.Record.Parse.Confidence}\n" +
                $"missing: [{string.Join(", ", result.Record.Parse.MissingFields)}]\n\n" +
                $"--- data ---\n{JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true })}";
        }
        catch (Exception ex)
        {
            ParseAndSaveResultBox.Text = ex.ToString();
            ParseAndSaveStatus.Text = "出错：" + ex.Message;
        }
        finally
        {
            ParseAndSaveButton.IsEnabled = true;
        }
    }

    // ---------- ③ Note append ----------
    private void AppendNoteButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = NoteInputBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                AppendNoteStatus.Text = "请输入内容";
                return;
            }

            var note = NotesStore.Append(text, DateTime.Now);
            var path = Path.Combine(StorageRoot.NotesDir, StorageRoot.MonthKey(note.Timestamp) + ".txt");
            AppendNoteStatus.Text = $"已写入 {path}（{note.Timestamp:yyyy-MM-dd HH:mm}）";
            NoteInputBox.Text = string.Empty;
        }
        catch (Exception ex)
        {
            AppendNoteStatus.Text = "出错：" + ex.Message;
        }
    }

    // ---------- ④ Browse records ----------
    private void ListRecordsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var month = BrowseMonthBox.Text.Trim();
            var records = RecordStore.ListByMonth(month).ToList();
            if (records.Count == 0)
            {
                BrowseRecordsBox.Text = $"{month} 无成功记录";
                return;
            }
            var sb = new StringBuilder();
            sb.AppendLine($"{month} 共 {records.Count} 条成功记录：\n");
            foreach (var r in records.OrderBy(r => r.Id))
            {
                sb.AppendLine($"  {r.Id}");
                sb.AppendLine($"    kind={r.Kind.ToSlug()}  confidence={r.Parse.Confidence}");
                sb.AppendLine($"    {r.DirectoryPath}");
                sb.AppendLine();
            }
            BrowseRecordsBox.Text = sb.ToString();
        }
        catch (Exception ex)
        {
            BrowseRecordsBox.Text = "出错：" + ex.Message;
        }
    }

    private void ListFailedButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var month = BrowseMonthBox.Text.Trim();
            var records = RecordStore.ListFailedByMonth(month).ToList();
            if (records.Count == 0)
            {
                BrowseRecordsBox.Text = $"{month} 无失败记录";
                return;
            }
            var sb = new StringBuilder();
            sb.AppendLine($"{month} 共 {records.Count} 条失败记录：\n");
            foreach (var r in records.OrderBy(r => r.Id))
            {
                sb.AppendLine($"  {r.Id}");
                sb.AppendLine($"    attempts={r.Attempts.Count}  last={r.Attempts.LastOrDefault()?.ErrorType}");
                sb.AppendLine($"    {r.DirectoryPath}");
                sb.AppendLine();
            }
            BrowseRecordsBox.Text = sb.ToString();
        }
        catch (Exception ex)
        {
            BrowseRecordsBox.Text = "出错：" + ex.Message;
        }
    }

    // ---------- ⑤ Browse notes ----------
    private void ListNotesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var month = NotesMonthBox.Text.Trim();
            var notes = NotesStore.ReadMonth(month);
            if (notes.Count == 0)
            {
                BrowseNotesBox.Text = $"{month} 无备注";
                return;
            }
            var sb = new StringBuilder();
            sb.AppendLine($"{month} 共 {notes.Count} 条备注：\n");
            foreach (var n in notes)
            {
                sb.AppendLine($"[{n.Timestamp:yyyy-MM-dd HH:mm}]");
                sb.AppendLine(n.Text);
                sb.AppendLine();
            }
            BrowseNotesBox.Text = sb.ToString();
        }
        catch (Exception ex)
        {
            BrowseNotesBox.Text = "出错：" + ex.Message;
        }
    }

    // ---------- helpers ----------
    private sealed record PickedFile(byte[] Bytes, string Mime, string Extension, string Name);

    private async Task<(AppSettings settings, PickedFile? file)> PickFileWithSettingsAsync(Microsoft.UI.Xaml.Controls.TextBlock statusBlock)
    {
        var settings = SettingsStore.Load();
        if (!settings.IsConfigured)
        {
            statusBlock.Text = "请先在 ⚙ 设置 中配置 Endpoint / Deployment / API Key";
            return (settings, null);
        }

        statusBlock.Text = "选择文件...";
        var picker = new FileOpenPicker();
        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");

        var file = await picker.PickSingleFileAsync();
        if (file == null)
        {
            statusBlock.Text = "已取消";
            return (settings, null);
        }

        statusBlock.Text = $"读取 {file.Name}...";
        var buffer = await FileIO.ReadBufferAsync(file);
        var bytes = buffer.ToArray();
        var ext = file.FileType.ToLowerInvariant();
        var mime = ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "image/png"
        };
        return (settings, new PickedFile(bytes, mime, ext, file.Name));
    }

    private async Task<string> CallParseAsync(AppSettings settings, byte[] imageBytes, string mime)
    {
        var sharedRoot = Path.Combine(AppContext.BaseDirectory, "shared");
        _cachedSystemPrompt = PromptLoader.Load(sharedRoot, "parse.md");
        PromptBox.Text = _cachedSystemPrompt;
        PromptHeader.Text = $"System Prompt（{_cachedSystemPrompt.Length} 字符，点击展开）";
        var schema = SchemaLoader.LoadInlined(sharedRoot, "parse-result.json");

        var client = new GptVisionClient(settings.Endpoint, settings.ApiKey, settings.DeploymentName);
        return await client.ParseScreenshotAsync(imageBytes, mime, _cachedSystemPrompt, schema);
    }

    private static DateTime? ExtractEventTime(RecordKind kind, JsonObject data)
    {
        var key = kind == RecordKind.Workout ? "date_time" : "measured_at";
        var node = data[key];
        if (node is null || node.GetValueKind() == JsonValueKind.Null) return null;
        var s = node.GetValue<string>();
        return DateTime.TryParse(s, out var dt) ? dt : null;
    }

    private static readonly string[] WorkoutRequired =
        { "date_time", "sport_type", "duration_text", "calories_text" };
    private static readonly string[] BodyMetricsRequired = { "measured_at", "weight_kg" };

    private static (TimestampSource ts, List<string> missing, Confidence confidence) AnalyzeData(
        RecordKind kind, JsonObject data)
    {
        var required = kind == RecordKind.Workout ? WorkoutRequired : BodyMetricsRequired;
        var missing = required
            .Where(k => data[k] is null || data[k]!.GetValueKind() == JsonValueKind.Null)
            .ToList();
        var confidence = missing.Count switch
        {
            0 => Confidence.High,
            1 => Confidence.Medium,
            _ => Confidence.Low
        };
        return (TimestampSource.Extracted, missing, confidence);
    }
}
