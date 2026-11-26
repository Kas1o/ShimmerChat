# Creator 工具集

> 本文描述内置的 Creator 工具集：三个让模型（AI）以结构化数据完成智能体创建与修改的
> Function Calling 工具——智能体 CRUD、节点目录、管线预设 CRUD。
> 代码位于 `ShimmerChatBuiltin/Creator/`，经 `IAutoCreateToolV2` 自动发现，无需手工注册。

---

## 1. 工具清单

| 工具名 | 本地化 Key | 作用 | 关键参数 |
|--------|------------|------|----------|
| `creator_agent` | `tool.creator_agent` | 新建 / 列出 / 查看 / 修改 / 删除智能体，读写其三条私有管线树 | `action`（必填）、`agent_id`、`name`、`user_intro`、`greeting`、`alternative_greetings`、`tags`、`chat_view_id`、`pipeline`、`tree_json` |
| `creator_node_catalog` | `tool.creator_node_catalog` | 列出所有管线节点类型及其属性元数据；列出已注册工具 | `action`（必填）、`pipeline` |
| `creator_preset` | `tool.creator_preset` | 列出 / 查看 / 新建 / 修改 / 删除三条管线的预设 | `action`（必填）、`pipeline`、`preset_id`、`name`、`root_node_json`、`pinned` |

三个工具都同时实现 `IAutoCreateToolV2`（自动发现）与 `IToolV3`（结构化参数、
`CancellationToken`、`ToolResult`），分类为 `category.creator`。

启用方式与其它自动工具一致：

1. 工具预设（Tool Manager）勾选对应工具类型；
2. 通用工具实例化节点（ToolInstantiateNode）选择对应类型。

---

## 2. creator_agent

### 2.1 动作

| 动作 | 参数 | 说明 |
|------|------|------|
| `list` | — | 全部智能体：`agent_id`、`name`、`tags`、`chat_view_id`、`chat_count`、三条管线是否有私有树。注册表里加载失败的记录进 `unreadable`，不隐藏 |
| `get` | `agent_id` | 单个智能体完整设置（不含树 JSON）；含 `avatar_path` / `background_path`（相对 `UserUploadImage` 的路径，null 表示无图） |
| `create` | `name`（必填）、`user_intro`、`greeting`、`alternative_greetings`、`tags`、`avatar_path`、`background_path` | 经 `Agent.Create` + `Save` + `AddAgentToAll`，返回新 `agent_id` |
| `update` | `agent_id` + 任意字段 | **只改参数里显式出现的字段**；显式 `null` 清空 `greeting` / `chat_view_id` / 图像路径 |
| `delete` | `agent_id` | 从 `__AllAgents__` 注册表移除，记录与对话保留（与 UI 删除语义一致，建议先与用户确认） |
| `get_pipeline` | `agent_id`、`pipeline` | 原样返回私有树 JSON；未配置时返回 `tree_json: null` 与说明 |
| `set_pipeline` | `agent_id`、`pipeline`、`tree_json` | 校验后写入；`""` / `"{}"` 表示清除（回退全局 / 默认管线） |
| `list_chat_views` | — | 已注册对话界面，供 `chat_view_id` 选择 |

### 2.2 契约要点

- **部分更新走 `Agent.Update`**：进程内互斥读改写，避免 Blazor Server 多电路下
  与整对象 `Save` 相互覆盖（见 `Agent.Update` 注释）。这是所有「只改部分字段」路径的统一入口。
- **写穿当前会话**：修改 `env.Agent` 所属的智能体时，同一组字段变更会同步到宿主持有的
  活实例（`PersistentEnv.Agent` 与 UI 共享同一对象），否则下一轮生成仍用旧树，
  表现为「工具说改了但没生效」。写穿只覆盖工具管理的那组字段。
- **删除自我保护**：拒绝删除当前会话所属的智能体（`TOOL_PROTECTED_TARGET`），
  让用户从智能体列表页操作。
- **`chat_view_id` 校验**：宿主界面注册表（`IChatViewRegistry`）可用时拒绝未注册 Id
  并列出可用值；注册表不可用（无宿主服务）时按原样接受，由宿主显示期做显式回退 + 告警。
- **图像路径写入**：`create` / `update` 接受 `avatar_path` / `background_path`，
  值可以是相对 `UserUploadImage` 的路径（如 `avatar.png`），也可以是 `/userimages/...` 链接。
  目标文件必须已存在；绝对路径、盘符、`..` 与不存在的文件返回 `TOOL_INVALID_ARGUMENTS`，
  显式 `null` / 空串清空路径。不存在「顺便把外部文件复制进来」的隐式行为。

---

## 3. creator_node_catalog

节点目录来自 `INodeTypeCatalog`（接口 + Attribute 自动发现），工具不维护平行注册表。

`list`（可选 `pipeline` 过滤）每个条目返回：

```json
{
  "type": "ShimmerChatBuiltin.NodeBasic.PreGeneration.SequenceNode",
  "type_name": "SequenceNode",
  "label_key": "node.sequence",
  "label": "…（本地化）",
  "description": null,
  "category": "category.flow",
  "icon": "▦",
  "pipelines": ["pre"],
  "properties": [
    { "name": "Nodes", "kind": "child_list", "element_wrapper": "PreGenerationNodeWrapper", … },
    { "name": "Repeat", "kind": "int", … }
  ]
}
```

- `type` 就是树 JSON 里 `"$type"` 的取值（完整类型名，无程序集段）。
- `properties` 来自 `[NodeProperty]` 反射；`kind` 为 `child_list` / `child_node` /
  `enum` / `int` / `float` / `bool` / `string`。
- **枚举同时给出名称与数字值**：树 JSON 按序列化器默认行为存数字
  （未配置 StringEnumConverter），只有数字值能让模型写出合法 JSON。

`list_tools` 返回 `IToolRegistry` 的自动工具（`type_name` 即 ToolPresetNode /
ToolInstantiateNode 使用的取值）。

---

## 4. creator_preset

存取经 `PipelinePresetStore`（三条管线预设存储位置的唯一事实来源，见第 6 节）。

| 动作 | 说明 |
|------|------|
| `list` | 不带 `pipeline` 时返回 `{"pre":[…],"post":[…],"render":[…]}`；带过滤时只含该管线。条目：`id`、`name`、`pinned` |
| `get` | `name`、`pinned`、`root_node_json`（树 JSON 作为转义字符串返回） |
| `create` | 生成新 GUID id；`root_node_json` 先校验再入库 |
| `update` | 只改显式出现的字段；树校验失败时保持旧值 |
| `delete` | 从存储移除；`"__default__"` 受保护 |

**`"__default__"` 预设受保护**：三条管线的宿主管理器在每次启动时删除并重建它，
工具侧的修改 / 删除都会丢失，因此直接以 `TOOL_PROTECTED_TARGET` 拒绝并建议另建预设。

预设列表的「读全表 → 改 → 整体写回」在工具内以进程内锁互斥；
树 JSON 的校验 / 规范化语义见第 5 节。

---

## 5. 节点树 JSON 契约（三个工具共用）

`set_pipeline` / `root_node_json` 接受的树 JSON 与节点编辑器存储的格式完全一致：

```json
{
  "Id": "任意 GUID（可省略，缺省自动生成）",
  "Name": "显示名",
  "IsExpanded": true,
  "IsBypassed": false,
  "Node": {
    "$type": "ShimmerChatBuiltin.NodeBasic.PreGeneration.SequenceNode",
    "Nodes": [
      {
        "Name": "System Prompt",
        "Node": {
          "$type": "ShimmerChatBuiltin.Fragments.FragmentNode",
          "Content": "You are a helpful assistant.",
          "From": 2
        }
      }
    ]
  }
}
```

- `Id` / `Name` / `IsExpanded` / `IsBypassed` 属于包装器；业务节点位于 `Node` 下，
  以 `"$type" = 完整类型名` 标识。
- 容器节点的子包装器列表（`Nodes` / `Children`）元素本身不带 `$type`。
- 枚举按数字存储（`creator_node_catalog` 的 `enum_values` 给出对应关系）。
- **校验与运行期同一套代码**：工具用对应管线的序列化器
  （Pre：`IPreGenerationNodeSerializer`；Post：`IPostGenerationNodeSerializerService`；
  Render：`IRenderModifierNodeSerializer`）执行 `DeserializeTree`——
  类型白名单、旧格式懒迁移、负载缺失检查都复用宿主实现，没有第二份校验规则。
- 校验通过后**写入规范化格式**（重新序列化）；旧格式 JSON 由此转正，
  结果里 `migrated_from_legacy: true` 提示模型内容被规范化过。
- `""` / `"{}"` 语义为「清除 / 未配置」，落库时写 `null`（智能体私有树）或 `"{}"`（预设）。
- 无宿主服务的上下文（如子代理虚拟会话，`PersistentEnv.Services == null`）无法解析序列化器，
  树操作显式失败 `TOOL_HOST_SERVICE_UNAVAILABLE`，绝不跳过校验写坏数据。

---

## 6. 相关文件

| 文件 | 职责 |
|------|------|
| `ShimmerChatBuiltin/Creator/CreatorAgentTool.cs` | 智能体 CRUD 与私有管线树读写（含写穿同步） |
| `ShimmerChatBuiltin/Creator/CreatorNodeCatalogTool.cs` | 节点目录 / 工具目录的元数据暴露 |
| `ShimmerChatBuiltin/Creator/CreatorPresetTool.cs` | 三条管线预设 CRUD 与 `"__default__"` 保护 |
| `ShimmerChatBuiltin/Creator/CreatorShared.cs` | 管线解析、序列化器解析、树校验与规范化 |
| `ShimmerChatLib/Generation/PipelinePresetStore.cs` | 预设存储位置的唯一事实来源 |
| `ShimmerChatLib/Generation/GenerationPreset.cs` | `IPipelinePreset` 与三个预设类 |
| `ShimmerChatLib/Generation/ToolResult.cs` | 工具错误码 |
| `ShimmerChatLib/Generation/IGenerationNodeSerializer.cs` | `IRenderModifierNodeSerializer` |
| `ShimmerChatBuiltin.Tests/Creator/*` | 三个工具的成功 / 失败路径单测 |
| `ShimmerChat.Tests/CreatorToolHostIntegrationTests.cs` | 工具 × 真实序列化器 / 真实目录的集成契约（树 JSON 格式、白名单拒绝、三序列化器解析） |
