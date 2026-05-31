# CLAUDE.md

项目简介、详细架构与设计见 [README.md](README.md) 和 [docs/v1/](docs/v1/)。本文件只放新会话立刻需要的"地图 + 约定"。

## 仓库地图

- [clients/windows/HealthyGuidance.App/](clients/windows/HealthyGuidance.App/) — WinUI 3 (Unpackaged) + .NET 8 桌面端，包含 `Pages/`、`ViewModels/`、`Services/`、`MainWindow`、`DevPanelWindow`
- [clients/windows/HealthyGuidance.Core/](clients/windows/HealthyGuidance.Core/) — 类库：`AzureOpenAI/`（GPT-4o 调用 + 导入流程）、`Storage/`（记录/备注落盘 + Formatter）、`Reports/`、`Settings/`、`Prompts/`、`Schemas/`
- [clients/windows/HealthyGuidance.slnx](clients/windows/HealthyGuidance.slnx) — 新版 .slnx 解决方案文件
- [shared/prompts/](shared/prompts/)、[shared/schemas/](shared/schemas/) — 多端共享的 prompt 模板和 JSON Schema；**通过 csproj `<Content Link>` 复制到 App 输出目录**（[HealthyGuidance.App.csproj:31-34](clients/windows/HealthyGuidance.App/HealthyGuidance.App.csproj#L31-L34)），运行时从 `AppContext.BaseDirectory/shared` 读取
- [docs/v1/](docs/v1/) — 10 篇设计文档；[docs/decisions/](docs/decisions/) — ADR（历史记录，不回头改）

## 构建 / 运行

```bash
# 构建整套
dotnet build clients/windows/HealthyGuidance.slnx

# 跑 App（unpackaged 配置，无需 MSIX 打包）
dotnet run --project clients/windows/HealthyGuidance.App -- # 用 "Unpackaged" profile
```

VS 里有两个 launch profile：`(Unpackaged)`（日常）和 `(Package)`（MSIX 打包，目前没用到）。

## 运行时数据位置

所有用户数据在 `%LocalAppData%\HealthyGuidance\`（Windows：`C:\Users\<u>\AppData\Local\HealthyGuidance\`）：

| 子目录 | 内容 |
|---|---|
| `records/<yyyy-MM>/<id>_<kind>_<hash8>/` | 成功记录：`parsed.json` + 原图 |
| `failed/<yyyy-MM>/<id>/` | 失败记录：`error.json` + 原图，**attempts 数组追加不覆盖** |
| `notes/<yyyy-MM>.txt` | 饮食备注纯文本（按月一文件） |
| `reports/<yyyy-MM>/<id>.json` | 生成的报告 |
| `config/secrets.dat` | DPAPI 加密的 Azure API key |

## 关键约定

- **JSON 命名**：`JsonNamingPolicy.SnakeCaseLower`；时间用 ISO 8601 不带时区
- **RecordKind slug**：`"workout"` / `"body-metrics"`（[RecordModels.cs:9-22](clients/windows/HealthyGuidance.Core/Storage/RecordModels.cs#L9-L22)），目录名中段也是这两个
- **去重**：按图片 SHA256；重复导入返回 `DuplicateSkipped`
- **Schema $ref 内联**：Azure Structured Outputs 不支持跨文件 `$ref`，`SchemaLoader` 加载 `parse-result.json` 时手动把 `workout.json` / `body-metrics.json` 内联进去
- **Azure Structured Outputs**：所有 properties 必须列入 `required[]`，可选字段用 `"type": ["...", "null"]` 表达——改 schema 时务必同步更新 `required`
- **加新字段流程**：① 改 `shared/schemas/<x>.json` ② 改 `shared/prompts/parse.md` 表格 ③ 改 [RecordSchema.cs](clients/windows/HealthyGuidance.Core/Storage/RecordSchema.cs) 的 `WorkoutFields` / `BodyMetricsFields`（UI 显示用） ④ 视情况改 [data-model.md](docs/v1/data-model.md)

## 注意事项

- **DPAPI 仅限 Windows**：[SettingsStore.cs](clients/windows/HealthyGuidance.Core/Settings/SettingsStore.cs) 有 4 个 CA1416 警告（已知，不要"修"），跨平台移植需要换 keystore
- **doc vs code 已知差异**：[docs/v1/data-model.md](docs/v1/data-model.md) §5 已精简到 7 个体成分字段；其他文档目前与代码一致
- **`docs/decisions/0001-core-data-flow.md`** 是早期 ADR，里面的字段名（如 `BMR`）和当前不同，按惯例不动它
