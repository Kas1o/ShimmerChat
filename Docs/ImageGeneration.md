# 文生图（ComfyUI）

> 本文描述内置的文生图插件：默认工作流、自定义工作流、工具集节点、生成图像的落盘位置以及工具返回格式。
> 当前仅支持 ComfyUI API。

---

## 1. 能力概览

| 能力 | 说明 |
|------|------|
| 默认工作流 | 内置 `CheckpointLoaderSimple → CLIPTextEncode → EmptyLatentImage → KSampler → VAEDecode → SaveImage`，工具参数可覆盖模型、宽高、步数、CFG、采样器、调度器、seed、批量张数 |
| 自定义工作流 | 在设置面板粘贴 ComfyUI **API 格式**工作流 JSON，自行声明工具参数，用 `{{参数名}}` 占位符替换 |
| 工具暴露 | 每个自定义工作流暴露为独立工具 `generate_image_{id}`；默认工作流为 `generate_image` |
| 图像存储 | 生成结果始终写入 `UserUploadImage` 目录，不受「上传图像存储方式」的 Base64 设置影响 |
| 返回格式 | 工具返回 `/userimages/...` 的 Markdown 图片语法与裸 URL，供模型嵌入回复；同时把生成图附加到 tool_result 消息，让模型在下一轮真正“看到”图像 |
| 取消与失败 | 所有 HTTP 请求响应取消令牌；超时、服务错误、节点校验错误、落盘失败都返回明确错误码 |

依赖：

- ComfyUI 服务已启动，且已安装至少一个 checkpoint。
- 默认工作流只使用 ComfyUI 核心节点；自定义工作流可以包含任意已安装节点的 API 格式 JSON。
- 设置面板中配置 ComfyUI 地址（默认 `http://127.0.0.1:8188`）。

---

## 2. 设置面板

面板位置：设置 → 文生图（`panel.image_generation`）。

配置保存在 KVData：`ImageGeneration/Config`。读取时如果 JSON 损坏或取值非法，加载会返回明确错误，不会静默回退成默认值。

### 2.1 连接参数

| 字段 | 范围 | 说明 |
|------|------|------|
| `ComfyUiBaseUrl` | 绝对 http(s) URL | 例如 `http://127.0.0.1:8188` |
| `TimeoutSeconds` | 1 ~ 600 | 单次 HTTP 请求超时 |
| `MaxWaitSeconds` | 1 ~ 3600 | 从提交任务到取回图像的总等待上限 |
| `PollIntervalMilliseconds` | 100 ~ 60000 | 轮询 `/history/{id}` 的间隔 |

「读取模型与采样器」按钮会请求 ComfyUI 的 `/object_info/CheckpointLoaderSimple` 与 `/object_info/KSampler`，把可选项填入下拉候选。读取失败只影响候选列表，不会阻止保存，也不会自动修改已有配置。

### 2.2 默认工作流

`DefaultWorkflow` 中的值是工具参数的默认值：

| 字段 | 说明 |
|------|------|
| `Model` | `CheckpointLoaderSimple.ckpt_name`。允许为空保存，但执行默认工作流时会明确失败 |
| `Width` / `Height` | 64 ~ 8192，且必须是 8 的倍数 |
| `Steps` | 1 ~ 150 |
| `CfgScale` | 0 ~ 100 |
| `Sampler` | 例如 `euler` / `dpmpp_2m` |
| `Scheduler` | 例如 `normal` / `karras` |
| `NegativePrompt` | 工具调用未传 `negative_prompt` 时使用 |
| `BatchSize` | 1 ~ 8 |

---

## 3. 工具集节点

节点：**文生图工具集**（`node.image_generation_tool_set`，分类 `Tool` / `Image Generation`）。

属性：

| 属性 | 默认 | 作用 |
|------|------|------|
| 启用默认工作流工具 | 开 | 添加 `generate_image` |
| 启用自定义工作流工具 | 开 | 为设置面板中每个 `Enabled = true` 的自定义工作流添加 `generate_image_{id}` |

执行期行为：

- 配置不合法 → 节点失败（`DATA_MISSING`），消息提示检查设置面板。
- 启用默认工作流但未选择模型 → 节点失败（`CONFIG_NOT_FOUND`），提示选择模型或关闭默认工具。
- 当前会话没有 `IImageAttachmentService` → 节点失败，不添加必然无法落盘的工具。
- 没有任何启用的自定义工作流 → 只添加默认工具，并写一条 DebugOutput。
- 工具名冲突 → 节点失败并指出冲突的工具名。

---

## 4. 默认工作流工具

工具名：`generate_image`。

工具参数全部可选（`prompt` 必填）：

| 参数 | 类型 | 说明 |
|------|------|------|
| `prompt` | string | 正向提示词，必填且非空 |
| `negative_prompt` | string | 负向提示词，缺省使用面板默认值 |
| `model` | string | checkpoint 名，缺省使用面板默认值 |
| `width` / `height` | number | 缺省使用面板默认值 |
| `steps` | number | 缺省使用面板默认值 |
| `cfg` | number | 缺省使用面板默认值 |
| `sampler` / `scheduler` | string | 缺省使用面板默认值 |
| `seed` | number | 省略时每次随机生成 |
| `batch_size` | number | 1 ~ 8，缺省使用面板默认值 |

工具会拒绝未声明的参数，参数范围 / 宽高是否为 8 的倍数都在执行期再次校验。

内部工作流节点编号固定为：

```
3 KSampler
4 CheckpointLoaderSimple
5 EmptyLatentImage
6 CLIPTextEncode (positive)
7 CLIPTextEncode (negative)
8 VAEDecode
9 SaveImage
```

---

## 5. 自定义工作流

### 5.1 工作流 JSON

必须是 ComfyUI **API 格式**（`prompt` 字段里的那种 `{ "节点id": { "class_type": ..., "inputs": ... } }`），不是 UI 保存的 workflow 格式。

把需要由模型控制的字段写成占位符：

```json
{
  "3": {
    "class_type": "KSampler",
    "inputs": {
      "seed": {{seed}},
      "steps": {{steps}},
      "cfg": {{cfg}},
      "sampler_name": "{{sampler}}",
      "scheduler": "{{scheduler}}",
      "denoise": 1.0,
      "model": ["4", 0],
      "positive": ["6", 0],
      "negative": ["7", 0],
      "latent_image": ["5", 0]
    }
  },
  "4": {
    "class_type": "CheckpointLoaderSimple",
    "inputs": { "ckpt_name": "{{model}}" }
  },
  "5": {
    "class_type": "EmptyLatentImage",
    "inputs": { "width": {{width}}, "height": {{height}}, "batch_size": 1 }
  },
  "6": {
    "class_type": "CLIPTextEncode",
    "inputs": { "text": "{{prompt}}", "clip": ["4", 1] }
  },
  "7": {
    "class_type": "CLIPTextEncode",
    "inputs": { "text": "{{negative_prompt}}", "clip": ["4", 1] }
  },
  "8": {
    "class_type": "VAEDecode",
    "inputs": { "samples": ["3", 0], "vae": ["4", 2] }
  },
  "9": {
    "class_type": "SaveImage",
    "inputs": { "filename_prefix": "ShimmerChat", "images": ["8", 0] }
  }
}
```

### 5.2 占位符语义

- 语法只有一种：`{{参数名}}`。
- 位于 JSON 字符串内（`"{{prompt}}"`）→ 参数值按字符串内容做 JSON 转义后插入。
- 位于 JSON 字符串外（`{{steps}}`）→ 参数值按 JSON 字面量插入；字符串类型会带上引号。
- 模板中出现的每个占位符都必须有同名参数声明；声明的每个参数也必须至少出现在模板中一次。
- 工作流必须最终渲染为 JSON **对象**。
- 模板中的 `{{` 是保留语法；提示词内容由模型在调用时传入，替换发生在模板渲染阶段，因此提示词里的花括号不会被再次解析。

### 5.3 参数声明

每个自定义工作流有独立参数表。参数会成为工具定义的一部分，模型按 JSON 参数调用。

| 字段 | 说明 |
|------|------|
| `Name` | 参数名 / 占位符名；字母或下划线开头，只含字母、数字、下划线 |
| `Type` | `String` / `Integer` / `Number` / `Boolean` / `Enum` |
| `Description` | 给模型看的参数说明 |
| `Required` | 必填时调用必须提供 |
| `DefaultValue` | 非必填参数的默认值，必须能按类型解析 |
| `EnumValues` | 仅 `Enum` 使用，逗号分隔；匹配时忽略大小写并归一化为声明值 |
| `Min` / `Max` | 仅数值类型使用，闭区间 |

校验规则：

- 参数名不可重复。
- `Integer` / `Number` 的默认值、`Min` / `Max` 必须可解析且 `Min <= Max`。
- `Enum` 至少一个值，不能有空值或大小写不敏感的重复值。
- 非必填参数的默认值必须能按类型解析（`String` 允许空串）。
- 停用（`Enabled = false`）的工作流允许是未完成草稿，不做模板与参数校验；启用时才校验。

### 5.4 工具名

自定义工作流 `Id` 会直接拼进工具名：

```
generate_image_{Id}
```

`Id` 限定为 1-40 个字母、数字、下划线或连字符。保存时会检查 Id 与名称唯一。

---

## 6. 图像落盘与返回格式

### 6.1 落盘

生成结果通过 `IImageAttachmentService.SaveGeneratedAsync` 保存：

- 无论上传图像存储方式是什么，都写入 `UserUploadImage` 目录，并返回只含文件名的 `MessageImage`。
- 文件名为新 Guid + 按 MIME 决定的扩展名。
- 空内容会被删除并显式报错。
- 读取时通过 `GetDisplayUrl` 得到 `/userimages/{文件名}`；Program.cs 已把该目录挂载为静态资源。

### 6.2 工具返回文本

成功时返回类似：

```
Generated 1 image(s) with ComfyUI (default workflow).
Seed: 123

Embed the following Markdown snippet(s) in your reply so the user can see the image(s):
![generated image 1](/userimages/xxxxxxxx.png)
Image URL: /userimages/xxxxxxxx.png
```

模型把 Markdown 片段放进回复后，对话界面的 Markdown 渲染会把相对路径解析为当前服务的 `/userimages/...`。

### 6.3 让模型真正看到图像

仅返回 Markdown 链接只能让**用户**看到图，模型本身并不能从链接读取图像。因此工具成功时还会：

1. 把保存后的 `MessageImage`（文件引用）放进 `ToolResult.Images`；
2. `PostToolCallNode` 将这些引用写到 tool_result 消息的 `Message.Images`，与文本一起落库；
3. 下一轮 `AppendChatMessagesNode` 把 `Message.Images` 解析成 `ChatImage` 内容块，OpenAI 兼容客户端会把它作为 `role=tool` 消息的 `image_url` 内容块发给模型。

这样模型既能“看到”图像，也能在回复里嵌入稳定的 `/userimages/...` 链接。生成图不会自动附加到助手回复消息；回复中的图片仍由模型复制返回的 Markdown 片段决定。图像文件的生命周期由上传目录管理。

---

## 7. 错误与取消

| 场景 | 工具结果 |
|------|----------|
| 配置 JSON 损坏 / 校验失败 | `TOOL_CONFIG_INVALID`，Details 带原始错误 |
| 参数 JSON 非法 / 缺少必填 / 未知参数 / 超范围 | `TOOL_INVALID_ARGUMENTS` |
| 未配置模型 | `TOOL_INVALID_ARGUMENTS`，提示到设置面板选择模型 |
| ComfyUI 队列 / 执行 / 下载失败 | `TOOL_SERVICE_ERROR`，Details 带 ComfyUI 原始信息 |
| 保存图像失败 | `TOOL_OUTPUT_WRITE_FAILED` |
| 用户取消生成 | `OperationCanceledException` 向上传播 |
| 内部等待超时 | `TOOL_SERVICE_ERROR`，明确说明等待时长 |

ComfyUI 调用顺序：

```
POST /prompt                 → prompt_id（node_errors / error 立即失败）
GET  /history/{prompt_id}    → 轮询到 status.completed / status_str=success
GET  /view?filename=...      → 下载所有输出图像
```

总等待上限由 `MaxWaitSeconds` 控制；单请求超时由 `TimeoutSeconds` 控制。用户取消时不会把取消伪装成超时错误。

---

## 8. 相关代码

| 文件 | 职责 |
|------|------|
| `ShimmerChatBuiltin/ImageGeneration/ImageGenerationConfig.cs` | 配置模型、KV 读写、集中校验 |
| `ShimmerChatBuiltin/ImageGeneration/WorkflowTemplateEngine.cs` | `{{参数}}` 扫描、绑定与渲染 |
| `ShimmerChatBuiltin/ImageGeneration/DefaultImageWorkflow.cs` | 默认工作流 JSON 构建 |
| `ShimmerChatBuiltin/ImageGeneration/ComfyUiClient.cs` | ComfyUI 协议客户端、连接探测 |
| `ShimmerChatBuiltin/ImageGeneration/ImageGenerationToolV2.cs` | 默认 / 自定义工作流工具；返回链接 + `ToolResult.Images` |
| `ShimmerChatLib/Generation/ToolResult.cs` | 工具结果的可选图像附件 |
| `ShimmerChatLib/Generation/PostChat.cs` | 追加消息时原子写入图像引用 |
| `ShimmerChatBuiltin/NodeBasic/PostGeneration/PostToolCallNode.cs` | 把工具结果图像写入 tool_result 消息 |
| `ShimmerChatBuiltin/ImageGeneration/ImageGenerationToolSetNode.cs` | 工具集节点 |
| `ShimmerChatBuiltin/ImageGeneration/ImageGenerationPanel.razor` | 设置面板 |
| `ShimmerChat/Singletons/ImageAttachmentService.cs` | `SaveGeneratedAsync` 文件落盘实现 |

测试：

- `ShimmerChatBuiltin.Tests/ImageGeneration/*`：配置校验、模板引擎、ComfyUI 客户端、工具、工具集节点。
- `ShimmerChat.Tests/ImageAttachmentServiceTests.cs`：生成结果始终落盘。
