# Windows Desktop Client

WinUI 3 + .NET 8 桌面客户端。负责截图导入、本地存档、调用后端 API 获取解析与建议、历史浏览与趋势可视化。

## 技术栈

- **UI**：WinUI 3（Windows App SDK 1.5+）
- **MVVM**：`CommunityToolkit.Mvvm`
- **HTTP**：`Refit` 或 `HttpClient` + 从 [shared/api-schema/](../../shared/api-schema/) 生成的客户端
- **本地存储**：System.Text.Json + 本地文件夹（见下方"数据落盘"）

## 目录结构

| 文件夹 | 职责 |
|---|---|
| `Views/` | XAML 页面与控件 |
| `ViewModels/` | MVVM 视图模型 |
| `Services/` | 本地存储、API 客户端、截图处理 |
| `Models/` | 本地数据模型（与后端 Contracts 大部分对齐） |
| `Assets/` | 图标、样式资源 |

## 数据落盘

所有用户数据保存在：

```
%LocalAppData%\HealthyGuidance\
├── images\
│   ├── workout\<yyyy-MM-dd>\<guid>.png       # 原图
│   ├── body-metrics\<yyyy-MM-dd>\<guid>.png
│   └── meals\<yyyy-MM-dd>\<guid>.png
├── records\
│   ├── workout-sessions.json                  # 解析后的结构化数据
│   ├── body-metrics.json
│   └── meals.json
└── advice\
    └── <yyyy-MM-dd>-<guid>.json               # AI 建议归档
```

## 待办

- [ ] 初始化 WinUI 3 项目（`dotnet new winui` 或 Visual Studio 模板）
- [ ] 截图导入页 + 拖拽上传
- [ ] 本地存储服务 `ILocalArchiveService`
- [ ] API 客户端封装
- [ ] 历史记录与趋势图表
