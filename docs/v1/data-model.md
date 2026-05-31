# 数据模型

- 主文档：[design.md](design.md)
- 相关文档：[storage.md](storage.md)（落盘布局）、[prompts.md](prompts.md)（如何被 LLM 消费）

## 1. 数据类型分类

| 类型 | 来源 | 存储形态 | 是否结构化 |
|---|---|---|---|
| 运动 | 截图（GPT-4o 解析） | `records/<yyyy-MM>/<id>_workout_<hash>/parsed.json` | ✅ 严格 schema |
| 体成分 | 截图（GPT-4o 解析） | `records/<yyyy-MM>/<id>_body-metrics_<hash>/parsed.json` | ✅ 严格 schema |
| 饮食备注 | 用户文字输入 | `notes/<yyyy-MM>.txt`（纯文本） | ❌ 不抽字段 |
| 报告 | LLM 生成 | `reports/<yyyy-MM>/<id>.json` | ✅ 五段式 |
| 解析失败 | 任何上传失败 | `failed/<yyyy-MM>/<id>/error.json` | ✅ 元信息 |

## 2. 命名规范

- **JSON key**：`snake_case`（`avg_heart_rate`、`body_fat_pct`）
- **字符串值**：保留中文原文（`sport_type: "户外骑行"`、`body_type: "苹果型"`）
- **时间**：ISO 8601 不带时区（`2026-05-27T19:35:00`），按本地时间处理
- **数值单位**：见各 schema 字段注释；统一标准单位（时长秒、距离米、心率次/分钟、重量千克）
- **C# 反序列化**：使用 `JsonNamingPolicy.SnakeCaseLower`

## 3. Schema 文件组织

3 个独立文件，结构清晰，加载时合并：

```text
shared/schemas/
├── workout.json          ← 运动 schema
├── body-metrics.json     ← 体成分 schema
└── parse-result.json     ← LLM 解析输出顶层封装（含 kind 判断）
```

`parse-result.json` 用 `$ref` 引用前两个；**SchemaLoader** 在加载时把 `$ref` 内联展开，喂给 Azure Structured Outputs（API 不支持跨文件 `$ref`）。

## 4. 运动 Schema

### 4.1 字段定义

| 字段 | 类型 | 必需 | 单位 | 说明 |
|---|---|---|---|---|
| `date_time` | string (ISO 8601) | ✅ | — | 运动开始时间 |
| `sport_type` | string | ✅ | — | 运动类型，按截图原文（`户外骑行` / `泳池游泳` / `自由训练` / `跑步` 等） |
| `duration_text` | string | ✅ | — | 运动总时长原文（如 `01:34:26` / `46分5秒`），客户端自行解析 |
| `calories_text` | string | ✅ | — | 消耗热量原文（如 `861 千卡`） |
| `avg_heart_rate` | integer \| null | ❌ | bpm | 平均心率（纯数字） |
| `max_heart_rate` | integer \| null | ❌ | bpm | 最大心率（纯数字） |
| `hr_zones` | object \| null | ❌ | 原文字符串 | 心率区间分布 |
| `hr_zones.extreme` | string | — | — | 极限（如 `"0 分钟"`） |
| `hr_zones.anaerobic` | string | — | — | 无氧耐力（如 `"0 分钟"`） |
| `hr_zones.aerobic` | string | — | — | 有氧耐力（如 `"6 分钟"`） |
| `hr_zones.fat_burn` | string | — | — | 燃脂（如 `"29 分钟"`） |
| `hr_zones.warmup` | string | — | — | 热身（如 `"10 分钟"`） |
| `distance_text` | string \| null | ❌ | — | 距离原文（如 `30.85 公里` / `1000 米`） |
| `avg_pace_or_speed` | string \| null | ❌ | — | 配速或平均速度原文（`5'46"/km` 或 `19.60 km/h`） |

### 4.2 JSON Schema（草案）

```jsonc
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "workout.json",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "date_time":         { "type": "string", "format": "date-time" },
    "sport_type":        { "type": "string" },
    "duration_text":     { "type": "string" },
    "calories_text":     { "type": "string" },
    "avg_heart_rate":    { "type": ["integer", "null"] },
    "max_heart_rate":    { "type": ["integer", "null"] },
    "hr_zones": {
      "type": ["object", "null"],
      "additionalProperties": false,
      "properties": {
        "extreme":   { "type": "string" },
        "anaerobic": { "type": "string" },
        "aerobic":   { "type": "string" },
        "fat_burn":  { "type": "string" },
        "warmup":    { "type": "string" }
      },
      "required": ["extreme", "anaerobic", "aerobic", "fat_burn", "warmup"]
    },
    "distance_text":      { "type": ["string", "null"] },
    "avg_pace_or_speed":  { "type": ["string", "null"] }
  },
  "required": [
    "date_time", "sport_type", "duration_text", "calories_text",
    "avg_heart_rate", "max_heart_rate", "hr_zones",
    "distance_text", "avg_pace_or_speed"
  ]
}
```

**说明**：Azure Structured Outputs 要求 `required` 列出**所有** properties，可选字段通过 `"type": ["...", "null"]` 表达。

### 4.3 必需字段（用于置信度推断）

口径上"必需"的字段（缺则记录意义大打折扣）：

```
date_time, sport_type, duration_text, calories_text
```

这 4 个缺任何一个 → 视为低置信度（UI 黄色 ⚠）。其他字段缺失正常。

## 5. 体成分 Schema

### 5.1 字段定义

| 字段 | 类型 | 必需 | 单位 | 说明 |
|---|---|---|---|---|
| `measured_at` | string (ISO 8601) | ✅ | — | 测量时间 |
| `weight_kg` | number | ✅ | kg | 体重 |
| `bmi` | number \| null | ❌ | — | BMI |
| `body_fat_pct` | number \| null | ❌ | % | 体脂率 |
| `visceral_fat_level` | number \| null | ❌ | 级 | 内脏脂肪等级 |
| `skeletal_muscle_kg` | number \| null | ❌ | kg | 骨骼肌量 |
| `heart_rate_bpm` | integer \| null | ❌ | bpm | 静息心率（体脂秤测量时） |

### 5.2 JSON Schema（草案）

结构同 §4.2：所有 properties 在 `required` 中列出，可选字段通过 `"type": ["...", "null"]` 表达。完整定义见 [shared/schemas/body-metrics.json](../../shared/schemas/body-metrics.json)。

### 5.3 必需字段（用于置信度推断）

```
measured_at, weight_kg
```

只要这两项在就算基本可用。

## 6. parse-result.json（LLM 解析输出顶层封装）

```jsonc
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "parse-result.json",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "kind":         { "type": "string", "enum": ["workout", "body-metrics", "unknown"] },
    "workout":      { "$ref": "workout.json" },
    "body_metrics": { "$ref": "body-metrics.json" },
    "error_reason": { "type": ["string", "null"] }
  },
  "required": ["kind", "workout", "body_metrics", "error_reason"]
}
```

**消费规则**：
- `kind = "workout"` → 读 `workout` 字段，`body_metrics` 应为 null
- `kind = "body-metrics"` → 读 `body_metrics` 字段，`workout` 应为 null
- `kind = "unknown"` → `error_reason` 给出原因；客户端按"解析失败"处理

**SchemaLoader 合并逻辑**：加载 `parse-result.json` 时识别 `$ref`，把 `workout.json` 和 `body-metrics.json` 内容内联进对应位置，最终喂给 API 的是单文件 schema。

## 7. parsed.json 落盘结构（成功记录）

LLM 输出 + 客户端追加的元信息：

```jsonc
{
  "id":           "20260524-190100_workout_a1b2c3d4",
  "kind":         "workout",
  "saved_at":     "2026-05-24T19:05:23",
  "image_file":   "screenshot.png",
  "image_sha256": "a1b2c3d4e5f6789...",
  "parse": {
    "model":             "gpt-4o",
    "api_version":       "2024-10-21",
    "parsed_at":         "2026-05-24T19:05:21",
    "timestamp_source":  "extracted",       // "extracted" | "import"
    "missing_fields":    [],                // 必需字段中缺失的字段名
    "confidence":        "high"             // "high" | "medium" | "low"
  },
  "data": {
    // LLM 输出的 workout schema 内容
    "date_time":     "2023-10-17T20:31:00",
    "sport_type":    "户外骑行",
    "duration_sec":  5666,
    "calories":      861,
    ...
  }
}
```

**字段说明**：
- `id`：等于所在目录名
- `kind`：与目录名中段一致
- `saved_at`：客户端写盘时间
- `image_sha256`：完整 SHA256，去重用
- `parse.timestamp_source`：`extracted` 表示用 LLM 抽出的事件时间；`import` 表示 LLM 未抽出，用导入时间兜底
- `parse.missing_fields`：必需字段中缺失的，用于推断 `confidence`
- `parse.confidence`：派生字段，规则见 §4.3 / §5.3
- `data`：LLM 输出的纯净结构化数据

## 8. error.json 落盘结构（失败记录）

```jsonc
{
  "id":           "20260530-120000_x9y8z7w6",
  "saved_at":     "2026-05-30T12:00:01",
  "image_file":   "screenshot.png",
  "image_sha256": "x9y8z7w6v5u4...",
  "attempts": [
    {
      "attempted_at": "2026-05-30T12:00:01",
      "model":        "gpt-4o",
      "error_type":   "schema_violation",
      "error_message": "Required field 'date_time' is null"
    }
  ]
}
```

- `attempts` 数组：每次重试追加一条，**不覆盖**历史
- `error_type` 取值（约定）：
  - `schema_violation`：返回 JSON 不符合 schema（Structured Outputs 下罕见）
  - `kind_unknown`：LLM 返回 `kind = "unknown"`
  - `api_error`：HTTP 错误（4xx/5xx）
  - `network_error`：网络层失败
  - `timeout`：调用超时

重试成功后该目录从 `failed/` 移至 `records/`，按 `parsed.json` 规则写入。`error.json` 不保留。

## 9. report.json（分析报告）

LLM 输出的五段文字 + 客户端元信息。具体结构在 [report.md](report.md) 定稿（待补）。本文档先约定：

```jsonc
{
  "id":          "20260530-153000_r9s0t1u2",
  "generated_at": "2026-05-30T15:30:00",
  "window": {
    "start": "2026-04-30T00:00:00",
    "end":   "2026-05-30T15:30:00",
    "preset": "30d"                          // "7d" | "30d" | "90d" | "custom"
  },
  "goal": {
    "weight_kg":    78,
    "body_fat_pct": 20
  },
  "source": {
    "model":             "gpt-4o",
    "api_version":       "2024-10-21",
    "workout_ids":       ["20260527-193500_workout_b2c3d4e5", ...],
    "body_metrics_ids":  ["20260529-072300_body-metrics_e5f6g7h8", ...],
    "notes_window":      ["2026-04.txt", "2026-05.txt"]
  },
  "content": {
    "summary":        "...",
    "trend":          "...",
    "diet_advice":    "...",
    "workout_advice": "...",
    "warnings":       ""
  },
  "disclaimer": "本建议仅供参考，不构成医疗或营养专业意见。"
}
```

## 10. Schema 演进策略

将来加字段：

- **加可选字段**（新增 `"type": ["...", "null"]`）→ 兼容老数据，老 parsed.json 反序列化时新字段为 null
- **加必需字段** → 需要数据迁移；P0 阶段不允许
- **改字段名** → 视同删旧加新，需迁移；P0 阶段不允许

为防止以后混乱，每个 `parsed.json` / `report.json` **可考虑加 `schema_version` 字段**（如 `"schema_version": 1`）。P0 暂不加（YAGNI），等真正需要迁移时再引入。
