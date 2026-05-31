# 安全与密钥

- 主文档：[design.md](design.md)
- 相关：[storage.md](storage.md)（`config/` 落盘位置）、[ui-flow.md](ui-flow.md)（首次启动与设置页）

## 1. 范围

本文档定义：

- **Azure OpenAI 调用凭证**的存储与使用
- 应用与外部服务的**传输安全**
- 日志、错误信息中的**敏感信息脱敏**
- 用户**截图原数据**的处理边界

不在本文档范围：

- 数据本身的加密（截图 / parsed.json / 备注 / 报告 **均不加密**，原因见 §7）

## 2. 凭证存储

### 2.1 要保护的字段

调用 Azure OpenAI 需要 3 项配置，**统一加密落盘**：

| 字段 | 示例 | 敏感度 |
|---|---|---|
| `api_key` | `<azure openai key>` | 高 |
| `endpoint` | `https://xxx.openai.azure.com` | 中（暴露 Azure 资源） |
| `deployment_name` | `gpt-4o` | 低 |

虽然 `deployment_name` 不敏感，统一加密简化逻辑，不再区分。

### 2.2 加密方式

**DPAPI**（`System.Security.Cryptography.ProtectedData`）：

- 按 `DataProtectionScope.CurrentUser` 加密
- 密钥由 Windows 管理，绑定当前用户账户
- 换电脑 / 换 Windows 账户后**无法解密** —— 用户需重新填写凭证

理由：
- 系统内置，无需引入额外密钥管理
- 文件形式落盘，匹配 `config/secrets.dat` 的设计
- 单用户桌面应用，无需更复杂的方案（如 Windows Credential Manager）

### 2.3 落盘格式

`config/secrets.dat`：DPAPI 加密后的**二进制文件**，内容为以下结构的 UTF-8 JSON 字节经 `ProtectedData.Protect()` 加密：

```jsonc
{
  "api_key":         "<明文>",
  "endpoint":        "<明文>",
  "deployment_name": "<明文>"
}
```

> 文件本身是二进制，不可直接编辑；用户改配置只能通过设置页 UI。

### 2.4 读写流程

**写入**（首次配置 / 设置页修改）：

```text
UI 收集 3 项明文
  → 序列化为 JSON
  → ProtectedData.Protect(jsonBytes, null, CurrentUser)
  → 写入 config/secrets.dat
  → 立即更新内存中的凭证（无需重启）
```

**读取**（应用启动）：

```text
读 config/secrets.dat
  → ProtectedData.Unprotect(bytes, null, CurrentUser)
  → 反序列化 JSON
  → 装入 SecretStore 单例，全局只读
```

### 2.5 内存生命周期

- 启动加载一次，**全程常驻内存**
- 设置页修改后**立即替换**内存值，无需重启
- 不做"读完即释放"的复杂管理（桌面单用户场景无意义）

### 2.6 文件缺失 / 解密失败

| 场景 | 处理 |
|---|---|
| 文件不存在 | 启动时跳"首次配置"对话框，强制填写后才能进主界面（见 [ui-flow.md §9](ui-flow.md)） |
| 文件存在但解密失败（如换了 Windows 账户） | 视同文件不存在，跳"首次配置"，并提示"原有凭证无法在当前账户下读取，请重新填写" |
| JSON 反序列化失败 | 同上 |

### 2.7 不做的事

- **不做** 备份机制 —— 用户自行从 Azure portal 重新获取 key
- **不做** 凭证有效性预校验 —— 保存即落盘；调用 Azure 失败时再由 UI 提示（见 [errors.md](errors.md) 待补）
- **不做** 多套凭证管理 —— 单用户单套，足够

## 3. 凭证在 UI 上的展示

### 3.1 显示规则

API Key 在设置页与首次配置页统一格式：**前 4 + `…` + 后 4**：

```
sk-12...wxyz
```

- 默认隐藏（密码框样式）
- 旁边一个"眼睛"图标 → 点击切换为完整明文显示
- 失焦自动恢复隐藏

Endpoint：**完整显示**（中等敏感，便于用户核对）

Deployment Name：完整显示

### 3.2 复制行为

- 设置页提供"复制"按钮 → 复制完整 Key 到剪贴板
- 复制后 30 秒内自动清空剪贴板（防止误粘贴到其他应用）

## 4. 传输安全

- Azure OpenAI SDK 全程 HTTPS，由 Azure SDK 保证
- 应用不做额外网络安全处理
- 不允许通过环境变量 / 命令行参数传入凭证（避免出现在进程列表或 shell 历史）

## 5. 日志与错误信息脱敏

### 5.1 强制规则

任何日志输出、异常堆栈、UI 错误提示中：

- **绝不**输出完整 API Key
- Endpoint 输出时脱敏为：`https://xxx...openai.azure.com`（保留 `https://` + `...` + 最后一段域名）
- Deployment Name 可完整输出

### 5.2 SecretStore 的对外接口

`HealthyGuidance.Core/Security/SecretStore.cs`（伪签名）：

```csharp
public sealed class SecretStore {
    public string ApiKey { get; }              // 仅 AzureOpenAI 调用层使用
    public string Endpoint { get; }            // 同上
    public string DeploymentName { get; }      // 同上
    public string MaskedEndpoint { get; }      // 日志/UI 使用
    public string MaskedApiKey { get; }        // UI 展示使用（前4后4）
}
```

约定：日志相关代码**只读 Masked 版本**；调用 Azure SDK 时才读完整版本。

## 6. 截图原数据

### 6.1 EXIF / 元数据

- 用户上传的 PNG 截图通常无 EXIF（手机截屏截图不含相机元信息）
- 应用**不主动清洗** EXIF
- 不主动**新增**任何元数据（不写水印、不写来源标签）

### 6.2 截图本身的留存

- 落盘到 `records/<id>/screenshot.png`，原样保存
- 不上传到任何第三方（除 Azure OpenAI 调用本身）
- Azure OpenAI 调用为合规云端推理；如有疑虑可参考 Azure 数据使用政策（不在应用控制范围）

## 7. 数据本身不加密的理由

`records/`、`failed/`、`notes/`、`reports/` 全部**明文**存储于 `%LocalAppData%`：

1. 其他进程若能读 `%LocalAppData%`，意味着当前 Windows 账户已被攻陷 —— 加密无意义
2. 加密会阻碍用户用资源管理器直接备份 / 检查
3. 微软自家应用（Photos、Outlook 缓存、Edge 历史等）也均不加密
4. 一致性：与"数据归用户"原则相符

如未来涉及敏感健康指征（如医疗诊断），再单独评估。

## 8. 未来可能的扩展

不在 P0 实现，仅作记录：

- 数据导出/导入（含凭证）时的临时加密容器
- 多设备同步场景下的端到端加密
- 凭证的有效期检查与自动刷新
