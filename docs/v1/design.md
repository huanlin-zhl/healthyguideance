# HealthyGuidance v1 设计文档

- 状态：Draft（骨架版）
- 日期：2026-05-30

## 项目目录结构

```text
HealthyGuidance/
├── clients/
│   ├── windows/                       ← WinUI 3 桌面端（首发）
│   │   ├── HealthyGuidance.App/       ← UI 层：页面、ViewModel、本地存储读写
│   │   └── HealthyGuidance.Core/      ← 纯逻辑类库：AI 调用、prompt 加载、数据模型
│   └── harmony/                       ← 鸿蒙端（未来）
├── shared/                            ← 多端共享资产
│   ├── prompts/                       ← AI prompt 模板
│   │   ├── parse.md                   （解析单张截图）
│   │   └── advice.md                  （趋势分析报告）
│   └── schemas/                       ← JSON Schema
│       ├── workout.json
│       └── body-metrics.json
├── docs/                              ← 设计文档
│   ├── v1/                            （当前版本）
│   └── decisions/                     （ADR 历史）
├── SamplePicture/                     ← 测试截图样本
├── scripts/                           ← 辅助脚本（按需）
└── README.md
```

**说明：**

- **不再有 `backend/`**：v1 已确定砍掉服务端，客户端直连 Azure AI Foundry。
- **`clients/windows/` 拆两个项目**：`App`（UI）与 `Core`（纯逻辑类库）分离，便于将来 Core 复用到其他宿主（例如未来若改为 server 版）。
- **`shared/` 不再含 `api-schema/`**：原 OpenAPI 契约随后端一起废弃，多端共享的是 prompt 模板与 JSON Schema。
- **鸿蒙端不能引用 .NET 类库**，因此跨端复用走"共享数据资产"而非"共享代码"。

## 后续待补章节

子文档按讨论顺序逐个补充：

- [x] [storage.md](storage.md) —— 本地存储结构
- [x] [ui-flow.md](ui-flow.md) —— UI 流程
- [x] [prompts.md](prompts.md) —— AI Prompt 设计
- [x] [data-model.md](data-model.md) —— 数据类型、Schema、落盘结构
- [x] [security.md](security.md) —— Key 与安全
- [x] [errors.md](errors.md) —— 错误与异常处理
- [x] [product.md](product.md) —— 产品定位与功能优先级
- [x] [architecture.md](architecture.md) —— 架构、技术栈、客户端分层
- [x] [multi-client.md](multi-client.md) —— 多端共享策略
- [x] [report.md](report.md) —— 分析报告内容结构
