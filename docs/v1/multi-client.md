# 多端共享策略

- 主文档：[design.md](design.md)
- 相关：[prompts.md](prompts.md)、[data-model.md](data-model.md)

## 1. 共享什么

```text
shared/
├── prompts/                  ← AI prompt 模板（Markdown）
│   ├── parse.md
│   └── advice.md
└── schemas/                  ← JSON Schema
    ├── workout.json
    ├── body-metrics.json
    └── parse-result.json
```

**共享的是"数据资产"，不是代码**：

- prompt 模板（中文文本 + `{占位符}`）
- JSON Schema（标准 JSON）

## 2. 不共享什么

- 调用 Azure OpenAI 的具体代码
- 本地存储读写逻辑
- UI 代码
- 配置管理 / 加密 / 错误处理代码

## 3. 各端怎么用

### 3.1 Windows 端（首发）

- `HealthyGuidance.Core/Prompts/PromptLoader.cs` 从 `shared/prompts/*.md` 读取
- `HealthyGuidance.Core/Schemas/SchemaLoader.cs` 从 `shared/schemas/*.json` 读取（含 `$ref` 内联展开）
- `HealthyGuidance.Core/AzureOpenAI/` 用 Azure.AI.OpenAI SDK 调用
- 构建时通过 csproj 的 `<Content Include>` 把 `shared/` 复制到 `bin/` 输出目录

### 3.2 鸿蒙端（未来）

- ArkTS 写一个 50-100 行的 HTTP 调用层（直接 POST 到 Azure OpenAI REST API）
- 读同一份 `shared/prompts/` 与 `shared/schemas/`
- 不调用 .NET 任何东西

### 3.3 逻辑漂移控制

- prompt 修改 → 两端同步从 `shared/` 目录加载，自动一致
- schema 修改 → 两端反序列化逻辑都按同一份 schema 工作
- 不需要"两边各写一份再人工对齐"

## 4. 鸿蒙端的技术限制

| 库类型 | 鸿蒙是否能用 | 说明 |
|---|---|---|
| C# / .NET 类库 (.dll) | ❌ | 运行时不兼容 |
| Python 包 | ❌ | 鸿蒙应用无 Python 运行时 |
| 纯 JS / TS（npm） | ✅ | 通过 ohpm 或直接引入 |
| C / C++（.so） | ✅ | 通过 NAPI，麻烦 |
| HAR / HSP | ✅ | 鸿蒙原生格式 |
| HTTP API | ✅ | 最通用 |

**结论**：跨端复用的现实路径只有"共享数据资产，各端自行实现调用层"。

## 5. 未来若有更多端

若再增加 Web / iOS / Android 等端：

- 同样从 `shared/` 读 prompt 与 schema
- 各端按自己语言生态实现 HTTP 调用 + 本地存储
- 共享层保持现状，无需扩展

## 6. 不在本文档范围

- 多端之间的**数据同步**（不做）
- 多端之间的**配置同步**（不做）
- 共享凭证（每端各自管理）
