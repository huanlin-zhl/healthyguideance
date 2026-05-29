# Backend — HealthyGuidance API

ASP.NET Core 8 Web API。**无状态**，仅负责接收客户端上传的图片，编排 Azure AI 服务调用，将结构化结果与建议返回客户端。不做任何数据持久化。

## 项目分层

| 项目 | 职责 |
|---|---|
| `HealthyGuidance.Api` | Web API 入口：Controllers、中间件、DI 配置、Swagger |
| `HealthyGuidance.Application` | 用例编排（IngestScreenshotUseCase 等）、DTO 转换 |
| `HealthyGuidance.Domain` | 领域模型：WorkoutSession、BodyMetrics、MealPlan、AdviceReport |
| `HealthyGuidance.Infrastructure` | Azure SDK 适配器：Document Intelligence、Azure OpenAI |
| `HealthyGuidance.Contracts` | 对外 API 的 Request/Response DTO（与 [shared/api-schema/](../shared/api-schema/) 对齐） |

依赖方向：`Api → Application → Domain`，`Infrastructure → Application（实现接口）`。

## 关键依赖（建好 csproj 后再装）

- `Azure.AI.DocumentIntelligence`（图片 → 结构化）
- `Azure.AI.OpenAI`（GPT-4o 解读与建议）
- `Swashbuckle.AspNetCore`（Swagger / OpenAPI 导出到 `shared/api-schema/`）
- `Serilog.AspNetCore`（结构化日志）

## 部署目标

Azure App Service（Linux, .NET 8）。配置通过 App Service Configuration 注入：

- `Azure:DocumentIntelligence:Endpoint`
- `Azure:DocumentIntelligence:ApiKey`
- `Azure:OpenAI:Endpoint`
- `Azure:OpenAI:Deployment`
- `Azure:OpenAI:ApiKey`

生产建议使用 Managed Identity 替代 ApiKey。

## 待办

- [ ] 初始化 .sln 与各 .csproj
- [ ] 定义 `IDocumentIntelligenceClient` / `IAdviceGenerator` 接口
- [ ] 实现 `POST /api/screenshots/analyze` 端点
- [ ] 导出 OpenAPI 到 `shared/api-schema/openapi.json`
