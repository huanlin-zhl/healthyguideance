# Shared

跨端共享资源。

## api-schema/

由后端 `HealthyGuidance.Api` 通过 Swashbuckle 在构建时导出 `openapi.json`，提交到此目录。各客户端用 openapi-generator / NSwag 生成强类型客户端代码，保证多端契约一致。

## sample-data/

每种截图场景准备 1-2 张样例图 + 一份期望的 JSON 输出，用于：
- 本地联调（不耗 Azure 配额）
- 单元测试 fixture
- 提示词 / Document Intelligence 模型调优参考

子目录：
- `workout/`：运动记录截图（Keep、Apple Health、Strava 等）
- `body-metrics/`：体脂秤 App 截图（华为运动健康、小米运动等）
- `meals/`：菜单 / 外卖订单截图
