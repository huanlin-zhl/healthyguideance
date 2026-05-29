# 设计文档：HealthyGuidance 核心数据流

- 状态：Draft
- 日期：2026-05-29

## 1. 背景

HealthyGuidance 收集用户的运动、身体数据、饮食记录，并基于最近一段时间的记录给出健康建议。

数据来源两种：

- **截图**：运动 App 截图、体脂秤截图、食堂菜单截图
- **自然语言**：用户口述某几天吃了什么

## 2. 设计原则

1. **简单优先**：三个端点解决全部问题，避免提前抽象。
2. **服务端无状态**：只做识别/解析/分析，**不保存任何用户数据**。
3. **保存前必先确认**：所有服务端返回的结构化结果都是"未保存的候选"，由用户在客户端 UI 中确认后写本地。
4. **数据归用户**：本地 JSON + 图片文件夹，便于备份、迁移。

## 3. 数据模型

### 3.1 四种本地数据

| kind | 来源 | 是否有图片 | 用途 |
|---|---|---|---|
| `workout` | 截图 | ✅ | 运动数据（时长、距离、热量、心率） |
| `bodyMetrics` | 截图 | ✅ | 体重、体脂、肌肉量等 |
| `menu` | 截图 | ✅ | 菜单 OCR 结果（**仅存档，不参与分析**） |
| `meal` | 自然语言 | ❌ | 用户实际吃了什么（参与分析） |

### 3.2 数据流

```text
┌──────────────────────────────────────────────────────────────┐
│                  客户端本地存储                              │
│  workout/  bodyMetrics/  menu/  meal/  advice/               │
└──────────────────────────────────────────────────────────────┘
       ▲           ▲                          ▲
       │ confirm   │ confirm                  │ confirm
       │           │                          │
┌──────┴────┐  ┌───┴──────┐         ┌─────────┴─────────┐
│ 截图识别  │  │ 饮食解析 │         │ 综合分析报告       │
└──────┬────┘  └────┬─────┘         └─────────┬─────────┘
       │            │                         │
       ▼            ▼                         ▼
POST /api/screenshots/recognize    POST /api/advice/analyze
                  │
       POST /api/meals/recognize
                  │
       Document Intelligence + Azure OpenAI
```

## 4. API 契约

### 4.1 `POST /api/screenshots/recognize`

上传截图，服务端 OCR + LLM 自动判类，返回**未保存**的结构化结果给客户端确认。

**Request** (`multipart/form-data`):

| 字段 | 类型 | 必需 | 说明 |
|---|---|---|---|
| `image` | file | ✅ | 截图原文件（PNG/JPEG） |

**Response 200**:

```json
{
  "kind": "workout",
  "confidence": 0.93,
  "capturedAt": "2026-05-29T10:32:00Z",
  "structured": { /* 因 kind 而异，见 §5 */ },
  "model": {
    "extractor": "prebuilt-layout",
    "classifier": "gpt-4o-2024-08-06"
  }
}
```

- `kind`：LLM 自动判定，取值 `workout` / `bodyMetrics` / `menu`
- `capturedAt`：OCR 出的事件时间；解析不出则为 `null`，客户端 UI 提示用户填写
- 客户端拿到响应后展示在确认 UI，用户点保存才写本地

### 4.2 `POST /api/meals/recognize`

接收用户自然语言（"这周早上都是两个水煮蛋"），LLM 解析为按日期分组的 meal 候选。

**Request** (`application/json`):

```json
{
  "freeText": "这周早上都是两个水煮蛋，周三加了一个煎饼",
  "today": "2026-05-29"
}
```

| 字段 | 类型 | 必需 | 说明 |
|---|---|---|---|
| `freeText` | string | ✅ | 用户口述 |
| `today` | string (YYYY-MM-DD) | ✅ | 客户端当前日期，用于解析"这周/明天"等相对表达 |

**Response 200**:

```json
{
  "candidates": [
    {
      "appliesToDate": "2026-05-25",
      "mealType": "breakfast",
      "items": [
        { "name": "水煮蛋", "quantity": "2 个", "estimatedKcal": 156 }
      ]
    },
    {
      "appliesToDate": "2026-05-27",
      "mealType": "breakfast",
      "items": [
        { "name": "水煮蛋", "quantity": "2 个", "estimatedKcal": 156 },
        { "name": "煎饼",   "quantity": "1 个", "estimatedKcal": 280 }
      ]
    }
  ],
  "model": { "llm": "gpt-4o-2024-08-06" }
}
```

- `candidates`：每条对应客户端**确认后**要写盘的一个 meal 文件
- 客户端展示确认 UI，允许用户勾掉某天、改份量；用户点"全部确认"后逐日写入本地

### 4.3 `POST /api/advice/analyze`

综合分析。**以最近 2-3 次 bodyMetrics 变化为主轴**，结合同期的 workout 与 meal 给出校验和建议。

**Request** (`application/json`):

```json
{
  "bodyMetrics": [
    { "capturedAt": "2026-05-29T07:00:00Z", "structured": {...} },
    { "capturedAt": "2026-05-26T07:00:00Z", "structured": {...} },
    { "capturedAt": "2026-05-22T07:00:00Z", "structured": {...} }
  ],
  "workouts": [
    { "capturedAt": "...", "structured": {...} }
  ],
  "meals": [
    { "appliesToDate": "...", "mealType": "...", "items": [...] }
  ]
}
```

| 字段 | 类型 | 必需 | 说明 |
|---|---|---|---|
| `bodyMetrics` | array | ✅ | 最近 2-3 条体脂数据，按时间倒序 |
| `workouts` | array | ⭕ | 同期运动记录 |
| `meals` | array | ⭕ | 同期饮食记录 |

客户端负责挑选要上送的记录范围（典型策略：取最近 3 次体脂之间覆盖的时间窗内的所有运动和饮食）。

**Response 200**:

```json
{
  "summary": "近 3 次体脂数据显示体脂率从 19.1% 降至 18.4%，体重稳定，趋势良好。",
  "trendAnalysis": {
    "weightChangeKg": -0.2,
    "bodyFatChangePercent": -0.7,
    "verdict": "improving"
  },
  "validation": "运动量与饮食摄入基本匹配体脂改善趋势，无明显异常。",
  "suggestions": [
    "维持当前每周 3 次有氧 + 2 次力量的节奏",
    "早餐蛋白质摄入可再加 1 份（约 10g）"
  ],
  "warnings": [],
  "model": { "llm": "gpt-4o-2024-08-06" }
}
```

- `summary`：自然语言总结
- `trendAnalysis`：体脂趋势的结构化结果，便于客户端做图表
- `validation`：校验"运动/饮食是否与体脂变化匹配"
- `suggestions` / `warnings`：后续运动饮食建议与风险提示

### 4.4 `GET /api/health`

健康检查。

## 5. 截图结构化字段

设计原则：**只把"分析逻辑会读"的字段做成核心强类型字段，其余 OCR 出什么就保留在 `fields` 自由字典里**。这样既能保证趋势分析的稳定性，又不丢失截图里的丰富信息（配速、步频、代谢年龄、各品牌体脂秤的差异化指标等）。

`fields` 类型固定为 `Dictionary<string, string>`：OCR 识别出什么字面值就保留什么（"6.1 km" / "5'46\"/km" / "152"），不强行解析类型。分析时 LLM 把整个 `fields` 当上下文带进去。

核心字段允许 `null`：OCR 没识别到时不强填，客户端 UI 允许用户补充。

### 5.1 `workout`

```jsonc
{
  "occurredAt": "2026-05-29T10:32:00Z",   // 必需：分析按时间排序
  "category":   "跑步",                    // 自由字符串：跑步 / 骑行 / 游泳 / 力量 / Outdoor Run …
  "fields": {
    "距离":     "6.1 km",
    "时长":     "35:12",
    "配速":     "5'46\"/km",
    "卡路里":   "410 kcal",
    "平均心率": "152",
    "步频":     "178"
  }
}
```

### 5.2 `bodyMetrics`

```jsonc
{
  "measuredAt":       "2026-05-29T07:00:00Z",  // 必需：趋势分析按时间排序
  "weightKg":         68.4,    // 体重（核心）
  "bodyFatPercent":   18.2,    // 体脂率（核心）
  "skeletalMuscleKg": 30.5,    // 骨骼肌（核心）
  "visceralFatLevel": 6,       // 内脏脂肪等级（核心）
  "proteinPercent":   16.8,    // 蛋白质（核心）
  "bmi":              22.3,    // BMI（核心）
  "fields": {
    "水分率":   "58.5%",
    "BMR":      "1520 kcal",
    "代谢年龄": "26"
  }
}
```

### 5.3 `menu`

```jsonc
{
  "rawText": "宫保鸡丁 18\n番茄炒蛋 12\n米饭 2",   // OCR 原始文本，UI 直接展示
  "items":   ["宫保鸡丁", "番茄炒蛋", "米饭"]      // 可选：LLM 提取的菜名列表，给 meal 录入做候选
}
```

客户端 UI 对所有字段都允许编辑后再保存。

## 6. 客户端本地存储

### 6.1 目录布局

```text
%LocalAppData%\HealthyGuidance\
├── workout\
│   └── 2026-05-29\
│       ├── 20260529-103200-a1b2c3d4.png
│       └── 20260529-103200-a1b2c3d4.json
├── bodyMetrics\
│   └── 2026-05-29\
│       ├── 20260529-070000-e5f6g7h8.png
│       └── 20260529-070000-e5f6g7h8.json
├── menu\
│   └── 2026-05-29\
│       ├── 20260529-113000-m1n2o3p4.png
│       └── 20260529-113000-m1n2o3p4.json
├── meal\
│   └── 2026-05-25\
│       └── 20260525-080000-q5r6s7t8.json     ← 无图
└── advice\
    └── 20260529-150000-adv01.json
```

- 先分类、再按 `capturedAt` / `appliesToDate` 分日期目录
- 文件名格式 `<yyyyMMdd-HHmmss>-<guid8>`，文件名前缀就是事件时间
- 截图类记录 `.png` 与 `.json` 一一对应；`meal` 只有 `.json`

### 6.2 单条 JSON 文件格式

```json
{
  "id": "20260529-103200-a1b2c3d4",
  "kind": "workout",
  "capturedAt": "2026-05-29T10:32:00Z",
  "savedAt": "2026-05-29T10:35:00Z",
  "imageRelativePath": "workout/2026-05-29/20260529-103200-a1b2c3d4.png",
  "structured": { /* 用户确认后的字段，可能已被编辑 */ }
}
```

`meal` 类型额外有 `mealType` 字段，没有 `imageRelativePath`。

字段全部可编辑：用户后续修改后客户端直接覆盖写盘；改了 `capturedAt` 跨日期时移动文件到新目录。

### 6.3 检索

MVP 用扫目录：分析时按日期范围计算需要扫的目录集合，遍历各 `<kind>/<yyyy-MM-dd>/*.json`。

## 7. 服务端组件划分

| 项目 | 职责 |
|---|---|
| `HealthyGuidance.Domain` | 领域模型 |
| `HealthyGuidance.Contracts` | API DTO |
| `HealthyGuidance.Application` | 三个用例 + 接口（`IScreenshotRecognizer` / `IMealRecognizer` / `IAdvisor`） |
| `HealthyGuidance.Infrastructure` | Azure SDK 适配器（Document Intelligence + Azure OpenAI） |
| `HealthyGuidance.Api` | 三个 Controller + Health |

## 8. 不在本文档范围

- AI Prompt 的具体模板
- 客户端 UI 设计稿
- 多账号 / 多端同步
- 数据加密 / 敏感字段处理（后续 ADR）
