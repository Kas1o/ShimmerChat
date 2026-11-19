# 文生图（多端点：ComfyUI / A1111）

> 本文描述内置的文生图插件：多配置模型、ComfyUI 与 A1111 两种 API、工具集节点、生成图像的落盘位置以及工具返回格式。

---

## 1. 能力概览

| 能力 | 说明 |
|------|------|
| 多配置 | `Profiles` 保存任意多份具名配置，每份配置 = 一个 API 端点 + 一种生成方式 + 一组生成默认值 |
| API 协议 | ComfyUI（`/prompt` → `/history/{id}` → `/view`）与 A1111 风格（`/sdapi/v1/txt2img`） |
| ComfyUI 工作流 | 默认内置工作流，或用户粘贴的 **API 格式** 自定义工作流（`{{参数名}}` 占位符） |
| A1111 工作流 | 标准 txt2img 参数，不支持自定义工作流 |
| 工具暴露 | 每份配置暴露为独立工具 `generate_image_{id}`；Id 为 `default` 时为 `generate_image` |
| 节点选择 | 工具集节点用 `image_generation_configs` 下拉选项选择某一分配置；留空表示全部启用的配置 |
| 图像存储 | 生成结果始终写入 `UserUploadImage` 目录，不受「上传图像存储方式」的 Base64 设置影响 |
| 返回格式 | 工具返回 `/userimages/...` 的 Markdown 图片语法与裸 URL；同时把生成图附加到 tool_result 消息，让模型在下一轮真正“看到”图像 |
| 取消与失败 | 所有 HTTP 请求响应取消令牌；超时、服务错误、节点校验错误、落盘失败都返回明确错误码 |

依赖：

- 至少一个可访问的 ComfyUI 或 A1111 服务端点。
- ComfyUI 默认工作流只使用核心节点；自定义工作流可以包含任意已安装节点的 API 格式 JSON。
- A1111 需要开启 API（`--api`），并安装至少一个 checkpoint（或使用服务当前模型）。

---

## 2. 配置模型与迁移

配置保存在 KVData：`ImageGeneration/Config`。

```jsonc
{
  "Version": 2,
  "Profiles": [
    {
      "Id": "default",
      "Name": "ComfyUI local",
      "Enabled": true,
      "Provider": "ComfyUi",          // 或 "Automatic1111"
      "BaseUrl": "http://127.0.0.1:8188",
      "ApiKey": "",
      "TimeoutSeconds": 30,
      "MaxWaitSeconds": 300,
      "PollIntervalMilliseconds": 1000,
      "WorkflowMode": "Default",      // 或 "Custom"（仅 ComfyUI）
      "DefaultWorkflow": { "Model": "", "Width": 512, "Height": 512, "...": "..." },
      "CustomWorkflowJson": "",
      "CustomWorkflowParameters": []
    }
  ]
}
```

- `Version` 当前为 `2`。版本 `0/1` 是旧结构（全局 `ComfyUiBaseUrl` + 单个 `DefaultWorkflow` + `CustomWorkflows` 列表）。
- 读取旧结构时在内存中懒迁移：旧默认工作流变成 Id 为 `default` 的 ComfyUI 配置，旧自定义工作流各自变成一份 `WorkflowMode = Custom` 的配置；下一次保存时写成新结构。
- 迁移幂等；旧 Id 与工具名保持不变（包括 `generate_image` 与 `generate_image_{id}`）。
- 若旧自定义工作流恰好使用了 `default` 这个 Id，默认配置会改名为 `default_comfy`，避免冲突。
- 配置损坏或取值非法时，加载返回明确错误，不静默回退成默认值。设置面板会显示错误并允许用户显式恢复默认值。

### 2.1 校验规则

- 配置 `Id`：1-40 个字母、数字、下划线或连字符，且不可重复；`Name` 不可为空且不可重名。
- `BaseUrl`：绝对 http(s) URL。
- `TimeoutSeconds`：1 ~ 600；`MaxWaitSeconds`：1 ~ 3600；`PollIntervalMilliseconds`：100 ~ 60000。
- `Provider` 必须是 `ComfyUi` 或 `Automatic1111`。
- `WorkflowMode = Custom` 只允许 ComfyUI。
- 生成默认值：宽高 64 ~ 8192 且为 8 的倍数；步数 1 ~ 150；CFG 0 ~ 100；批量 1 ~ 8。
- ComfyUI 必须填写采样器与调度器；A1111 必须填写采样器，调度器字段不参与请求。
- `Model` 允许为空：ComfyUI 默认工作流会在工具集节点处显式失败；A1111 表示使用服务当前模型。
- 启用的自定义工作流会校验：占位符与参数声明一一对应、参数名合法、非必填参数默认值可解析、模板能渲染为 JSON 对象。
- 停用的配置视为草稿，跳过自定义工作流模板与参数校验。

---

## 3. 设置面板

面板位置：设置 → 文生图（`panel.image_generation`）。

界面采用「左侧配置列表 + 右侧编辑器」：

- 左侧列出所有配置，显示协议、工作流模式、地址与启用状态；窄屏切换为二级页。
- 「新建配置」创建一份 ComfyUI 默认配置；「复制配置」生成一份独立副本。
- 编辑器分区：基础信息（名称 / Id / 说明 / 协议）、连接（地址、凭据、超时）、工作流（ComfyUI 模式选择）、自定义工作流（仅自定义模式显示：JSON 与参数声明）、生成参数（默认工作流 / A1111 显示；ComfyUI 自定义工作流模式下隐藏，因为该组不参与自定义工作流生成）。
- 删除配置需要二次确认，且只有保存后才写入存储。
- 「读取模型与采样器」按当前协议调用：
  - ComfyUI：`/object_info/CheckpointLoaderSimple` 与 `/object_info/KSampler`。
  - A1111：`/sdapi/v1/sd-models` 与 `/sdapi/v1/samplers`。
  读取结果只填充下拉候选，失败不影响手动填写，也不会自动保存。
- 底部「保存配置 / 恢复默认值」；任何校验失败都会显示具体原因。

### 3.1 公共字段

| 字段 | 说明 |
|------|------|
| `Id` | 持久化标识，同时决定工具名 |
| `Name` | 面板与工具描述中的显示名 |
| `Description` | 附加到工具描述的补充说明 |
| `Enabled` | 只有启用的配置才会被工具集节点暴露 |
| `Provider` | `ComfyUi` / `Automatic1111` |
| `BaseUrl` / `ApiKey` | 端点地址与可选凭据 |
| `TimeoutSeconds` | 单次 HTTP 请求超时 |
| `MaxWaitSeconds` | 从提交到拿到结果的总体等待上限 |
| `PollIntervalMilliseconds` | ComfyUI 轮询 `/history` 的间隔；A1111 忽略 |

`ApiKey` 含冒号时按 HTTP Basic（`user:password`）发送，否则按 Bearer Token 发送；留空不发送 `Authorization` 头。

### 3.2 生成默认值

两种协议共用 `DefaultWorkflow`：

| 字段 | 说明 |
|------|------|
| `Model` | ComfyUI 为 `CheckpointLoaderSimple.ckpt_name`；A1111 为 `sd_model_checkpoint` |
| `Width` / `Height` | 64 ~ 8192，且必须是 8 的倍数 |
| `Steps` | 1 ~ 150 |
| `CfgScale` | 0 ~ 100 |
| `Sampler` | ComfyUI 例如 `euler`；A1111 例如 `Euler a` |
| `Scheduler` | 仅 ComfyUI 使用 |
| `NegativePrompt` | 工具调用未传 `negative_prompt` 时使用 |
| `BatchSize` | 1 ~ 8 |

这些值只是工具参数的默认值，模型可以在单次调用中覆盖。

---

## 4. 工具集节点

节点：**文生图工具集**（`node.image_generation_tool_set`，分类 `Tool` / `Image Generation`）。

属性：

| 属性 | 默认 | 作用 |
|------|------|------|
| `ConfigurationId` | 空（全部） | 留空时把设置面板中所有 `Enabled = true` 的配置加入工具列表；选择某个配置时只添加该配置。选项来自 `OptionsKey = image_generation_configs` |

执行期行为：

- 配置不合法 → 节点失败（`DATA_MISSING`），消息提示检查设置面板。
- 当前会话没有 `IImageAttachmentService` → 节点失败，不添加必然无法落盘的工具。
- 没有任何启用的配置 → 节点成功但不添加工具，并写一条 DebugOutput。
- 指定了配置但不存在 → 节点失败（`CONFIG_NOT_FOUND`），提示重新选择。
- 指定的配置已停用 → 节点失败（`CONFIG_NOT_FOUND`），不静默跳过。
- 选中的 ComfyUI 默认工作流配置没有模型 → 节点失败（`CONFIG_NOT_FOUND`），消息带上配置名；A1111 允许模型为空。
- 工具名冲突 → 节点失败并指出冲突的工具名。

工具名：

```
Id = default       → generate_image
其它 Id            → generate_image_{Id}
```

> 迁移说明：旧版工具集节点的 `EnableDefaultWorkflow` / `EnableCustomWorkflows` / `CustomWorkflowId`
> 属性不再读取。旧节点仍可正常反序列化，升级后默认按「全部已启用的配置」工作；
> 需要限制工具集合时，请在节点上重新选择 `ConfigurationId`（或在设置面板停用不需要的配置）。

---

## 5. 工具参数

所有工具都会把生成结果保存到 UserUploadImage，并返回 `/userimages/...` 链接与 `ToolResult.Images`。

### 5.1 ComfyUI 默认工作流 / A1111

| 参数 | 类型 | ComfyUI | A1111 | 说明 |
|------|------|---------|-------|------|
| `prompt` | string | ✓ | ✓ | 正向提示词，必填且非空 |
| `negative_prompt` | string | ✓ | ✓ | 缺省使用配置默认值 |
| `model` | string | ✓ | ✓ | 缺省使用配置默认值；A1111 可为空 |
| `width` / `height` | number | ✓ | ✓ | 缺省使用配置默认值 |
| `steps` | number | ✓ | ✓ | 缺省使用配置默认值 |
| `cfg` | number | ✓ | ✓ | 缺省使用配置默认值 |
| `sampler` | string | ✓ | ✓ | 缺省使用配置默认值 |
| `scheduler` | string | ✓ | ✗ | 仅 ComfyUI 暴露 |
| `seed` | number | ✓ | ✓ | 省略时每次随机生成 |
| `batch_size` | number | ✓ | ✓ | 1 ~ 8，缺省使用配置默认值 |

工具会拒绝未声明的参数；参数范围、宽高是否为 8 的倍数都在执行期再次校验。

### 5.2 ComfyUI 自定义工作流

工具形参完全来自该配置的 `CustomWorkflowParameters`。参数声明与模板占位符必须一一对应。

---

## 6. ComfyUI 协议

调用顺序：

```
POST /prompt                 → prompt_id（node_errors / error 立即失败）
GET  /history/{prompt_id}    → 轮询到 status.completed / status_str=success
GET  /view?filename=...      → 下载所有输出图像
```

- 总等待上限由 `MaxWaitSeconds` 控制；单请求超时由 `TimeoutSeconds` 控制。
- 自定义工作流必须是 **API 格式**（`prompt` 字段里的那种 `{ "节点id": { "class_type": ..., "inputs": ... } }`），不是 UI 保存的 workflow 格式。
- 默认工作流节点编号固定为：

```
3 KSampler
4 CheckpointLoaderSimple
5 EmptyLatentImage
6 CLIPTextEncode (positive)
7 CLIPTextEncode (negative)
8 VAEDecode
9 SaveImage
```

### 6.1 占位符语义

- 语法只有一种：`{{参数名}}`。
- 位于 JSON 字符串内（`"{{prompt}}"`）→ 参数值按字符串内容做 JSON 转义后插入。
- 位于 JSON 字符串外（`{{steps}}`）→ 参数值按 JSON 字面量插入；字符串类型会带上引号。
- 模板中出现的每个占位符都必须有同名参数声明；声明的每个参数也必须至少出现在模板中一次。
- 工作流必须最终渲染为 JSON **对象**。
- 模板中的 `{{` 是保留语法；提示词内容由模型在调用时传入，替换发生在模板渲染阶段，因此提示词里的花括号不会被再次解析。

### 6.2 参数声明

| 字段 | 说明 |
|------|------|
| `Name` | 参数名 / 占位符名；字母或下划线开头，只含字母、数字、下划线 |
| `Type` | `String` / `Integer` / `Number` / `Boolean` / `Enum` |
| `Description` | 给模型看的参数说明 |
| `Required` | 必填时调用必须提供 |
| `DefaultValue` | 非必填参数的默认值，必须能按类型解析 |
| `EnumValues` | 仅 `Enum` 使用，逗号分隔；匹配时忽略大小写并归一化为声明值 |
| `Min` / `Max` | 仅数值类型使用，闭区间 |

---

## 7. A1111 协议

```
POST /sdapi/v1/txt2img
```

请求体由工具参数映射：

| 配置 / 工具参数 | A1111 字段 |
|-----------------|-----------|
| `prompt` | `prompt` |
| `negative_prompt` | `negative_prompt` |
| `model` | `override_settings.sd_model_checkpoint`（为空时省略） |
| `width` / `height` | `width` / `height` |
| `steps` | `steps` |
| `cfg` | `cfg_scale` |
| `sampler` | `sampler_name` |
| `seed` | `seed` |
| `batch_size` | `batch_size`，同时 `n_iter = 1` |

实现还会设置 `do_not_save_samples = true` 与 `do_not_save_grid = true`，避免服务端输出目录留下副本。

响应 `images` 数组中的每一项按 base64 解码；兼容带 `data:image/...;base64,` 前缀的返回。图像类型由实际文件头识别，不接受伪造的 MIME。数组为空、base64 非法或不是受支持图像时显式失败。

连接探测：

```
GET /sdapi/v1/sd-models   → model_name / title / filename 作为模型候选
GET /sdapi/v1/samplers    → name 作为采样器候选
```

---

## 8. 图像落盘与返回格式

### 8.1 落盘

生成结果通过 `IImageAttachmentService.SaveGeneratedAsync` 保存：

- 无论上传图像存储方式是什么，都写入 `UserUploadImage` 目录，并返回只含文件名的 `MessageImage`。
- 文件名为新 Guid + 按 MIME 决定的扩展名。
- 空内容会被删除并显式报错。
- 读取时通过 `GetDisplayUrl` 得到 `/userimages/{文件名}`；Program.cs 已把该目录挂载为静态资源。

### 8.2 工具返回文本

成功时返回类似：

```
Generated 1 image(s) (A1111 shared).
Seed: 123

The generated image(s) are attached to this tool result so you can inspect them.
Embed the following Markdown snippet(s) in your reply so the user can see the image(s):
![generated image 1](/userimages/xxxxxxxx.png)
Image URL: /userimages/xxxxxxxx.png
```

模型把 Markdown 片段放进回复后，对话界面的 Markdown 渲染会把相对路径解析为当前服务的 `/userimages/...`。

### 8.3 让模型真正看到图像

仅返回 Markdown 链接只能让**用户**看到图，模型本身并不能从链接读取图像。因此工具成功时还会：

1. 把保存后的 `MessageImage`（文件引用）放进 `ToolResult.Images`；
2. `PostToolCallNode` 将这些引用写到 tool_result 消息的 `Message.Images`，与文本一起落库；
3. 下一轮 `AppendChatMessagesNode` 把 `Message.Images` 解析成 `ChatImage` 内容块，OpenAI 兼容客户端会把它作为 `role=tool` 消息的 `image_url` 内容块发给模型。

这样模型既能“看到”图像，也能在回复里嵌入稳定的 `/userimages/...` 链接。生成图不会自动附加到助手回复消息；回复中的图片仍由模型复制返回的 Markdown 片段决定。图像文件的生命周期由上传目录管理。

---

## 9. 错误与取消

| 场景 | 工具结果 |
|------|----------|
| 配置 JSON 损坏 / 校验失败 | `TOOL_CONFIG_INVALID`，Details 带原始错误 |
| 参数 JSON 非法 / 缺少必填 / 未知参数 / 超范围 | `TOOL_INVALID_ARGUMENTS` |
| ComfyUI 默认工作流未配置模型 | `TOOL_INVALID_ARGUMENTS`，提示到设置面板选择模型 |
| 自定义工作流渲染失败 | `TOOL_CONFIG_INVALID` |
| ComfyUI 队列 / 执行 / 下载失败 | `TOOL_SERVICE_ERROR`，Details 带 ComfyUI 原始信息 |
| A1111 HTTP / 解码 / 图像格式失败 | `TOOL_SERVICE_ERROR`，Details 带服务原始信息 |
| 保存图像失败 | `TOOL_OUTPUT_WRITE_FAILED` |
| 用户取消生成 | `OperationCanceledException` 向上传播 |
| 内部等待超时 | `TOOL_SERVICE_ERROR`，明确说明等待时长 |

用户取消时不会把取消伪装成超时错误。

---

## 10. 相关代码

| 文件 | 职责 |
|------|------|
| `ShimmerChatBuiltin/ImageGeneration/ImageGenerationConfig.cs` | 配置模型、版本迁移、KV 读写、集中校验 |
| `ShimmerChatBuiltin/ImageGeneration/ImageApiProviderCatalog.cs` | 两种 API 协议的能力目录（唯一来源） |
| `ShimmerChatBuiltin/ImageGeneration/WorkflowTemplateEngine.cs` | `{{参数}}` 扫描、绑定与渲染 |
| `ShimmerChatBuiltin/ImageGeneration/DefaultImageWorkflow.cs` | 默认 ComfyUI 工作流 JSON 构建 |
| `ShimmerChatBuiltin/ImageGeneration/ComfyUiClient.cs` | ComfyUI 协议客户端、连接探测、共享 HttpClient |
| `ShimmerChatBuiltin/ImageGeneration/A1111Client.cs` | A1111 txt2img 客户端、base64 解码、连接探测 |
| `ShimmerChatBuiltin/ImageGeneration/ImageGenerationToolV2.cs` | 按配置生成工具；返回链接 + `ToolResult.Images` |
| `ShimmerChatLib/Generation/ToolResult.cs` | 工具结果的可选图像附件 |
| `ShimmerChatLib/Generation/PostChat.cs` | 追加消息时原子写入图像引用 |
| `ShimmerChatBuiltin/NodeBasic/PostGeneration/PostToolCallNode.cs` | 把工具结果图像写入 tool_result 消息 |
| `ShimmerChatBuiltin/ImageGeneration/ImageGenerationToolSetNode.cs` | 工具集节点 |
| `ShimmerChatBuiltin/Options/ImageGenerationConfigOptionsProvider.cs` | 节点属性 `image_generation_configs` 的选项来源 |
| `ShimmerChatBuiltin/ImageGeneration/ImageGenerationPanel.razor` | 多配置设置面板 |
| `ShimmerChat/Singletons/ImageAttachmentService.cs` | `SaveGeneratedAsync` 文件落盘实现 |

测试：

- `ShimmerChatBuiltin.Tests/ImageGeneration/ImageGenerationConfigTests.cs`：默认配置、字段校验、旧结构迁移、往返序列化。
- `ShimmerChatBuiltin.Tests/ImageGeneration/ComfyUiClientTests.cs`：ComfyUI 协议与失败路径。
- `ShimmerChatBuiltin.Tests/ImageGeneration/A1111ClientTests.cs`：A1111 请求映射、base64/Data URI、错误与探测。
- `ShimmerChatBuiltin.Tests/ImageGeneration/ImageGenerationToolTests.cs`：默认 / 自定义 / A1111 工具、失败路径与取消。
- `ShimmerChatBuiltin.Tests/ImageGeneration/ImageGenerationToolSetNodeTests.cs`：多配置暴露、选择、停用与模型缺失。
- `ShimmerChat.Tests/ImageAttachmentServiceTests.cs`：生成结果始终落盘。
