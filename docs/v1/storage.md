# 本地存储

- 主文档：[design.md](design.md)

## 1. 数据根目录

```text
%LocalAppData%\HealthyGuidance\
```

对应路径：`C:\Users\<用户名>\AppData\Local\HealthyGuidance\`

理由：符合 Windows 应用规范，备份工具能识别，无需用户额外配置。

## 2. 完整目录布局

```text
%LocalAppData%\HealthyGuidance\
├── records\                                    ← 解析成功的截图记录
│   └── 2026-05\
│       ├── 20260524-190100_workout_a1b2c3d4\
│       │   ├── screenshot.png
│       │   └── parsed.json
│       ├── 20260527-193500_workout_b2c3d4e5\
│       │   ├── screenshot.png
│       │   └── parsed.json
│       └── 20260529-072300_body-metrics_e5f6g7h8\
│           ├── screenshot.png
│           └── parsed.json
├── failed\                                     ← 解析失败的截图
│   └── 2026-05\
│       └── 20260530-120000_x9y8z7w6\
│           ├── screenshot.png
│           └── error.json
├── notes\                                      ← 饮食备注（按月聚合 txt）
│   ├── 2026-05.txt
│   └── 2026-06.txt
├── reports\                                    ← 分析报告
│   └── 2026-05\
│       └── 20260530-153000_r9s0t1u2.json
└── config\
    ├── settings.json                           ← 应用设置（明文）
    └── secrets.dat                             ← API key（DPAPI 加密）
```

## 3. 截图记录（records / failed）

### 3.1 子目录命名

**成功**：`<yyyyMMdd-HHmmss>_<kind>_<sha256前8位>`

- `kind` 取值：`workout` / `body-metrics`
- 例：`20260524-190100_workout_a1b2c3d4`

**失败**：`<yyyyMMdd-HHmmss>_<sha256前8位>`

- 失败时无 kind 段
- 例：`20260530-120000_x9y8z7w6`

### 3.2 时间戳来源

| 场景 | 时间戳 |
|---|---|
| 解析成功且 GPT 抽出事件时间 | 用事件时间（运动 `date_time` / 体成分 `measured_at`） |
| 解析成功但 GPT 没抽出时间 | 用导入时间，`parsed.json` 中标注 `timestamp_source: "import"` |
| 解析失败 | 用导入时间 |

### 3.3 子目录内文件

| 状态 | 文件 |
|---|---|
| 成功 | `screenshot.png` + `parsed.json` |
| 失败 | `screenshot.png` + `error.json` |

### 3.4 去重

**规则**：仅按图片 **SHA256** 全量哈希。命中已有记录即跳过，不报错、不覆盖。

理由：图片内容 hash 同时解决"重复传同一张"和"防止改名再传"两种场景，无需额外按时间戳兜底。

### 3.5 失败重试

- 用户在 UI 的失败列表里手动点"重试"才触发
- 重试成功 → 从 `failed/<yyyy-MM>/...` 移动到 `records/<yyyy-MM>/...`，按 3.1 成功规则重命名
- 重试失败 → 更新 `error.json` 中的 `last_attempt_at` 与错误信息，目录不动

## 4. 饮食备注（notes）

### 4.1 文件组织

- 按月聚合：`notes/<yyyy-MM>.txt`
- 跨月按"用户保存那一刻"归档：5/31 23:50 写"今天" → `2026-05.txt`；6/1 00:10 写"昨天" → `2026-06.txt`

### 4.2 文件格式

每条两行：第一行时间戳（到分钟），第二行原文。条目间空行分隔。

```text
2026-05-24 12:30
昨天中午吃了牛肉面，晚上沙拉

2026-05-27 08:05
今天早餐两个蛋一杯豆浆
```

### 4.3 写入策略

- 直接追加到月文件末尾，不维护内部排序
- 读取时再按时间戳排序
- 时间戳用**用户保存那一刻**，不调 LLM 解析相对时间

### 4.4 分析时如何使用

取时间窗口覆盖的所有月份 txt → 整段拼进 prompt → GPT 看到"X 时间点写的：YYY"，能自行推理相对时间词（"昨天"、"周三"）。

## 5. 分析报告（reports）

- 文件路径：`reports/<yyyy-MM>/<yyyyMMdd-HHmmss>_<8位hash>.json`
- 时间戳用报告生成时间
- hash 为随机短 hash，避免同一秒生成多份报告时冲突

报告 JSON 的字段结构在 [report.md](report.md) 定义（待补）。

## 6. 配置

```text
config\
├── settings.json       ← 明文：默认时间窗口、目标体重 / 体脂率等
└── secrets.dat         ← DPAPI 加密：API key
```

详见 [security.md](security.md)（待补）。

## 7. 索引与查询

MVP 不维护索引文件。查询按目录扫描：

- "过去 30 天所有运动" → 扫 `records/2026-04/` 与 `records/2026-05/`，按文件夹名过滤含 `_workout_` 的目录
- "过去 30 天所有备注" → 读 `notes/2026-04.txt` 与 `notes/2026-05.txt`

数据量预计每年几百条以内，目录扫描足够快。文件数显著增长后再考虑加 `index.json` 缓存。
