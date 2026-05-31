# 架构

- 主文档：[design.md](design.md)
- 相关：[data-model.md](data-model.md)、[prompts.md](prompts.md)、[security.md](security.md)

## 1. 架构总览

```text
┌──────────────────────────────┐         ┌──────────────────────────┐
│  Windows Client              │  HTTPS  │  Azure AI Foundry        │
│  (WinUI 3 + .NET 8)          │ ──────► │  GPT-4o (vision-enabled) │
│  - UI 层                     │ ◄────── │                          │
│  - HealthyGuidance.Core 类库 │         └──────────────────────────┘
│  - 本地 JSON + 图片          │
└──────────────────────────────┘
              │ （未来）
              ▼
┌──────────────────────────────┐
│  HarmonyOS Client (ArkTS)    │  共用 shared/prompts + shared/schemas
└──────────────────────────────┘
```

## 2. 关键决策

| 决策 | 内容 | 理由 |
|---|---|---|
| 砍掉后端 | 不再做 ASP.NET Core 网关 | 单用户、本机使用、Key 放本地可接受；后端只增加部署/维护成本无等价收益 |
| 直连 Azure AI Foundry | 客户端用 Azure OpenAI .NET SDK 直接调用 | 简单直接，少一跳延迟 |
| 不用 Document Intelligence | GPT-4o vision 原生支持图片 | 一次调用完成 OCR + 字段抽取 + 类型判断，更准更省 |
| 多端共享靠数据资产 | 共享 prompt 模板 + JSON Schema，不共享代码 | 鸿蒙端无法引用 .NET 类库 |

详细演进历史见 [decisions/0001-core-data-flow.md](../decisions/0001-core-data-flow.md)（已废弃，仅作历史记录）。

## 3. 客户端内部分层

```text
┌─────────────────────────────────────────────┐
│  HealthyGuidance.App（WinUI 3 项目）        │
│  - Pages / ViewModels（界面）               │
│  - Services（本地存储、文件读写）           │
│  - 引用 Core                                │
│  - 引用 CommunityToolkit.Mvvm               │
└─────────────────────────────────────────────┘
              │ 引用
              ▼
┌─────────────────────────────────────────────┐
│  HealthyGuidance.Core（纯 net8.0 类库）     │
│  - AzureOpenAI（GPT-4o 调用封装）           │
│  - Prompts（PromptLoader）                  │
│  - Schemas（SchemaLoader）                  │
│  - Models（数据模型）                       │
│  - 引用 OpenAI SDK                          │
└─────────────────────────────────────────────┘
              │ HTTPS
              ▼
        Azure AI Foundry (GPT-4o)
```

Core 类库**刻意不依赖 UI 与 Windows**：

- 目标框架 `net8.0`（非 `net8.0-windows`）
- 不引用 WinUI / WindowsAppSDK
- 未来若做 server 版可整体搬到 ASP.NET Core 复用

## 4. 技术栈

| 项 | 选型 | 版本 |
|---|---|---|
| .NET | .NET 8 | 8.x |
| UI 框架 | WinUI 3 | 通过 WindowsAppSDK |
| Windows App SDK | Microsoft.WindowsAppSDK | 2.1.3 |
| 项目模板 | Unpackaged（`<WindowsPackageType>None</WindowsPackageType>`） | — |
| MVVM | CommunityToolkit.Mvvm | 8.4.2 |
| Azure OpenAI SDK | OpenAI (官方包，通过 Azure AI Foundry v1 兼容 endpoint) | 2.1.0 |
| Azure AI Foundry endpoint | `https://<resource>.services.ai.azure.com/openai/v1` | — |
| 模型部署 | gpt-4o (vision-enabled) | 部署在 Azure AI Foundry |

## 5. 项目目录结构

```text
HealthyGuidance/
├── clients/
│   ├── windows/
│   │   ├── HealthyGuidance.slnx
│   │   ├── HealthyGuidance.App/
│   │   │   ├── Pages/
│   │   │   ├── ViewModels/
│   │   │   ├── Services/
│   │   │   ├── App.xaml + App.xaml.cs
│   │   │   ├── MainWindow.xaml + MainWindow.xaml.cs
│   │   │   ├── app.manifest
│   │   │   └── HealthyGuidance.App.csproj
│   │   └── HealthyGuidance.Core/
│   │       ├── AzureOpenAI/
│   │       ├── Prompts/
│   │       ├── Schemas/
│   │       ├── Models/
│   │       └── HealthyGuidance.Core.csproj
│   └── harmony/                       ← 未来
├── shared/
│   ├── prompts/                       ← AI prompt 模板
│   │   ├── parse.md
│   │   └── advice.md
│   └── schemas/                       ← JSON Schema
│       ├── workout.json
│       ├── body-metrics.json
│       └── parse-result.json
├── docs/
│   ├── v1/                            ← 本设计文档目录
│   └── decisions/                     ← ADR 历史
├── SamplePicture/                     ← 测试截图样本
├── scripts/                           ← 辅助脚本
└── README.md
```

## 6. 部署形态

- **运行时依赖**：用户机器需安装 Windows App SDK Runtime（Unpackaged 应用要求）
- **发布产物**：`dotnet publish` 输出文件夹（含 exe + 依赖 dll + `shared/` 目录）
- **数据位置**：`%LocalAppData%\HealthyGuidance\`，与发布产物分离
- **更新方式**：手动替换发布文件夹（个人使用，不做自动更新）

## 7. 不在架构范围

- 服务端（已砍）
- 数据库（用本地 JSON / txt）
- 消息队列 / 后台 worker
- 鉴权 / 多租户
- 监控 / APM
