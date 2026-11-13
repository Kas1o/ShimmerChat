# CodeAct（C# 代码行动）

CodeAct 把“调用一个工具”升级为“写一段 C# 代码来组合调用本轮所有工具”。它由两部分组成：

- `CodeActNode`（前生成节点）：把 `CodeActToolV2` 加入本轮工具列表；
- `CodeActToolV2`（工具名 `codeact_csharp`）：用 Roslyn Scripting 执行模型给出的完整 C# 脚本。

旧的 `InvokeCSharpV2`（`EvalCsharp`）保持原行为不变；两者可以同时存在，互不影响。

---

## 1. 为什么需要专用节点

CodeAct 需要拿到**本轮生成环境**的 `TransientEnv.Tools`，而不是 `PersistentEnv` 里的工具目录。
`IAutoCreateToolV2.Create(PersistentEnv)` 在构造期看不到瞬态环境，因此 CodeAct 不实现该接口，
也不能通过“实例化工具 / 工具预设”节点加载。请使用 `CodeActNode`。

节点传入的是 `Transient.Tools` 的**活列表引用**，脚本在真正执行时才枚举工具，因此：

- 节点放在前生成树的任意位置都可以；
- 同一轮里在 CodeAct 节点**之后**添加的工具，脚本同样能调用；
- 下一轮重建环境后会创建新的 CodeAct 工具实例，看到的是新一轮的工具。

CodeAct 工具自身不会出现在脚本的 `Tools` 列表里，避免脚本递归调用自己。

---

## 2. 脚本可用的 API

脚本以 `CodeActGlobals` 作为 globals 对象，以下成员可直接使用：

| 成员 | 说明 |
|------|------|
| `Task<string> CallAsync(string toolName, object? arguments = null)` | 调用本轮工具并返回文本结果。工具返回失败时抛 `CodeActException`，不会把错误文本当成功继续。 |
| `Task<ToolResult> CallToolAsync(string toolName, object? arguments = null)` | 同上，但返回完整 `ToolResult`（`Status` / `ErrorCode` / `Content`），失败不抛异常。 |
| `IReadOnlyList<string> Tools` | 本轮可调用的工具名（去重，不含 CodeAct 自身）。 |
| `string GetToolInfo(string toolName)` | 查看工具的名称、描述与参数声明。 |
| `Task<string> AddImageAsync(string path, string? mimeType = null)` | 读取本地图像并附加到本次结果，返回显示 URL。 |
| `Task<string> AddImageAsync(byte[] content, string mimeType)` | 附加内存中的图像，返回显示 URL。 |
| `CancellationToken Ct` | 本轮工具调用的取消令牌；应传给异步调用，并在长循环里调用 `Ct.ThrowIfCancellationRequested()`。 |
| `PersistentEnv Env` | 持久化环境（服务入口）。 |
| `Chat Chat` / `Agent Agent` / `IKVDataService KVData` | 当前会话、Agent 与 KV 存储。 |
| `void Print(object? value)` | 写入工具输出（不写宿主控制台）。 |
| `CodeActGlobals CodeAct` | 指向自身的引用，便于脚本写成 `CodeAct.CallAsync(...)`。 |

参数规则：

- `arguments` 为普通对象（如匿名对象、字典）时序列化为 JSON；
- 为字符串时按**原始 JSON** 传入，便于脚本直接写 `CallAsync("x", """{"q":"..."}""")`；
- 非法 JSON 由目标工具按参数错误显式返回。

结果规则：

- 脚本最后一个表达式的值作为返回值；字符串原样返回，其他对象 JSON 序列化；
- 脚本最后一条表达式若是未 `await` 的 `Task`，工具会自动等待并取出结果；
- `Print` 的内容追加在返回值之后，以 `[stdout]` 标记；
- 结果文本超过 64,000 字符时显式截断并标注总长度。

---

## 3. 工具调用与图像输出

### 3.1 工具调用

被调用的工具按 `IToolV3` → `IToolV2` 的顺序分派：

- `IToolV3` 收到新的 `ToolInvocationContext`（`ToolCallId` 形如 `父调用#序号`）与本轮取消令牌；
- 旧 `IToolV2` 收到序列化后的 JSON 字符串，返回值包装成 `ToolResult`。

同一次脚本内的工具调用**串行执行**（内部信号量），避免插件工具在并发下状态互相干扰；
单次脚本最多发起 128 次嵌套工具调用，超限返回 `TOOL_CODE_EXECUTION_ERROR`。

### 3.2 图像输出（可在节点上关闭）

节点属性 `EnableImageOutput` 默认开启。

开启时：

- 被调用工具返回的 `ToolResult.Images` 自动随本次结果回传；
- 脚本可调用 `AddImageAsync(path)` / `AddImageAsync(bytes, mimeType)`，图像统一经
  `IImageAttachmentService.SaveGeneratedAsync` 落盘，返回 `/userimages/...` 链接；
- 格式按文件头识别，只接受 PNG / JPEG / WebP / GIF，大小上限复用 `ImageAttachmentRules.MaxBytes`；
- 单次结果最多 16 张图，超出部分会被丢弃并在结果中标注数量；显式 `AddImageAsync` 超限则直接失败。

关闭时：

- 被调用工具的图像不会回传，结果中标注被省略的数量；
- `AddImageAsync` 明确返回 `TOOL_IMAGE_OUTPUT_DISABLED`。

图像服务不可用（如子代理虚拟会话）时，`AddImageAsync` 返回 `TOOL_CONFIG_INVALID`，不会静默内联 Base64。

---

## 4. 取消与限制

- `Ct` 就是本轮工具调用的 `CancellationToken`；被调用工具与图像落盘都会响应它。
- 用户取消时 `CodeActToolV2` 会向上抛出 `OperationCanceledException`，执行节点据此停止本轮，不会写成工具错误。
- **取消是协作式的**：Roslyn 无法中断用户代码里的同步死循环。脚本若进行长时间计算，
  必须自行检查 `Ct`。这是代码执行的固有边界，工具描述中也已告知模型。
- 未捕获的脚本异常、编译错误都会变成结构化 `ToolResult.Fail`（见下节），由后处理节点按
  `continueOnToolError` 决定是否继续循环。

| 错误码 | 含义 |
|--------|------|
| `TOOL_CODE_COMPILATION_ERROR` | 脚本编译失败；`Details` 带完整 Roslyn 诊断 |
| `TOOL_CODE_EXECUTION_ERROR` | 脚本运行期未处理异常，或嵌套工具调用超限 |
| `TOOL_IMAGE_OUTPUT_DISABLED` | 图像输出已关闭却调用了 `AddImageAsync` |
| `TOOL_CONFIG_INVALID` | 当前会话没有图像存储服务 |
| `TOOL_OUTPUT_WRITE_FAILED` | 图像落盘失败、无法解析显示 URL，或图像数量超限 |
| `TOOL_INVALID_ARGUMENTS` | 参数缺失、工具名不存在、图像格式/大小/MIME 不匹配 |

---

## 5. 安全边界

CodeAct 在宿主进程内以**全权限**执行任意 C# 代码，没有沙箱：可以读写文件、访问网络、
调用反射、创建进程。它和旧的 `EvalCsharp` 一样属于“用户明确启用的高危能力”。

- 只把 `CodeActNode` 加进可信的生成树；
- 不要对不可信模型/不可信用户开放此节点；
- 需要隔离时，应在进程/容器级别隔离，而不是期待工具内部沙箱。

---

## 6. 示例

在系统中加入一段“先搜索，再读取图片，最后汇总”的脚本：

```csharp
var search = await CallAsync("web_search", new { query = "ShimmerChat 项目介绍" });
Print(search.Substring(0, Math.Min(500, search.Length)));

var info = GetToolInfo("read_image");
Print(info);

// 工具返回的图像会自动回传；也可以显式附加本地图像：
// var url = await AddImageAsync(@"C:\images\diagram.png");

search.Length
```

对应的工具调用参数为：

```json
{
  "code": "var search = await CallAsync(\"web_search\", new { query = \"ShimmerChat 项目介绍\" }); ...",
  "using": "System.Net.Http"
}
```

`using` 可省略；默认已导入 `System`、`System.IO`、`System.Linq`、`System.Text`、
`System.Threading`、`System.Threading.Tasks`、`Newtonsoft.Json`、`ShimmerChatLib`、
`ShimmerChatLib.Generation`、`ShimmerChatLib.Interface` 与 `ShimmerChatBuiltin.CodeAct`。
