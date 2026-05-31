using HealthyGuidance.Core.AzureOpenAI;
using HealthyGuidance.Core.Prompts;
using HealthyGuidance.Core.Schemas;
using HealthyGuidance.Core.Settings;
using HealthyGuidance.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace HealthyGuidance.App.Pages;

public sealed partial class RecordsPage : Page
{
    private List<SavedRecord> _allSaved = new();
    private List<FailedRecord> _allFailed = new();
    private readonly Dictionary<string, Expander> _expandersById = new();
    private string? _pendingHighlightId;

    public RecordsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string id && !string.IsNullOrEmpty(id) && id != "unconfigured")
        {
            _pendingHighlightId = id;
            // 推迟到 Loaded 之后（首次进入）或当前帧渲染完成后（页面已存在）。
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (RecordsPanel is null) return;
                if (_expandersById.Count == 0) Reload();
                else ApplyPendingHighlight();
            });
            return;
        }
        if (RecordsPanel is not null) Reload();
    }

    public void ReloadFromHost() => Reload();

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (RecordsPanel is null) return;
        RenderList();
    }

    private void Reload()
    {
        if (RecordsPanel is null) return;
        try
        {
            _allSaved = RecordStore.ListAll()
                .OrderByDescending(r => r.SavedAt)
                .ToList();
            _allFailed = RecordStore.ListAllFailed()
                .OrderByDescending(r => r.SavedAt)
                .ToList();
        }
        catch (Exception)
        {
            _allSaved = new();
            _allFailed = new();
        }
        RenderList();
    }

    private string CurrentFilter() =>
        FilterPanel.Children
            .OfType<RadioButton>()
            .FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "all";

    private void RenderList()
    {
        if (RecordsPanel is null) return;
        RecordsPanel.Children.Clear();
        _expandersById.Clear();

        var filter = CurrentFilter();
        var items = new List<(DateTime when, UIElement element)>();

        if (filter is "all" or "workout" or "body")
        {
            foreach (var r in _allSaved)
            {
                if (filter == "workout" && r.Kind != RecordKind.Workout) continue;
                if (filter == "body" && r.Kind != RecordKind.BodyMetrics) continue;
                try
                {
                    items.Add((r.SavedAt, BuildSavedExpander(r)));
                }
                catch (Exception ex)
                {
                    items.Add((r.SavedAt, BuildErrorExpander(r.Id, ex)));
                }
            }
        }

        if (filter is "all" or "failed")
        {
            foreach (var r in _allFailed)
            {
                try
                {
                    items.Add((r.SavedAt, BuildFailedExpander(r)));
                }
                catch (Exception ex)
                {
                    items.Add((r.SavedAt, BuildErrorExpander(r.Id, ex)));
                }
            }
        }

        var ordered = items.OrderByDescending(t => t.when).ToList();
        foreach (var (_, ui) in ordered)
            RecordsPanel.Children.Add(ui);

        SummaryText.Text = ordered.Count == 0
            ? "暂无记录"
            : $"共 {ordered.Count} 条";

        if (ordered.Count == 0)
        {
            RecordsPanel.Children.Add(new TextBlock
            {
                Text = "当前筛选下没有记录。",
                Opacity = 0.6
            });
        }

        ApplyPendingHighlight();
    }

    private void ApplyPendingHighlight()
    {
        if (_pendingHighlightId is null) return;
        if (!_expandersById.TryGetValue(_pendingHighlightId, out var exp))
        {
            // 命中失败：当前筛选可能把它过滤掉了。切回「全部」再试一次。
            if (CurrentFilter() != "all" && FilterAll is not null)
            {
                FilterAll.IsChecked = true; // 触发 Filter_Checked → RenderList → 再走到这里
                return;
            }
            _pendingHighlightId = null;
            return;
        }
        var target = exp;
        var id = _pendingHighlightId;
        _pendingHighlightId = null;

        target.IsExpanded = true;
        // 等 layout 完成后再滚到视图
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            try { target.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true }); }
            catch { }
        });
    }

    private static Expander BuildErrorExpander(string id, Exception ex)
    {
        var header = new TextBlock
        {
            Text = $"{id} · 渲染失败",
            TextWrapping = TextWrapping.Wrap
        };
        if (Application.Current.Resources["SystemFillColorCriticalBrush"] is Brush b)
            header.Foreground = b;

        var body = new TextBox
        {
            Text = ex.ToString(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12
        };

        return new Expander
        {
            Header = header,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = body
        };
    }

    private Expander BuildSavedExpander(SavedRecord r)
    {
        var view = new SavedRecordView(r,
            onChanged: updated =>
            {
                var idx = _allSaved.FindIndex(x => x.Id == updated.Id);
                if (idx >= 0) _allSaved[idx] = updated;
            },
            onDeleted: () => Reload());

        var exp = new Expander
        {
            Header = view.BuildHeader(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = view.Root
        };
        _expandersById[r.Id] = exp;
        return exp;
    }

    private Expander BuildFailedExpander(FailedRecord r)
    {
        var view = new FailedRecordView(r,
            onRetried: () => Reload(),
            onDeleted: () => Reload());

        return new Expander
        {
            Header = view.BuildHeader(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = view.Root
        };
    }

    // ============================================================
    //  SavedRecordView：单条成功记录的可切换视图（view <-> edit）
    // ============================================================
    private sealed class SavedRecordView
    {
        private SavedRecord _record;
        private readonly Action<SavedRecord> _onChanged;
        private readonly Action _onDeleted;

        public StackPanel Root { get; }
        private readonly StackPanel _headerPanel;
        private readonly TextBlock _headerText;

        public SavedRecordView(SavedRecord record, Action<SavedRecord> onChanged, Action onDeleted)
        {
            _record = record;
            _onChanged = onChanged;
            _onDeleted = onDeleted;
            _headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            _headerText = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            _headerPanel.Children.Add(_headerText);

            Root = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
            RenderView();
        }

        public UIElement BuildHeader()
        {
            UpdateHeaderText();
            return _headerPanel;
        }

        private void UpdateHeaderText()
        {
            _headerText.Text = RecordFormatter.FormatCollapsed(_record);
        }

        // ---------- 查看态 ----------
        private void RenderView()
        {
            Root.Children.Clear();

            var actionBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var openImageBtn = new Button { Content = "打开原图" };
            openImageBtn.Click += (_, _) => OpenImageWithDefaultViewer();
            var editBtn = new Button { Content = "编辑" };
            editBtn.Click += (_, _) => RenderEdit();
            var deleteBtn = new Button { Content = "删除" };
            deleteBtn.Click += async (_, _) => await ConfirmAndDeleteAsync();
            actionBar.Children.Add(openImageBtn);
            actionBar.Children.Add(editBtn);
            actionBar.Children.Add(deleteBtn);
            Root.Children.Add(actionBar);

            var groups = RecordFormatter.FormatGroups(_record);
            Root.Children.Add(BuildGroupedBody(groups));
        }

        private void OpenImageWithDefaultViewer()
        {
            var path = Path.Combine(_record.DirectoryPath, _record.ImageFile);
            if (!File.Exists(path)) return;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch { /* 用户无关联应用，静默忽略 */ }
        }

        private async Task ConfirmAndDeleteAsync()
        {
            var dlg = new ContentDialog
            {
                Title = "删除记录？",
                Content = "此操作不可恢复，原图与解析结果都会被删除。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Root.XamlRoot
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                RecordStore.DeleteSaved(_record);
                _onDeleted();
            }
            catch (Exception ex)
            {
                var err = new ContentDialog
                {
                    Title = "删除失败",
                    Content = ex.Message,
                    CloseButtonText = "好",
                    XamlRoot = Root.XamlRoot
                };
                _ = err.ShowAsync();
            }
        }

        // ---------- 编辑态 ----------
        private void RenderEdit()
        {
            Root.Children.Clear();

            // 编辑前对当前 data 做深拷贝快照，作为编辑缓冲
            var snapshot = (JsonObject)_record.Data.DeepClone();
            var schema = RecordSchema.ForKind(_record.Kind);
            var editorState = new EditorState();

            var actionBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var openImageBtn = new Button { Content = "打开原图" };
            openImageBtn.Click += (_, _) => OpenImageWithDefaultViewer();
            var saveBtn = new Button
            {
                Content = "保存",
                Style = (Style)Application.Current.Resources["AccentButtonStyle"]
            };
            var cancelBtn = new Button { Content = "取消" };
            actionBar.Children.Add(openImageBtn);
            actionBar.Children.Add(saveBtn);
            actionBar.Children.Add(cancelBtn);
            Root.Children.Add(actionBar);

            Root.Children.Add(BuildEditorBody(schema, snapshot, editorState));

            cancelBtn.Click += (_, _) => RenderView();

            saveBtn.Click += (_, _) =>
            {
                try
                {
                    editorState.CommitAll(snapshot);
                    var updated = RecordStore.UpdateData(_record, snapshot);
                    _record = updated;
                    _onChanged(updated);
                    UpdateHeaderText();
                    RenderView();
                }
                catch (Exception ex)
                {
                    var dlg = new ContentDialog
                    {
                        Title = "保存失败",
                        Content = ex.Message,
                        CloseButtonText = "好",
                        XamlRoot = Root.XamlRoot
                    };
                    _ = dlg.ShowAsync();
                }
            };
        }

        private static StackPanel BuildEditorBody(
            IReadOnlyList<FieldDef> defs, JsonObject snapshot, EditorState state)
        {
            var root = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };

            var flatCards = new List<FrameworkElement>();

            foreach (var def in defs)
            {
                if (def.ValueType == FieldValueType.NestedObject)
                {
                    FlushFlatCards(root, flatCards);

                    // 嵌套对象用一段子区
                    root.Children.Add(new TextBlock
                    {
                        Text = def.Label,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Opacity = 0.8,
                        Margin = new Thickness(2, 4, 0, 0)
                    });

                    // 确保 snapshot[key] 存在（若为 null 则建一个空对象，方便子字段写入）
                    if (snapshot[def.Key] is not JsonObject nested)
                    {
                        nested = new JsonObject();
                        snapshot[def.Key] = nested;
                    }

                    var grid = new Grid { ColumnSpacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
                    for (var i = 0; i < def.Children!.Count; i++)
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    for (var i = 0; i < def.Children!.Count; i++)
                    {
                        var child = def.Children![i];
                        var editor = BuildLeafEditor(child, nested, state);
                        Grid.SetColumn(editor, i);
                        grid.Children.Add(editor);
                    }
                    root.Children.Add(grid);
                }
                else
                {
                    flatCards.Add(BuildLeafEditor(def, snapshot, state));
                }
            }

            FlushFlatCards(root, flatCards);
            return root;
        }

        private const int EditorColumns = 3;

        private static void FlushFlatCards(StackPanel root, List<FrameworkElement> buffer)
        {
            if (buffer.Count == 0) return;
            var grid = new Grid
            {
                ColumnSpacing = 8,
                RowSpacing = 8,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            for (var c = 0; c < EditorColumns; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var rows = (int)Math.Ceiling(buffer.Count / (double)EditorColumns);
            for (var r = 0; r < rows; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var i = 0; i < buffer.Count; i++)
            {
                var cell = buffer[i];
                Grid.SetRow(cell, i / EditorColumns);
                Grid.SetColumn(cell, i % EditorColumns);
                grid.Children.Add(cell);
            }
            root.Children.Add(grid);
            buffer.Clear();
        }

        private static Border BuildLeafEditor(FieldDef def, JsonObject parent, EditorState state)
        {
            var label = new TextBlock
            {
                Text = string.IsNullOrEmpty(def.Suffix) ? def.Label : $"{def.Label}（{def.Suffix.Trim()}）",
                Opacity = 0.6,
                FontSize = 12
            };

            var node = parent[def.Key];
            FrameworkElement editor;

            if (def.ValueType == FieldValueType.Number)
            {
                double? initial = null;
                if (node is JsonValue jv && jv.GetValueKind() == JsonValueKind.Number
                    && jv.TryGetValue<double>(out var d))
                {
                    initial = d;
                }
                var num = new NumberBox
                {
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
                    Value = initial ?? double.NaN,
                    PlaceholderText = "—",
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                editor = num;
                state.Register(() =>
                {
                    if (double.IsNaN(num.Value))
                        parent[def.Key] = null;
                    else
                        parent[def.Key] = JsonValue.Create(num.Value);
                });
            }
            else
            {
                string initial = node is JsonValue sv && sv.GetValueKind() == JsonValueKind.String
                    ? sv.GetValue<string>()
                    : "";
                var tb = new TextBox
                {
                    Text = initial,
                    PlaceholderText = "—",
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                editor = tb;
                state.Register(() =>
                {
                    if (string.IsNullOrEmpty(tb.Text))
                        parent[def.Key] = null;
                    else
                        parent[def.Key] = JsonValue.Create(tb.Text);
                });
            }

            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(label);
            stack.Children.Add(editor);

            return new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = stack
            };
        }

        // ---------- 共享渲染（查看态用）----------
        private static StackPanel BuildGroupedBody(IReadOnlyList<FieldGroup> groups)
        {
            var root = new StackPanel
            {
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            foreach (var g in groups)
            {
                switch (g.Kind)
                {
                    case FieldGroupKind.Cards:
                        root.Children.Add(BuildCardGrid(g.Fields));
                        break;
                    case FieldGroupKind.SubCards:
                        if (!string.IsNullOrEmpty(g.Title))
                            root.Children.Add(BuildSectionTitle(g.Title!));
                        root.Children.Add(BuildSubCardRow(g.Fields));
                        break;
                    case FieldGroupKind.LongText:
                        root.Children.Add(BuildLongTextCard(g.Fields[0]));
                        break;
                }
            }
            return root;
        }

        private static Grid BuildSubCardRow(IReadOnlyList<DisplayField> fields)
        {
            var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, ColumnSpacing = 8 };
            for (var i = 0; i < fields.Count; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var card = BuildFieldCard(fields[i]);
                Grid.SetColumn(card, i);
                grid.Children.Add(card);
            }
            return grid;
        }

        private static TextBlock BuildSectionTitle(string title) => new()
        {
            Text = title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Opacity = 0.8,
            Margin = new Thickness(2, 4, 0, 0)
        };

        private const int CardColumns = 3;

        private static Grid BuildCardGrid(IReadOnlyList<DisplayField> fields)
        {
            var grid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ColumnSpacing = 8,
                RowSpacing = 8
            };
            for (var c = 0; c < CardColumns; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var rowCount = (int)Math.Ceiling(fields.Count / (double)CardColumns);
            for (var r = 0; r < rowCount; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (var i = 0; i < fields.Count; i++)
            {
                var card = BuildFieldCard(fields[i]);
                Grid.SetRow(card, i / CardColumns);
                Grid.SetColumn(card, i % CardColumns);
                grid.Children.Add(card);
            }
            return grid;
        }

        private static Border BuildFieldCard(DisplayField f)
        {
            var labelBlock = new TextBlock { Text = f.Label, Opacity = 0.6, FontSize = 12 };
            var valueBlock = new TextBlock
            {
                Text = string.IsNullOrEmpty(f.Value) ? "—" : f.Value,
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                Opacity = f.IsMissing ? 0.5 : 1.0
            };
            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(labelBlock);
            stack.Children.Add(valueBlock);
            return new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = stack
            };
        }

        private static Border BuildLongTextCard(DisplayField f)
        {
            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(new TextBlock { Text = f.Label, Opacity = 0.6, FontSize = 12 });
            stack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(f.Value) ? "—" : f.Value,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Opacity = f.IsMissing ? 0.4 : 1.0
            });
            return new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = stack
            };
        }
    }

    private sealed class EditorState
    {
        private readonly List<Action> _commits = new();
        public void Register(Action commit) => _commits.Add(commit);
        public void CommitAll(JsonObject _) { foreach (var c in _commits) c(); }
    }

    // ============================================================
    //  FailedRecordView：失败记录展示 + 重试按钮
    // ============================================================
    private sealed class FailedRecordView
    {
        private readonly FailedRecord _record;
        private readonly Action _onRetried;
        private readonly Action _onDeleted;

        public StackPanel Root { get; }
        private readonly TextBlock _headerText;
        private readonly TextBlock _statusText;
        private readonly Button _retryBtn;
        private readonly Button _openImageBtn;
        private readonly Button _deleteBtn;

        public FailedRecordView(FailedRecord record, Action onRetried, Action onDeleted)
        {
            _record = record;
            _onRetried = onRetried;
            _onDeleted = onDeleted;

            _headerText = new TextBlock { TextWrapping = TextWrapping.Wrap };
            if (Application.Current.Resources["SystemFillColorCriticalBrush"] is Brush b)
                _headerText.Foreground = b;
            _headerText.Text = RecordFormatter.FormatCollapsed(_record);

            _statusText = new TextBlock { Opacity = 0.7, FontSize = 12 };
            _retryBtn = new Button { Content = "重试解析" };
            _openImageBtn = new Button { Content = "打开原图" };
            _deleteBtn = new Button { Content = "删除" };
            _openImageBtn.Click += (_, _) => OpenImageWithDefaultViewer();
            _retryBtn.Click += async (_, _) => await RetryAsync();
            _deleteBtn.Click += async (_, _) => await ConfirmAndDeleteAsync();

            Root = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
            var actionBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            actionBar.Children.Add(_statusText);
            actionBar.Children.Add(_openImageBtn);
            actionBar.Children.Add(_retryBtn);
            actionBar.Children.Add(_deleteBtn);
            Root.Children.Add(actionBar);

            var groups = RecordFormatter.FormatGroups(_record);
            Root.Children.Add(SavedRecordView_BuildGroupedBody(groups));
        }

        public UIElement BuildHeader() => _headerText;

        private void OpenImageWithDefaultViewer()
        {
            var path = Path.Combine(_record.DirectoryPath, _record.ImageFile);
            if (!File.Exists(path)) return;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch { }
        }

        private async Task ConfirmAndDeleteAsync()
        {
            var dlg = new ContentDialog
            {
                Title = "删除失败记录？",
                Content = "此操作不可恢复，原图与错误日志都会被删除。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Root.XamlRoot
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                RecordStore.DeleteFailed(_record);
                _onDeleted();
            }
            catch (Exception ex)
            {
                var err = new ContentDialog
                {
                    Title = "删除失败",
                    Content = ex.Message,
                    CloseButtonText = "好",
                    XamlRoot = Root.XamlRoot
                };
                _ = err.ShowAsync();
            }
        }

        private async Task RetryAsync()
        {
            _retryBtn.IsEnabled = false;
            _statusText.Text = "重试中...";
            try
            {
                var settings = SettingsStore.Load();
                if (!settings.IsConfigured)
                {
                    _statusText.Text = "请先在 ⚙ 设置 中配置 Endpoint / Deployment / API Key";
                    return;
                }

                var imagePath = Path.Combine(_record.DirectoryPath, _record.ImageFile);
                if (!File.Exists(imagePath))
                {
                    _statusText.Text = "原图不存在，无法重试";
                    return;
                }
                var bytes = await File.ReadAllBytesAsync(imagePath);
                var ext = Path.GetExtension(_record.ImageFile).ToLowerInvariant();
                var mime = ext switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    _ => "image/png"
                };

                // 先删原失败目录（你确认的语义：删旧失败 → 重新调用 → 不管成败都新增一条）
                RecordStore.DeleteFailed(_record);

                var importTime = DateTime.Now;
                await RetryParseAndSaveAsync(settings, bytes, ext, mime, importTime);

                _onRetried();
            }
            catch (Exception ex)
            {
                _statusText.Text = "出错：" + ex.Message;
            }
            finally
            {
                _retryBtn.IsEnabled = true;
            }
        }

        private static async Task RetryParseAndSaveAsync(
            AppSettings settings, byte[] bytes, string ext, string mime, DateTime importTime)
        {
            var sharedRoot = Path.Combine(AppContext.BaseDirectory, "shared");
            var systemPrompt = PromptLoader.Load(sharedRoot, "parse.md");
            var schema = SchemaLoader.LoadInlined(sharedRoot, "parse-result.json");
            var client = new GptVisionClient(settings.Endpoint, settings.ApiKey, settings.DeploymentName);

            string json;
            try
            {
                json = await client.ParseScreenshotAsync(bytes, mime, systemPrompt, schema);
            }
            catch (Exception apiEx)
            {
                RecordStore.SaveFailure(bytes, ext, new FailureAttempt
                {
                    AttemptedAt = DateTime.Now,
                    Model = settings.DeploymentName,
                    ErrorType = ErrorType.ApiError,
                    ErrorMessage = apiEx.Message
                }, importTime);
                return;
            }

            JsonObject root;
            try
            {
                root = JsonNode.Parse(json) as JsonObject
                       ?? throw new InvalidOperationException("响应不是 JSON 对象");
            }
            catch (Exception parseEx)
            {
                RecordStore.SaveFailure(bytes, ext, new FailureAttempt
                {
                    AttemptedAt = DateTime.Now,
                    Model = settings.DeploymentName,
                    ErrorType = ErrorType.SchemaViolation,
                    ErrorMessage = parseEx.Message
                }, importTime);
                return;
            }

            var kindStr = root["kind"]?.GetValue<string>() ?? "unknown";
            if (kindStr == "unknown")
            {
                RecordStore.SaveFailure(bytes, ext, new FailureAttempt
                {
                    AttemptedAt = DateTime.Now,
                    Model = settings.DeploymentName,
                    ErrorType = ErrorType.KindUnknown,
                    ErrorMessage = root["error_reason"]?.GetValue<string>() ?? "kind=unknown"
                }, importTime);
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
                ApiVersion = "v1",
                ParsedAt = DateTime.Now,
                TimestampSource = eventTime is null ? TimestampSource.Import : TimestampSource.Extracted,
                MissingFields = missingFields,
                Confidence = confidence
            };

            RecordStore.SaveSuccess(bytes, ext, kind, data, parseMeta, eventTime, importTime);
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

        // 与 SavedRecordView 渲染共用：复制一份小工具以避免 nested-class 互访限制
        private static StackPanel SavedRecordView_BuildGroupedBody(IReadOnlyList<FieldGroup> groups)
        {
            var root = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var g in groups)
            {
                switch (g.Kind)
                {
                    case FieldGroupKind.Cards:
                        root.Children.Add(BuildCardGrid(g.Fields));
                        break;
                    case FieldGroupKind.SubCards:
                        if (!string.IsNullOrEmpty(g.Title))
                            root.Children.Add(new TextBlock
                            {
                                Text = g.Title,
                                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                                Opacity = 0.8,
                                Margin = new Thickness(2, 4, 0, 0)
                            });
                        root.Children.Add(BuildSubCardRow(g.Fields));
                        break;
                    case FieldGroupKind.LongText:
                        root.Children.Add(BuildLongTextCard(g.Fields[0]));
                        break;
                }
            }
            return root;
        }

        private static Grid BuildSubCardRow(IReadOnlyList<DisplayField> fields)
        {
            var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, ColumnSpacing = 8 };
            for (var i = 0; i < fields.Count; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var card = BuildFieldCard(fields[i]);
                Grid.SetColumn(card, i);
                grid.Children.Add(card);
            }
            return grid;
        }

        private const int CardColumns = 3;

        private static Grid BuildCardGrid(IReadOnlyList<DisplayField> fields)
        {
            var grid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ColumnSpacing = 8,
                RowSpacing = 8
            };
            for (var c = 0; c < CardColumns; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var rowCount = (int)Math.Ceiling(fields.Count / (double)CardColumns);
            for (var r = 0; r < rowCount; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var i = 0; i < fields.Count; i++)
            {
                var card = BuildFieldCard(fields[i]);
                Grid.SetRow(card, i / CardColumns);
                Grid.SetColumn(card, i % CardColumns);
                grid.Children.Add(card);
            }
            return grid;
        }

        private static Border BuildFieldCard(DisplayField f)
        {
            var labelBlock = new TextBlock { Text = f.Label, Opacity = 0.6, FontSize = 12 };
            var valueBlock = new TextBlock
            {
                Text = string.IsNullOrEmpty(f.Value) ? "—" : f.Value,
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                Opacity = f.IsMissing ? 0.5 : 1.0
            };
            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(labelBlock);
            stack.Children.Add(valueBlock);
            return new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = stack
            };
        }

        private static Border BuildLongTextCard(DisplayField f)
        {
            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(new TextBlock { Text = f.Label, Opacity = 0.6, FontSize = 12 });
            stack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(f.Value) ? "—" : f.Value,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Opacity = f.IsMissing ? 0.4 : 1.0
            });
            return new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = stack
            };
        }
    }
}
