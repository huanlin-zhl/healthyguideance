# AI Prompt 设计

- 主文档：[design.md](design.md)

## 1. 总体策略

| 项 | 决策 |
|---|---|
| 模板存放 | `shared/prompts/*.md`（多端共享） |
| 变量替换 | Markdown 中用 `{占位符}` 标记，代码 `string.Replace()` 替换 |
| 输出格式 | OpenAI **Structured Outputs**（`ChatResponseFormat.CreateJsonSchemaFormat`，严格模式） |
| Schema 文件 | `shared/schemas/*.json`，作为 SoT（唯一真实源），prompt 不重复列字段 |
| Prompt 语言 | 中文（系统提示 + 用户消息）；字段名（JSON key）英文；字段值保留中文 |
| 模型 | `gpt-4o-mini`（vision-enabled，可升级到 `gpt-4o`） |
| SDK | `OpenAI` 官方包，通过 Azure AI Foundry v1 兼容 endpoint 调用 |

## 2. 调用清单

应用全局只有两处 LLM 调用：

1. **截图解析**：输入一张图 → 输出 `kind` + 结构化字段
2. **趋势分析**：输入窗口期内全部结构化数据 + 饮食备注 → 输出五块式报告

饮食备注录入**不调** LLM。

## 3. 截图解析 prompt

### 3.1 文件位置

`shared/prompts/parse.md`

### 3.2 模板内容（草案）

```markdown
你是一名健康数据提取助手。用户会上传**运动 App 截图**或**体脂秤截图**。
你的任务：判断截图类型并按照给定 JSON Schema 抽取字段。

## 类型判断
- `workout`：运动 App 截图（含运动类型、时长、心率等）
- `body-metrics`：体脂秤截图（含体重、体脂率、骨骼肌等）
- 都不像 → 在输出中以 schema 允许的方式表达"无法识别"

## 抽取规则
1. **数字字段**：原样抽取，单位换算为 schema 注释中标注的标准单位
   （如时长统一为秒、距离统一为米、心率单位次/分钟）
2. **时间字段**：解析为 ISO 8601 格式（`YYYY-MM-DDTHH:mm:ss`）
   - 截图中通常带"2026/5/27 19:35"或类似表达
   - 时区按本地时间处理，不带时区后缀
3. **运动类型 / 体型描述等文字字段**：保留中文原文（如 `户外骑行`、`苹果型`）
4. **缺失字段**：填 `null`，不要编造
5. **来源元信息**：从截图底部 logo / 设备名抽取
   （如 `source_app: 华为运动健康`、`device: HUAWEI WATCH GT 3 Pro`）

## 输出
严格按 JSON Schema 输出，不要包含任何额外文字。
```

### 3.3 入参

无变量替换（截图通过 vision message 直接传入）。

### 3.4 调用方式（伪代码）

```csharp
var systemPrompt = LoadPrompt("parse.md");
var schema = LoadSchema("parse-result.json");  // 见 §3.5

var client = new ChatClient(
    credential: new ApiKeyCredential(apiKey),
    model: "gpt-4o-mini",
    options: new OpenAIClientOptions { Endpoint = new Uri(endpoint) }
);

var completion = await client.CompleteChatAsync(
    new ChatMessage[] {
        new SystemChatMessage(systemPrompt),
        new UserChatMessage(
            ChatMessageContentPart.CreateTextPart("请解析这张截图。"),
            ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(imageBytes), "image/png")
        )
    },
    new ChatCompletionOptions {
        ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
            jsonSchemaFormatName: "parse_result",
            jsonSchema: BinaryData.FromString(schema),
            jsonSchemaIsStrict: true
        )
    }
);
```

### 3.5 输出 Schema

`shared/schemas/parse-result.json` —— **统一封装**，根级有 `kind` 字段，按 `kind` 区分子结构：

```jsonc
{
  "type": "object",
  "properties": {
    "kind": { "enum": ["workout", "body-metrics", "unknown"] },
    "workout":      { "$ref": "workout.json" },        // 仅 kind=workout 时填
    "body_metrics": { "$ref": "body-metrics.json" },   // 仅 kind=body-metrics 时填
    "error_reason": { "type": "string", "nullable": true }  // kind=unknown 时填
  },
  "required": ["kind"]
}
```

实现细节（`$ref` 解引用 / Azure Structured Outputs 对 `oneOf` 的支持）以 [data-model.md](data-model.md) 定稿后为准。

### 3.6 置信度策略

不让模型输出 `parse_confidence` 字段。客户端按"必需字段覆盖率"自行推断：

- 必需字段全部非 null → `high`
- 缺 1 个 → `medium`
- 缺 2+ 或 `kind = unknown` → `low`

`low` 的记录在 UI 上额外标记（黄色 ⚠ 而非红色 ✗，跟"完全失败"区分）。

## 4. 趋势分析 prompt

### 4.1 文件位置

`shared/prompts/advice.md`

### 4.2 模板内容（草案）

```markdown
你是一名健康教练，根据用户提供的运动数据、体成分数据和饮食备注，
给出一份针对未来一段时间的个性化建议报告。

## 用户背景
- 当前时间：{current_time}
- 时间窗口：{window_start} 至 {window_end}
- 目标体重：{goal_weight_kg} kg
- 目标体脂率：{goal_body_fat_pct}%

## 数据
### 体成分记录（按时间正序）
```json
{body_metrics_json}
```

### 运动记录（按时间正序）
```json
{workouts_json}
```

### 饮食备注（按时间正序，原文，含相对时间词请自行推理）
```
{notes_text}
```

## 输出要求
严格按 JSON Schema 输出 5 个段落字段。每段一段自然语言中文，
不要列点（除非必要），口吻像私人教练而非医生。

- **summary**（现状小结）：时间窗口内体重 / 体脂率 / 内脏脂肪变化与运动总量
- **trend**（趋势判断）：变好 / 变差 / 停滞，简要分析原因
- **diet_advice**（饮食建议）：基于备注 + 体成分趋势的方向性建议；备注稀疏时给方向，备注充足时可针对具体食物
- **workout_advice**（运动建议）：下周建议频次、类型、强度
- **warnings**（注意事项）：异常项（如肌肉量下降、内脏脂肪上升、运动恢复时间长期偏高等）；无异常返回空字符串

**底部固定追加一句免责声明**（已包含在 schema 中，模型不必生成）。

## 边界
- 无菜单/餐次精确数据，**不要给"少吃 200 卡"这类量化指令**
- 数据不足时（如体成分只有 1 条）**明确说"数据太少，无法判断趋势"**而不是硬给结论
- 不要重复"根据您提供的数据"等套话
```

### 4.3 入参占位符

| 占位符 | 类型 | 说明 |
|---|---|---|
| `{current_time}` | ISO 8601 | 报告生成时刻 |
| `{window_start}` / `{window_end}` | ISO 8601 | 时间窗口起止 |
| `{goal_weight_kg}` / `{goal_body_fat_pct}` | 数字 | 用户在设置页填的目标 |
| `{body_metrics_json}` | JSON 字符串 | 窗口内所有体成分记录数组，**全量喂** |
| `{workouts_json}` | JSON 字符串 | 窗口内所有运动记录数组，**全量喂** |
| `{notes_text}` | 多行文本 | 窗口内所有备注，每条两行（时间戳 + 原文），按时间正序 |

### 4.4 输出 Schema

`shared/schemas/report.json`：

```jsonc
{
  "type": "object",
  "properties": {
    "summary":        { "type": "string" },
    "trend":          { "type": "string" },
    "diet_advice":    { "type": "string" },
    "workout_advice": { "type": "string" },
    "warnings":       { "type": "string" }
  },
  "required": ["summary", "trend", "diet_advice", "workout_advice", "warnings"]
}
```

报告写盘时由客户端追加元信息（生成时间、窗口、所用数据 id 列表、模型版本、免责声明）。最终落盘 JSON 结构以 [report.md](report.md) 为准（待补）。

### 4.5 数据量与 token

预估 30 天典型数据：

- 体成分 10 条 × 约 300 字符 = 3 KB
- 运动 15 条 × 约 400 字符 = 6 KB
- 备注 30 条 × 约 100 字符 = 3 KB

合计约 12 KB ≈ 3-4k tokens，远低于 GPT-4o 128k 上下文上限。**全量喂安全**。

数据量异常增长（如时间窗口拉到 1 年）时再考虑分桶汇总，P0 不做。

## 5. 加载与替换

### 5.1 PromptLoader（Core 类库）

```text
HealthyGuidance.Core/
└── Prompts/
    └── PromptLoader.cs
```

职责：
- 从应用根目录的 `shared/prompts/*.md` 读取模板
- 提供 `Render(string templateName, IDictionary<string, string> vars)` 方法做 `{占位符}` 替换
- 找不到占位符的变量 → 抛异常（避免静默缺参）
- 模板中有未替换的 `{xxx}` → 抛异常（避免变量名拼错）

### 5.2 SchemaLoader

同理，从 `shared/schemas/*.json` 读取并缓存（schema 不变可全程缓存）。

### 5.3 部署时的 shared/ 位置

构建发布时 `shared/` 跟随 exe 一起拷贝。`HealthyGuidance.App.csproj` 中配置：

```xml
<ItemGroup>
  <Content Include="..\..\..\shared\**\*">
    <Link>shared\%(RecursiveDir)%(Filename)%(Extension)</Link>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

运行时通过 `AppContext.BaseDirectory + "shared/"` 定位。

## 6. 调用失败与重试

详见 [errors.md](errors.md)（待补）。本文只声明：

- **HTTP 5xx / 限流（429）**：自动重试最多 3 次，指数退避
- **解析失败（JSON 不符合 schema）**：理论上 Structured Outputs 不会发生；若发生则按"失败"处理写入 `failed/`
- **网络断 / Key 失效（401）**：直接抛错给 UI 显示
