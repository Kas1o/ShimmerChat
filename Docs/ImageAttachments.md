# 图像附件（上传、存储、处理与请求解析）

> 本文描述图像从用户上传到进入 LLM 请求的完整链路：两种存储方式、上传处理方式（原图 / 处理模式）、
> 加载对话消息节点的预算处理，以及所有入口共享的约束与失败语义。

---

## 1. 链路概览

```
上传（聊天 / 节点编辑器）
   │  ImageAttachmentRules 校验（格式 / 单张 20MiB / 单条 5 张）
   ▼
IImageAttachmentService.SaveAsync ── 上传处理方式（原图模式 / 处理模式）
   │                                    └─ 处理模式：ImageNormalizer 按像素预算缩放 + 重编码
   ▼
MessageImage（UserUploadImage 文件引用 / 消息内联 Base64）
   │
   ▼
AppendChatMessagesNode（Pre 管线）── 解析为 ChatImage；Budgeted 策略下超预算图像再次经过 ImageNormalizer
   │
   ▼
SharperLLM（只处理 Base64 内容块，不接触文件系统）
```

三份**唯一事实来源**：

| 来源 | 决定什么 |
|------|----------|
| `ShimmerChatLib/ImageAttachmentRules.cs` | 支持的格式、单张字节上限、单条数量上限、像素预算与质量的合法区间及默认值 |
| `ShimmerChatLib/ImageMimeDetector.cs` | 文件头 → MIME 识别与声明校验（MCP、CodeAct、文件系统工具共用） |
| `ShimmerChat/Singletons/ImageNormalizer.cs` | 唯一的像素级处理实现（缩放 + 重编码），上传处理与请求预算处理共用 |

---

## 2. 存储方式（`App/image_storage_mode`）

| 方式 | 说明 |
|------|------|
| `UserUploadImage`（默认） | 图像写入 `UserUploadImage` 目录，消息里只保存文件名引用 |
| `MessageBase64` | Base64 直接内联进消息，随对话一起导出 / 迁移，消息体积更大 |

设置每次读取，切换后立即对**新的上传**生效；目录同时由 Program.cs 以 `/userimages/` 暴露为静态资源。
内联 Base64 的消息在构建请求前由 `IImageAttachmentService.ToChatImageAsync` 统一解析。

## 3. 上传处理方式（`App/image_upload_processing_mode`）

控制**进入存储前**是否对上传图像做处理，四份 KV 键（`App` 空间）：

| 键 | 取值 | 默认 |
|----|------|------|
| `image_upload_processing_mode` | `Original` / `Processed` | `Original` |
| `image_upload_pixel_budget` | 像素预算（`宽 × 高` 乘积） | 2048×2048 = 4194304 |
| `image_upload_compression` | `WebP` / `Jpeg` / `None` | `WebP` |
| `image_upload_quality` | 有损编码质量 1-100 | 85 |

- **原图模式**：字节原样存储，行为与旧版完全一致（默认，保证兼容与数据预期）。
- **处理模式**：像素总量超过预算的图像先**等比缩放**到预算内，再按所选算法**重编码**后存储；
  同时落盘与内联路径都会变小。**不超预算的图像保持原字节，零质量损失。**

设置在设置 → 杂项 → 图像设置中配置；处理模式开启时，聊天上传按钮与节点编辑器的图像列表都会给出提示。

### 3.1 处理器语义（`ImageNormalizer`）

基于 SkiaSharp（Chrome / Android 同源的解码器：libpng-turbo / libjpeg-turbo / libwebp）。
未选 ImageSharp：3.1.x 挂 6 条安全公告（均不在本项目使用路径上，但警告常驻）；
4.x 的 Split License 在 Release 构建强制 license key，对开源仓库分发不友好。

**平台支持**：native 库随 RID 发布自动携带，已验证 `win-x64`（`libSkiaSharp.dll`）、
`linux-x64`（`libSkiaSharp.so`，经 `SkiaSharp.NativeAssets.Linux.NoDependencies` 静态链接、
不依赖系统库）、`osx-x64`（`libSkiaSharp.dylib`）；macOS 包为 universal（x64 + arm64），
Linux 另有 arm / arm64 / musl 等变体。native 缺失时异常（`DllNotFoundException` 等）原样向上抛出，
不伪装成图像数据错误。约 80MB 的原生符号文件已在发布产物中移除（`RemoveNativeSymbolFilesFromPublish`）。

1. **GIF 永远跳过**：重编码只保留第一帧，等于悄悄丢动画，因此原样返回。
   多帧动画 WebP 同理（探测阶段 `FrameCount > 1` 即跳过）。
2. **先探测后解码**：只读图像头部即可判断像素总量与帧数，预算内的图像不付出解码成本。
3. **缩放**：`scale = √(预算 / 当前像素)`，等比缩放且宽高至少 1px；顺带按 EXIF 方向展开手机照片。
4. **重编码**：
   - `WebP`：体积收益最大，质量参数生效，保留透明通道；
   - `Jpeg`：兼容性最好，透明像素先合成白底；
   - `None`：仅缩放，保持原格式（PNG 仍是无损，体积收益有限）。
5. **像素安全上限**：声明尺寸超过 `ImageAttachmentRules.MaxPixelBudget × 4`（≈ 2.68 亿像素）时
   显式拒绝处理（解码会占用数百 MB 内存），报 `InvalidDataException`，不冒险解码。
6. **失败显式**：数据无法解码时抛 `InvalidDataException` 并附原因，绝不静默退回原图掩盖失败。

### 3.2 不受上传处理方式影响的图像

| 入口 | 原因 |
|------|------|
| 工具生成图（`SaveGeneratedAsync`，见 [ImageGeneration.md](ImageGeneration.md)） | 生成结果是用户明确要求的产品，画质不能被悄悄压低；始终原图落盘。其进入请求的体积问题由第 4 节兜底 |
| MCP 图像（见 [McpIntegration.md](McpIntegration.md)） | 与生成图同路径（`SaveGeneratedAsync`） |
| `read_image` / CodeAct 内联图像 | 不经落盘，直接内联 Base64（见 [FileSystemTools.md](FileSystemTools.md)、[CodeAct.md](CodeAct.md)）；请求侧由第 4 节兜底 |

---

## 4. 请求侧预算处理（`AppendChatMessagesNode`）

历史图像**每轮都会重发**。一张超预算的大图会让每轮请求持续携带巨大的 Base64 数据，
最终触发端点的 payload / token 上限，**对话从此无法继续**。加载对话消息节点提供逐树配置的处理策略：

| 属性 | 说明 |
|------|------|
| `ImageProcessing` | `AsStored`（默认，按存储原样发送）/ `Budgeted`（超预算先处理） |
| `ImagePixelBudget` | 像素预算，默认与合法区间同第 3 节 |
| `ImageCompression` | `WebP`（默认）/ `Jpeg` / `None` |
| `ImageQuality` | 1-100，默认 85 |

- 处理只发生在**本次请求的内存副本**上：消息持久化数据、`UserUploadImage` 文件都不被修改，
  每一轮按当时的节点配置重新处理。
- 越界的节点属性值（手改 JSON）在消费前收敛到合法区间，收敛动作写入 DebugOutput。
- 旧节点树反序列化后落到 `AsStored`，行为与旧版一致。

该策略覆盖了第 3.2 节的例外入口：生成图、MCP 图、`read_image` 内联图即使落盘 / 内联为原图，
进入请求前也会被压回预算内，历史遗留的大图对话因此可以救活。

接口层面：`IImageAttachmentService.ToChatImageAsync(image, processing, ct)` 为带处理参数的重载；
旧实现（测试替身、虚拟会话）经接口默认实现退化为原样解析——预算不生效但不会失败。

---

## 5. 失败语义汇总

| 场景 | 行为 |
|------|------|
| 上传格式不受支持 | `SaveAsync` 抛 `NotSupportedException`（上传前 UI 已按 `ImageAttachmentRules.Accept` 拦截） |
| 处理模式下数据无法解码 | `ImageNormalizer` 抛 `InvalidDataException`，上传失败并提示，不静默存原图 |
| 声明尺寸超过像素安全上限 | `ImageNormalizer` 显式拒绝（消息带具体尺寸与上限值） |
| 消息引用的文件缺失 | `ToChatImageAsync` 抛 `FileNotFoundException`，节点转成 `DATA_MISSING` 失败，不静默丢图 |
| 图像服务不可用（虚拟会话等） | `AppendChatMessagesNode` 返回 `SERVICE_ERROR` |
| KV 设置值非法 | `LogError` 后回退默认值（原图模式 / 默认参数），界面不会因坏配置挂掉 |
| 节点属性越界（手改 JSON） | 收敛到合法区间 + DebugOutput 留痕 |

## 6. 关键文件

| 文件 | 职责 |
|------|------|
| `ShimmerChatLib/ImageAttachmentRules.cs` | 全部共享约束、像素预算 / 质量区间与默认值、data URL 解析 |
| `ShimmerChatLib/ImageProcessingOptions.cs` | 处理模式、压缩算法、请求处理策略枚举与参数形状 |
| `ShimmerChatLib/Interface/IImageAttachmentService.cs` | 存储与解析服务契约（含带处理参数的兼容重载） |
| `ShimmerChat/Singletons/ImageAttachmentService.cs` | 存储方式与处理方式设置读取、落盘 / 内联、请求解析 |
| `ShimmerChat/Singletons/ImageNormalizer.cs` | 唯一的像素级处理实现 |
| `ShimmerChatBuiltin/Misc/Node/PreGeneration/AppendChatMessagesNode.cs` | 请求侧预算处理策略的配置点 |
| `ShimmerChat/Components/Pages/MiscSettingsPage.razor` | 设置界面（图像设置组） |
| `ShimmerChat.Tests/ImageNormalizerTests.cs` 等三处 | 成功路径与失败路径测试 |
