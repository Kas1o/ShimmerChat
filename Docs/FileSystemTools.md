# 文件系统工具

> 本文描述内置的文件系统工具集：工具清单、路径访问范围、图像读取的模型可见性，以及失败语义。
> 代码位于 `ShimmerChatBuiltin/FileSystem/`，工具集节点位于 `ShimmerChatBuiltin/ToolManage/FileSystemToolSetNode.cs`。

---

## 1. 工具清单

| 工具名 | 本地化 Key | 作用 | 关键参数 |
|--------|------------|------|----------|
| `browse_directory` | `tool.browse_directory` | 列出目录内容与大小 | `path`（必填）、`include_subdirs` |
| `read_file` | `tool.read_file` | 读取文本文件，自动识别编码；二进制只给提示 | `path`（必填） |
| `read_image` | `tool.read_image` | 读取图像并把图像附加到工具结果，供模型真正“看到” | `path`（必填） |
| `write_file` | `tool.write_file` | 写入文本文件（不存在则创建，存在则覆盖） | `path`、`content`、`encoding` |
| `edit_file` | `tool.edit_file` | 按精确字符串替换编辑文本文件 | `path`、`old_string`、`new_string`、`replace_all` |
| `file_system_overview` | `tool.file_system_overview` | 磁盘、挂载点与常用目录概览 | 无 |

`read_file` 对超过 500 KiB 的文本只返回前 500 KiB，并在结果里注明；`read_image` 的大小上限见第 3 节。

---

## 2. 路径访问范围

工具自身只读取全局配置（KVData：`Filesystem/Config`）中的 `AllowList` / `BanList`：

- `BanList` 优先；命中即拒绝。
- `AllowList` 为空表示不限制；否则路径必须命中至少一条。
- 每行一个正则表达式，匹配的是 `Path.GetFullPath` 归一化后的、`/` 分隔的路径。

文件系统工具集节点（`node.file_system_tool_set`）可以再叠加一份节点级 `AllowList` / `BanList`，
并通过 `ScopeMode` 决定与全局配置的合并方式：

| 模式 | 语义 |
|------|------|
| `Override`（默认） | 只使用节点配置 |
| `Union` | 节点或全局任一允许即可 |
| `Intersection` | 节点与全局都必须允许 |

节点只负责给带 `path` 参数的工具加作用域包装；`file_system_overview` 没有路径参数，直接加入工具列表。
包装器同时支持 `IToolV2` 与 `IToolV3`，结构化工具的图像结果、取消令牌会原样转发。

---

## 3. `read_image` 的图像语义

`read_file` 遇到二进制只会返回“Binary file detected”，模型拿不到像素。`read_image` 的职责是把图像
放进 `ToolResult.Images`，由 `PostToolCallNode` 写到 tool_result 消息的 `Message.Images`，下一轮构建请求时
再变成多模态内容块。只返回路径或 Markdown 链接不会让模型看到图像。

约束与共享规则：

- 支持格式：`image/png`、`image/jpeg`、`image/webp`、`image/gif`，来自 `ImageAttachmentRules.SupportedMimeTypes`。
- 单文件上限：`ImageAttachmentRules.MaxBytes`（20 MiB），与聊天上传、生成图像使用同一份上限。
- 格式按**文件头**识别（PNG/JPEG/GIF/WebP 魔数），不信任扩展名；扩展名是 `.png` 的文本文件会明确报错。
- 图像以 Base64 内联进 `MessageImage`，不依赖 `UserUploadImage` 目录。
- 工具响应取消令牌；读取期间用户取消会抛 `OperationCanceledException`，由执行节点中止本轮，不写伪造结果。

### 返回值

成功时 `Content` 包含路径、MIME、大小、修改时间，`Images` 包含一张内联图像：

```text
Image: D:\pics\cat.png
Type: image/png
Size: 42.31 KB
Last Modified: 2026-01-02 03:04:05
The image is attached to this tool result so you can see it.
```

### 失败

| 场景 | 错误码 |
|------|--------|
| 缺 `path`、路径非法、文件不存在、路径是目录、空文件、超出大小上限、格式不支持 | `TOOL_INVALID_ARGUMENTS` |
| 全局/节点访问范围拒绝 | `TOOL_ACCESS_DENIED` |
| 文件读取 I/O 失败 | `TOOL_SERVICE_ERROR` |

失败时不会附带图像；技术详情写入 `DebugOutput`，不塞进模型可见文本。

---

## 4. 启用方式

两种方式等价，均来自自动发现（`IAutoCreateToolV2`）：

1. 文件系统工具集节点：勾选“读取图像”（`prop.fs_tool_set.enable_image_read`，默认开启）。
2. 通用工具实例化节点 / 工具预设：选择 `FileSystemImageReadToolV2`。

新增/修改工具后不需要改中央注册表；本地化 Key 必须同时补齐 `Locales/en-US.json` 与 `Locales/zh-CN.json`。

---

## 5. 相关文件

| 文件 | 职责 |
|------|------|
| `ShimmerChatBuiltin/FileSystem/FileSystemImageReadToolV2.cs` | `read_image` 工具、图像头识别、结构化结果 |
| `ShimmerChatBuiltin/FileSystem/FileSystemConfig.cs` | 全局路径规则与 KV 读写 |
| `ShimmerChatBuiltin/ToolManage/FileSystemToolSetNode.cs` | 工具勾选与节点级作用域包装 |
| `ShimmerChatBuiltin/FileSystem/FileSystemPanel.razor` | 全局 AllowList / BanList 设置面板 |
| `ShimmerChatLib/ImageAttachmentRules.cs` | 支持格式与大小上限的唯一来源 |
| `ShimmerChatLib/Generation/ToolResult.cs` | `ToolResult.Images` 与工具错误码 |
| `ShimmerChatBuiltin.Tests/FileSystem/*` | 图像读取、格式识别、节点作用域测试 |
