# HealthyGuidance

个人运动健康教练。批量喂入运动 / 体脂秤截图与文字饮食备注，AI 基于时间窗口内的数据趋势，给出未来几天的饮食与训练建议。

## 架构概览

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

**核心原则**：

- **纯客户端**：客户端直连 Azure AI Foundry，无后端，无服务端运维
- **数据归用户**：所有原图与解析结果存在客户端本地（`%LocalAppData%\HealthyGuidance\`）
- **GPT-4o 一次搞定**：vision 模型直接读图，OCR + 字段抽取 + 类型判断一次调用完成（不再用 Document Intelligence）
- **多端共享数据资产**：[shared/prompts/](shared/prompts/) 与 [shared/schemas/](shared/schemas/) 是 prompt 模板与 JSON Schema，多端复用；调用代码各端自实现

## 目录结构

| 路径 | 说明 |
|---|---|
| [clients/windows/](clients/windows/) | WinUI 3 桌面端（首发） |
| [clients/harmony/](clients/harmony/) | 鸿蒙端（未来） |
| [shared/prompts/](shared/prompts/) | AI prompt 模板（多端共享） |
| [shared/schemas/](shared/schemas/) | JSON Schema（多端共享） |
| [docs/v1/](docs/v1/) | v1 设计文档（10 篇） |
| [docs/decisions/](docs/decisions/) | ADR 历史 |
| [SamplePicture/](SamplePicture/) | 测试截图样本 |

## 技术栈

- **Windows 端**：WinUI 3 (Unpackaged) · .NET 8 · CommunityToolkit.Mvvm
- **AI 服务**：Azure AI Foundry 上的 GPT-4o（vision-enabled），通过 `Azure.AI.OpenAI` SDK 调用
- **数据存储**：客户端本地（截图 PNG + 解析 JSON + 备注 TXT + 报告 JSON）
- **凭证管理**：Windows DPAPI 加密存储于 `config/secrets.dat`

## 设计文档

详细设计见 [docs/v1/design.md](docs/v1/design.md)，含 10 篇子文档：

| 文档 | 内容 |
|---|---|
| [product.md](docs/v1/product.md) | 产品定位、功能优先级 |
| [architecture.md](docs/v1/architecture.md) | 架构、技术栈、客户端分层 |
| [data-model.md](docs/v1/data-model.md) | 数据类型、Schema、落盘结构 |
| [storage.md](docs/v1/storage.md) | 本地存储目录布局 |
| [ui-flow.md](docs/v1/ui-flow.md) | UI 流程与页面规范 |
| [prompts.md](docs/v1/prompts.md) | AI prompt 设计 |
| [report.md](docs/v1/report.md) | 分析报告结构 |
| [security.md](docs/v1/security.md) | Key 加密与脱敏规则 |
| [errors.md](docs/v1/errors.md) | 错误与异常处理 |
| [multi-client.md](docs/v1/multi-client.md) | 多端共享策略 |

## 快速开始

待补：见 [docs/v1/](docs/v1/) 下的相关章节，开发指南后续补充。
