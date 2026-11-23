# 配置管理界面统一设计（Config UI）

本文是「预设管理 + 配置界面」类页面的统一设计契约：共享外壳、操作语义、状态反馈与存储约定，避免每页各自拼装操作栏、弹窗和保存逻辑。

参考实现：**API 设置**（`ShimmerChatBuiltin/API/ApiSettingsPage.razor`）。

---

## 1. 统一后的页面模型

### 1.1 骨架

所有页面共享同一个骨架：

```
┌──────────────────────────────────────────────────────────┐
│ 固定操作面板（sticky，不随正文滚动）                      │
│  ┌ 身份区：图标 + 标题 + 数量 + 脏状态 ────── 新建 / 保存 ┐│
│  └ 切换器 + 克隆 / 重命名 / 收藏 / 删除 / ID / 检索 + 状态 ┘│
├──────────────────────────────────────────────────────────┤
│ 正文（随页面滚动）                                        │
│  分区卡片 1：基本信息                                     │
│  分区卡片 2：连接                                         │
│  分区卡片 3：生成参数                                     │
│  分区卡片 N：页面特有配置                                 │
└──────────────────────────────────────────────────────────┘
```

### 1.2 已实现的页面

| 页面 | 入口 | 配置单元 | 说明 |
| --- | --- | --- | --- |
| API 设置 | `panel.api_settings` | `ApiConfig` | 参考实现 |
| 文生图 | `panel.image_generation` | `ImageGenerationProfile` | |
| Embedding API 配置 | `panel.embedding_api_config` | `EmbeddingProfile` | 「当前使用」独立于收藏 |
| DynPrompt | `panel.dyn_prompt` | 规则集 / 规则 | 集合 + 词条两级 |
| 子智能体 | `panel.sub_agent_config` | 子代理配置 | 保留会话入口与插件面板 |
| 工具管理 | `panel.tool_manager` | 工具预设 | |
| MCP 工具集 | `panel.mcp_toolset` | MCP 服务配置 | 探测 / 复制节点 ID 在工具栏 |
| 消息渲染 | `/messagerender` | 渲染预设（节点树） | |
| 前生成预设 | `/generationmanager` | 预设（节点树） | |
| 后生成预设 | `/postgeneration` | 预设（节点树） | |

> 树编辑器、工具列表等复杂正文继续由页面自己渲染；统一的是外壳、操作语义和反馈，不是把所有正文做成同一种表单。

---

## 2. 固定操作面板

### 2.1 布局

- 使用 `position: sticky; top: 0`，`z-index: 30`。
- 页面根为 `.cfg-page.cfg-page-main`（`ConfigShell Flush="true"`，或 API 设置这类手写页面的等价类）。
  经插件面板路由托管的页面必须在 `[PluginPanel(..., Flush = true)]` 上声明同一标记：
  宿主（`PluginPanelPage`）据此**不渲染外层标题与卡片外壳**，操作面板用负外边距直接铺满
  `.shimmer-content` 的内边距，与主项目页面（如生成管理器）完全一致，滚动时不能露出缝隙。
- 滚动容器 `.shimmer-content` 自带内边距，sticky 的 `top` 必须同步抵消同一段距离（`--cfg-content-gutter`，由
  `MainLayout` 在 padding 变化时同步维护），否则滚动后操作栏会停在视口下方，顶部露出一条内边距高度的空隙。
  铺满整页的操作栏顶部不再保留圆角，避免圆角处透出底色。
- 面板背景半透明并使用 `backdrop-filter: blur()`；不透明度不低于 94%，避免正文文字透出干扰阅读。
- 宽屏一行到两行；窄屏（≤900px）自动换行，隐藏按钮文字但保留 `title` / `aria-label`。
- 面板本身不参与正文滚动；正文滚动容器仍是 `.shimmer-content`，不要另起 `overflow` 容器。

### 2.2 必须提供的操作

| 操作 | 类 / 行为 | 说明 |
| --- | --- | --- |
| 新建配置 | `.cfg-topbar-actions` | 立即插入并选中新配置，标记为未保存 |
| 保存配置 | 同上，带 `Ctrl+S` 提示 | 同步写入存储，成功后显示 toast + 状态 |
| 切换配置 | `.cfg-switcher` 原生 `select` | 选项按「收藏优先、原顺序」排序 |
| 克隆配置 | `.cfg-topbar-tools` | 新 ID；名称追加本地化「副本」并去重 |
| 删除配置 | 同上，危险色 | 二次确认；删除后选中相邻项；保存后生效 |
| 重命名 | 同上 | 弹窗输入；空名称必须报错，不静默回退 |
| 收藏 / 置顶 | 同上，星标 | 切换 `Pinned`；只影响选择器/检索排序，不改持久化顺序 |
| 配置 ID | 同上，剪贴板图标 | 复制稳定 ID；失败必须提示用户，不静默失败 |
| 检索 | 同上，放大镜 | 打开高级选择弹窗，支持名称/类型/模型/URL/ID 搜索 |

配置级独特操作（如 MCP 的连接测试、文生图的探测、Embedding 的连通性测试）放在**同一工具栏**内，使用 `.su-btn-secondary` 或图标按钮，不要挤进正文底部；与当前配置字段绑定的操作（如 Embedding 的「设为当前」）仍属于正文分区。

### 2.3 状态区

面板内固定展示两类状态：

1. **脏状态**（身份区徽标）
   - 有修改：`未保存`，警告色；
   - 无修改：`已保存`，成功色。
2. **操作结果**（工具栏右侧或下一行）
   - 成功 / 信息 / 错误三种；
   - 错误不自动消失，必须让用户看到；
   - 成功可以配合 2.6 秒后自动消失的 toast。

---

## 3. 正文规范

### 3.1 分区

- 每个分区是一个 `.cfg-section`，使用「图标 + 标题 + 一句说明」的头部。
- 分区顺序固定：**基本信息 → 连接 → 生成参数 → 高级 → 页面特有**。
- 分区内使用 12 列网格 `.cfg-grid`，字段间距 12px。
- 常用跨度：整行 12、双列 6、三列 4、四列 3；窄屏（≤720px）全部折叠为 12。
- 字段结构统一：

```html
<div class="cfg-field cfg-span-6">
    <label class="su-label">标签</label>
    <input class="su-input" ... />
    <p class="cfg-field-hint">可选说明</p>
</div>
```

- 只读标识使用 `.cfg-readonly-row` + `<code>`，不要伪装成可编辑输入框。
- 布尔值使用 `.cfg-toggle`，多个开关放在 `.cfg-toggle-row`。
- 提示使用 `.cfg-note`；警告/危险分别加 `.is-warning` / `.is-danger`。
- 不要使用内联 `style` 承载布局（颜色变量除外）；布局全部走 `cfg-*`。

### 3.2 文本补全模板等「子配置」

- 子配置（模板、规则、参数）在正文中作为独立分区。
- 子配置的增删只影响当前父配置，不进入顶部主工具栏。
- 子配置选择器与父配置切换器视觉上必须区分：父配置用顶部 select，子配置用分区内 select。

---

## 4. 交互契约

### 4.1 草稿语义

- 所有编辑先修改内存中的配置对象，**不即时写库**。
- 新建、克隆、删除、重命名、收藏都进入草稿状态，由「保存配置」统一提交。
- 切换配置不丢弃草稿；编辑结果保留在内存中，直到保存或刷新页面。
- 脏状态通过「当前完整配置列表序列化结果 + 当前选中项 ID」与上次保存快照比较得出，不维护第二份字段级脏标记。切换配置会改变全局选中项，因此也应标记为未保存。
- 页面刷新 / 关闭会丢弃未保存修改，这是明确契约；不要偷偷自动保存。

> 如果某页面的数据模型不适合「整表保存」（例如节点树特别大），仍必须保留显式保存与脏状态，可改为单配置快照比较，但不能静默落库。

### 4.2 Ctrl + S

- 仅在没有弹窗打开时触发（避免与重命名/删除输入冲突）。
- 必须 `preventDefault`，阻止浏览器「保存网页」。
- 触发后与点击「保存配置」走**同一条**保存路径，不允许两套逻辑。
- 视觉反馈必须同时包含：
  1. 工具栏状态变为「已保存」；
  2. 右下角 toast「配置已保存」；
  3. 保存失败时 toast 与状态均为错误色，并保持错误信息。
- 快捷键由 `wwwroot/js/util.js` 的 `configPageShortcuts` 统一注册/注销；页面在 `OnAfterRenderAsync(firstRender)` 注册，在 `DisposeAsync` 注销。

### 4.3 检索弹窗

- 由顶部「检索配置」按钮打开，`Esc` 或点击遮罩关闭。
- 搜索框自动聚焦；支持名称、类型、模型、URL、ID 的大小写不敏感匹配。
- 列表按收藏优先排序；当前项高亮并显示勾选。
- `Enter` 选中第一个匹配项；`Esc` 关闭。
- 空结果显示明确的空状态，不隐藏弹窗。
- 弹窗使用 `.su-modal-mask` + `.cfg-dialog`，禁止使用浏览器原生 `prompt` / `confirm`。

### 4.4 重命名 / 删除

- 重命名：弹窗输入，`Enter` 确认、`Esc` 取消；名称为空时显示字段错误并阻止提交。
- 删除：必须二次确认，明确提示「引用了该配置 ID 的节点/智能体会失效」以及「保存后生效」。
- 删除后选中原位置相邻配置；列表为空时进入空状态。
- 打开删除弹窗时焦点应落在「取消」上，保证 `Esc` 可用。

### 4.5 收藏 / 置顶

- 收藏是配置级布尔字段（如 `Pinned`），随配置持久化。
- 只影响选择器与检索弹窗的排序：`Pinned` 降序，其余保持原始存储顺序。
- 不物理重排存储列表，避免取消收藏后无法恢复原顺序，也避免影响其他消费者（如节点下拉）。

### 4.6 保存失败

- 必须捕获异常、记录 `LogError`、向用户显示可读错误。
- 不得用「看起来成功」掩盖失败；不得自动补默认值后继续提交。
- 加载失败时禁用保存/新建，并提供「重试」；不能把空列表伪装成「没有配置」。

---

## 5. 数据与存储契约

每个配置单元至少具备：

| 字段 | 要求 |
| --- | --- |
| `Id` | 稳定持久化标识；克隆必须生成新 ID；UI 提供复制 |
| `Name` | 用户可读名称；允许为空但显示回退名「未命名配置」 |
| `Pinned` | 收藏/置顶标记；旧数据缺失时按 `false` |
| 页面特有字段 | 保持原有序列化兼容，新增字段必须有默认值 |

- 存储键与原有实现保持一致，迁移只做增量字段，不改变已有 JSON 结构。
- 配置列表顺序是持久化契约的一部分；收藏排序只在视图层做。
- 读取旧数据时做**幂等规范化**（例如补空集合、校正越界索引），但必须记录警告，不能静默修补语义。
- 保存时同时持久化「当前选中项 ID」；该 ID 失效时回退到排序后的第一项并记录警告。
  纯预设管理页面（节点树、DynPrompt、工具预设、MCP）没有全局 active 语义，选中项属于视图状态，
  不写入存储；Embedding 与 API 这类存在「当前使用」的页面必须持久化选中 ID。

### 5.1 各页面的存储与兼容

| 页面 | 存储位置 | 收藏字段 | 兼容说明 |
| --- | --- | --- | --- |
| API 设置 | `ApiSettings/apiSetting` + `selectedAPIGuid` | `ApiConfig.Pinned` | 旧数据缺失按 `false` |
| Embedding | `EmbeddingAPI/profiles` + `active` | `EmbeddingProfile.Pinned` | 「当前使用」仍由 `active` 独立表示 |
| 文生图 | `ImageGeneration/Config` | `ImageGenerationProfile.Pinned` | 配置结构版本不变，仅新增字段 |
| DynPrompt | `DynPrompt/DynPromptSets` | `DynPromptSet.Pinned` | 旧集合缺失 `Id` 时在读取阶段生成并记录警告 |
| 子智能体 | `SubAgent/configs` | `SubAgentConfig.Pinned` | 保留 `Guid` 作为稳定标识 |
| 工具管理 | `ToolPresets/__presets__` | `ToolPreset.Pinned` | 保留旧格式迁移逻辑 |
| MCP | `Mcp/servers` | `McpEndpointConfig.Pinned` | 端点 `Id` 可在设置页修改；节点引用改用稳定 `Key`，旧数据读取时把原 `Id` 固化为 `Key` |
| 节点树预设 | `GenerationManager/generation_presets`、`PostGenerationManager/post_generation_presets`、`RenderModifierManager/render_modifier_presets` | `*Preset.Pinned` | 仅新增字段 |

新增字段全部有默认值，读取旧 JSON 不需要迁移动作；`Pinned` 只参与视图排序，持久化列表顺序保持不变。

文生图的配置 `Id` 会拼进工具名（`generate_image_{Id}`），统一界面把它作为只读标识：已有 Id 原样保留，
克隆时由系统分配新 Id，不再允许在表单里直接改写。

---

## 6. 样式与组件

### 6.1 共享样式

全局样式文件：`ShimmerChat/wwwroot/config-ui.css`（在 `App.razor` 中引用）。

主要类：

| 类 | 用途 |
| --- | --- |
| `.cfg-page` | 页面根容器，纵向排列 |
| `.cfg-topbar` | 固定操作面板 |
| `.cfg-topbar-main` / `.cfg-topbar-sub` | 面板两行 |
| `.cfg-identity` / `.cfg-identity-icon` / `.cfg-identity-title` | 身份区 |
| `.cfg-topbar-actions` / `.cfg-topbar-tools` | 主操作 / 次级操作 |
| `.cfg-switcher` | 配置切换器 |
| `.cfg-dirty` | 脏状态徽标 |
| `.cfg-status` | 操作结果 |
| `.cfg-body` / `.cfg-section` / `.cfg-section-head` | 正文与分区 |
| `.cfg-grid` / `.cfg-field` / `.cfg-span-*` | 表单网格 |
| `.cfg-field-hint` / `.cfg-field-error` | 字段说明 / 错误 |
| `.cfg-toggle` / `.cfg-toggle-row` | 开关 |
| `.cfg-note` | 提示条 |
| `.cfg-dialog*` / `.cfg-searchbox` / `.cfg-choice*` | 弹窗与检索列表 |
| `.cfg-toast` | 保存/复制反馈 |
| `.cfg-empty` | 空状态 |

按钮、输入框、徽标继续复用 `shimmer-ui.css` 的 `su-*` 类与 `--su-*` 设计令牌。

### 6.2 组件边界

- 共享外壳已抽取为 `ShimmerChatLib/Components/ConfigShell.razor`（泛型 `ConfigShell<T>`）。
  页面通过 `Items` / `SelectedId` / `*Selector` 与回调 `OnCreate` / `OnSave` / `OnSelect` / `OnClone` /
  `OnRename` / `OnDelete` / `OnTogglePin` 接入；外壳统一负责固定面板、切换器、收藏、检索、
  重命名/删除弹窗、状态 toast 与 `Ctrl+S`。
- 页面只负责：数据加载、草稿状态、分区正文、保存/校验实现，以及配置级独特操作（通过
  `ToolbarExtras` 放回同一工具栏）。
- 正文中的复杂编辑器（节点树、工具列表、CodeMirror 等）保持原组件，只套用 `.cfg-section` 外壳。
- 修改外壳时不得改变本文的 DOM 类名与交互契约；所有页面共享同一份行为。

#### 调用示例

统一外壳的调用方式（以 Embedding 页面为例）：

```razor
<ConfigShell T="EmbeddingProfile"
            Items="_profiles"
            SelectedId="@_selectedId"
            IdSelector="@(p => p.Id)"
            NameSelector="@(p => p.Name)"
            PinSelector="@(p => p.Pinned)"
            Title="@Loc["panel.embedding_api_config"]"
            Flush="true"
            IsDirty="IsDirty"
            LoadError="@_loadError"
            StatusMessage="@_statusText"
            StatusKind="@_statusKind"
            OnCreate="CreateProfile"
            OnSave="SaveProfiles"
            OnSelect="SelectProfile"
            OnClone="CloneProfile"
            OnRename="RenameProfile"
            OnDelete="DeleteProfile"
            OnTogglePin="TogglePin">
    <ToolbarExtras>…配置级独特操作…</ToolbarExtras>
    <Body>…分区正文…</Body>
</ConfigShell>
```

> 注意：`SelectedId`、`LoadError`、`StatusMessage` 等字符串参数必须写成 `@_field`；
> 漏写 `@` 会被 Razor 当成字面量，导致选择器与选中项不同步。

---

## 7. 本地化与可访问性

- 所有新增文案必须同时补齐 `zh-CN` 与 `en-US`。
- 共享文案使用 `config_ui.*` 命名空间；页面专属文案使用 `api_settings.*` 这类页面前缀。
- 图标按钮必须有 `title` 与 `aria-label`；状态提示使用 `role="status"`。
- 弹窗必须有 `role="dialog"`、`aria-modal="true"`。
- 所有交互可用键盘完成：Tab 可达、Enter 确认、Esc 取消。
- 窄屏不依赖 hover；图标按钮至少 28×28px。
