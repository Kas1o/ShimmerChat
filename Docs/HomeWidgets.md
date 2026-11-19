# 首页 Widget 系统

首页（`/`）是一个可自定义的 Widget 工作台。Widget 是独立的 Blazor 组件，
由 `[HomeWidget]` 元数据自动发现；内置 Widget 与插件提供的 Widget 走同一条发现路径。

## 1. 当前内置 Widget

| Widget Id | 名称 | 默认出现 | 多实例 | 说明 |
|---|---|---|---|---|
| `agent-quick` | Agent 快捷入口 | 否 | 是 | 绑定一个 Agent，提供“新建聊天”和“继续最近聊天”。 |
| `getting-started` | 使用教学 | 是 | 否 | 从创建 Agent 到开始聊天的入门步骤；内容标记为 WIP。 |
| `github-release` | 检查更新 | 是 | 否 | 读取 GitHub 仓库的最新 Release。 |

首次运行（KV 中还没有布局数据）时，首页使用注册表里 `IsDefault = true` 的 Widget。
用户保存过布局后，即使移除了默认 Widget，也不会被自动加回。
把布局保存为空列表是合法的，首页会显示空状态。

## 2. 扩展点

### 2.1 元数据

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class HomeWidgetAttribute : Attribute
{
    public string Id { get; }             // 稳定标识，布局持久化引用它
    public string NameKey { get; }        // 本地化 Key
    public string? DescriptionKey { get; init; }
    public string Icon { get; init; }     // Bootstrap Icons 类名，如 "bi-robot"
    public int Order { get; init; }       // 添加面板排序
    public HomeWidgetSize Size { get; init; }   // Normal / Wide
    public bool AllowMultiple { get; init; } = true;
    public bool IsDefault { get; init; }
}
```

### 2.2 组件契约

```razor
@attribute [HomeWidget(
    "my-widget",
    "widget.my.name",
    DescriptionKey = "widget.my.desc",
    Icon = "bi-stars",
    Order = 100)]
@implements IHomeWidget

<article>
    @Loc["widget.my.body"]
</article>

@code {
    [Parameter]
    public HomeWidgetContext Context { get; set; } = default!;
}
```

宿主通过 `DynamicComponent` 渲染 Widget，并且**只传入 `Context` 一个参数**。
Widget 不得声明额外的 `[Parameter]`，否则会因缺少参数在渲染时抛异常。

`HomeWidgetContext` 提供：

- `Instance`：当前 Widget 实例，包括 `WidgetId`、`InstanceId` 与 `Settings`。
- `SaveSettingsAsync(IReadOnlyDictionary<string,string>)`：保存该实例配置，返回 `false` 表示持久化失败，Widget 必须显式提示。
- `RequestRefreshAsync()`：请求首页重新渲染。

Widget 的其他依赖（`IKVDataService`、`ILocService`、`NavigationManager` 等）通过 DI 注入。
插件程序集可以像内置 Widget 一样使用共享库中的服务接口。

### 2.3 发现与注册

`HomeWidgetRegistry` 委托 `IPluginLoaderService.GetTypesWithAttribute<HomeWidgetAttribute>()`
扫描所有已加载程序集（共享库、内置插件、宿主、外部插件），因此：

- 不需要修改任何中央注册表。
- 重复 Id 会记录 `LogError` 并跳过后出现的类型。
- 标记了 `[HomeWidget]` 但不是 `ComponentBase` / 未实现 `IHomeWidget` 的类会记录 `LogError` 并跳过。

## 3. 布局持久化

- KV 空间：`Home`
- KV Key：`WidgetLayout`
- 当前格式版本：`1`

存储结构：

```json
{
  "Version": 1,
  "Widgets": [
    {
      "InstanceId": "0f8fad5b-d9cb-469f-a165-70867728950e",
      "WidgetId": "agent-quick",
      "Settings": { "agentGuid": "..." }
    }
  ]
}
```

加载规则：

- 没有数据：返回默认布局，`IsFirstRun = true`。
- 非法 JSON、缺少 `Widgets`、空 `WidgetId`、空/重复 `InstanceId`、未知版本：返回明确的错误码与详情。
- 未知 `WidgetId` 不会被丢弃；首页显示“Widget 不可用”占位卡并允许移除，同时记录 `LogError`。
- `Settings` 缺失时补空集合并记录 `LogWarning`。

保存规则：

- 增删、排序、Widget 设置变更都会立即保存。
- 保存失败会回滚内存中的改动，并在首页顶部显示错误横幅。
- 布局只保存实例与配置，不保存 Widget 的显示状态或缓存。

## 4. 首页交互

- “自定义首页”进入编辑态：显示添加入口、上移/下移、移除按钮。
- “添加 Widget”：列出注册表中的 Widget；`AllowMultiple = false` 且已存在时禁用。
- 加载失败：显示错误横幅，可“重试”或“重置为默认布局”。
- 未知 Widget：显示占位卡，可在编辑态或卡片内移除。

## 5. 宿主服务

### 5.1 创建对话

`IChatCreationService`（`ShimmerChatLib.Interface`）是“新建对话”的唯一入口。
首页快捷入口与 `AgentPage` 都调用它，避免问候语、排序与持久化逻辑出现两份实现。

- 成功：返回 `ChatCreationResult(true, chatGuid)`，并把对话移到 Agent 的 `ChatGuids` 顶部。
- Agent 不存在 / 空 Guid / 存储失败：返回结构化错误码，UI 负责提示。

### 5.2 GitHub Release

`IGitHubReleaseService` 读取 GitHub REST API：

```
https://api.github.com/repos/{owner}/{repo}/releases/latest
```

配置项（`appsettings.json`）：

```json
"GitHubRelease": {
  "Repository": "Kas1o/ShimmerChat",
  "CacheMinutes": 10
}
```

- 成功结果缓存 `CacheMinutes` 分钟；Widget 的刷新按钮强制绕过缓存。
- 404、限流、网络失败、超时、非成功状态码、非法 JSON、配置错误分别返回独立错误码。
- 请求带 `User-Agent: ShimmerChat/{version}` 与 `Accept: application/vnd.github+json`。
- 服务不向 UI 抛异常；唯一例外是调用方主动取消（页面销毁），此时按取消契约抛出 `OperationCanceledException`。

## 6. 本地化

Widget 元数据的 `NameKey` / `DescriptionKey` 以及所有用户可见文本都必须提供本地化 Key。
内置 Widget 的 Key 位于 `ShimmerChatBuiltin/Locales/widgets.{culture}.json`，首页框架的
Key 位于 `ShimmerChat/Locales/`（按领域拆分的 `home.*` 等多个 `*.{culture}.json` 文件）。
`Locales/` 目录下文件名以 `.{culture}.json` 结尾都会被自动发现；支持的 culture 至少
包括 `zh-CN` 与 `en-US`。

## 7. 测试

- `ShimmerChat.Tests/HomeWidgetContractTests.cs`：发现、参数契约、Id 唯一、默认 Widget、本地化 Key。
- `ShimmerChat.Tests/HomeWidgetLayoutServiceTests.cs`：默认布局、往返、非法/未知版本/重复实例/存储失败。
- `ShimmerChat.Tests/ChatCreationServiceTests.cs`：新建对话成功、问候语版本、Agent 不存在、存储失败。
- `ShimmerChat.Tests/GitHubReleaseServiceTests.cs`：成功、缓存、404、限流、HTTP 错误、网络错误、非法 JSON、配置错误、取消。

## 8. 已知限制

- “使用教学”的文案与步骤仍标记为 WIP，后续会补充截图、节点示例与常见问题。
- Widget 的 `Size` 目前只有单列与两列两种；更细粒度的布局（拖拽、自由网格）尚未实现。
- GitHub Release 缓存为进程内内存缓存，重启后重新请求。
