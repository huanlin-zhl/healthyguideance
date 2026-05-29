# HealthyGuidance

运动健康监测助手。用户上传运动截图、体脂秤数据截图、菜单截图等图片，系统调用 Azure AI 服务进行 OCR 识别和智能分析，给出个性化健康建议。

## 架构概览

```text
┌──────────────────────┐         ┌────────────────────────┐         ┌──────────────────────┐
│  Windows Client      │  HTTPS  │  ASP.NET Core API      │  SDK    │  Azure AI Services   │
│  (WinUI 3 + .NET 8)  │ ──────► │  (无状态转发 + 编排)   │ ──────► │  - Document Intel.   │
│  本地 JSON + 图片    │ ◄────── │                        │ ◄────── │  - Azure OpenAI      │
└──────────────────────┘         └────────────────────────┘         └──────────────────────┘
         │                                  │
         │ 数据本地化存档                   │ 无持久化（云端实例可能随时下线）
         ▼                                  ▼
   %LocalAppData%/HealthyGuidance/   stateless
```

**核心原则**：
- **数据归用户**：所有原图与解析结果存在客户端本地（JSON + 文件夹），服务端不落盘。
- **服务端无状态**：仅做 AI 调用编排，便于随时迁移/下线。
- **多端共享契约**：[shared/api-schema/](shared/api-schema/) 定义 OpenAPI，Windows / 鸿蒙端共用。

## 目录结构

| 路径 | 说明 |
|---|---|
| [backend/](backend/) | ASP.NET Core 8 Web API，部署到 Azure App Service |
| [clients/windows/](clients/windows/) | WinUI 3 桌面端（首发） |
| [clients/harmony/](clients/harmony/) | 鸿蒙端（规划中） |
| [shared/api-schema/](shared/api-schema/) | OpenAPI 契约，多端共享 |
| [shared/sample-data/](shared/sample-data/) | 截图样例与期望输出，供联调使用 |
| [docs/](docs/) | 架构、API、ADR 决策记录 |

## 技术栈

- **后端**：ASP.NET Core 8 · Azure.AI.DocumentIntelligence · Azure.AI.OpenAI
- **Windows 端**：WinUI 3 · .NET 8 · CommunityToolkit.Mvvm
- **AI 服务**：Azure AI Document Intelligence（图片→结构化数据）+ Azure OpenAI GPT-4o（解读 & 建议）
- **存储**：客户端本地 JSON + 图片文件夹；服务端无状态

## 快速开始

待补：见 [docs/](docs/) 下的开发指南。
