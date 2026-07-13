# ShimmerChat 主题系统

## 架构概览

主题系统由三层组成：

```plain
┌─────────────────────────────────────────────────────┐
│  ThemeManager.razor     ← 用户编辑主题的 UI        │
├─────────────────────────────────────────────────────┤
│  ThemeServiceV2         ← 运行时主题管理 & 持久化  │
│  Theme.cs               ← 主题数据模型             │
├─────────────────────────────────────────────────────┤
│  theme.css              ← CSS 自定义属性 (Token)   │
│  shimmer-ui.css         ← 组件库，消费 Token       │
│  common.css             ← 应用级样式               │
│  node-editor.css        ← 节点编辑器样式           │
└─────────────────────────────────────────────────────┘
```

**流程**：`ThemeServiceV2` 持有一个当前 `Theme` 对象，当主题切换时通过 JS 互操作把 `Theme` 的所有属性注入到 `<html>` 元素上作为 CSS 自定义属性（`--su-*`、`--node-*`）。所有 UI 样式通过引用这些变量实现主题切换，无需重新加载 CSS 文件。

---

## Theme 数据模型 (`Theme.cs`)

`ShimmerChatLib/Models/Theme.cs` 定义了主题的完整数据结构，每个属性都有 `[JsonProperty]` 用于序列化存储/导入导出。

### 元数据

| 属性          | 类型      | 说明                            |
| ------------- | --------- | ------------------------------- |
| `Id`          | `string`  | GUID-based 唯一标识             |
| `Name`        | `string`  | 主题名称                        |
| `Description` | `string?` | 主题描述                        |
| `IsDefault`   | `bool`    | 是否为默认主题                  |
| `IsBuiltIn`   | `bool`    | 是否为内置主题（不可编辑/删除） |
| `IsDarkMode`  | `bool`    | 是否为暗色模式                  |

### Surface 层级

4 级背景色，从底层到顶层：

| 属性       | CSS 变量         | 默认值    | 用途               |
| ---------- | ---------------- | --------- | ------------------ |
| `Surface0` | `--su-surface-0` | `#f8f9fa` | 页面底色           |
| `Surface1` | `--su-surface-1` | `#ffffff` | 卡片、面板背景     |
| `Surface2` | `--su-surface-2` | `#ffffff` | 模态框、悬浮层背景 |
| `Surface3` | `--su-surface-3` | `#f1f3f5` | 输入框、交替行背景 |

### Text 层级

| 属性    | CSS 变量      | 默认值    | 用途              |
| ------- | ------------- | --------- | ----------------- |
| `Text0` | `--su-text-0` | `#11181c` | 正文 / 主文字     |
| `Text1` | `--su-text-1` | `#4a5568` | 次要文字          |
| `Text2` | `--su-text-2` | `#8896a4` | 辅助文字 / 占位符 |

### Border 层级

| 属性      | CSS 变量        | 默认值    | 用途     |
| --------- | --------------- | --------- | -------- |
| `Border0` | `--su-border-0` | `#e2e8f0` | 主边框   |
| `Border1` | `--su-border-1` | `#edf2f7` | 微妙边框 |

### Accent（强调色）

| 属性          | CSS 变量            | 默认值    | 用途                   |
| ------------- | ------------------- | --------- | ---------------------- |
| `Accent`      | `--su-accent`       | `#5e6ad2` | 主按钮、链接、选中态   |
| `AccentHover` | `--su-accent-hover` | `#4f5ac7` | hover 态               |
| `AccentSoft`  | `--su-accent-soft`  | `#f0f1fd` | 弱强调背景（如选中行） |

### Semantic（语义色）

| 属性          | CSS 变量            | 默认值 (Light) | 默认值 (Dark) | 用途          |
| ------------- | ------------------- | -------------- | ------------- | ------------- |
| `Success`     | `--su-success`      | `#2da44e`      | `#3fb950`     | 成功状态      |
| `SuccessSoft` | `--su-success-soft` | `#e6f4ea`      | `#122418`     | 成功背景      |
| `Warning`     | `--su-warning`      | `#d97706`      | `#f0a020`     | 警告状态      |
| `WarningSoft` | `--su-warning-soft` | `#fef3c7`      | `#1f1808`     | 警告背景      |
| `Danger`      | `--su-danger`       | `#cf222e`      | `#f85149`     | 危险/错误状态 |
| `DangerSoft`  | `--su-danger-soft`  | `#fde8e8`      | `#1f1114`     | 危险背景      |
| `Info`        | `--su-info`         | `#2563eb`      | `#58a6ff`     | 信息状态      |
| `InfoSoft`    | `--su-info-soft`    | `#eff6ff`      | `#0d1b2e`     | 信息背景      |

### Node Colors（节点语义色）

用于节点编辑器区分不同类型节点：

| 属性           | CSS 变量          | 默认值    | 对应节点                      |
| -------------- | ----------------- | --------- | ----------------------------- |
| `NodeFlow`     | `--node-flow`     | `#3b82f6` | 流程控制节点 (Sequence 等)    |
| `NodeBranch`   | `--node-branch`   | `#f59e0b` | 分支节点 (Conditional)        |
| `NodeLink`     | `--node-link`     | `#10b981` | 引用/链接节点 (CallNode)      |
| `NodeFragment` | `--node-fragment` | `#6366f1` | 片段节点 (FragmentNode)       |
| `NodePrompt`   | `--node-prompt`   | `#a855f7` | Prompt 相关 (PromptNode)      |
| `NodeTool`     | `--node-tool`     | `#22c55e` | 工具节点 (ToolPresetNode)     |
| `NodeMemory`   | `--node-memory`   | `#eab308` | 记忆节点 (MemoryRetrieveNode) |
| `NodeConfig`   | `--node-config`   | `#ef4444` | 配置节点 (APISelectNode)      |
| `NodeSubagent` | `--node-subagent` | `#ec4899` | 子代理节点                    |
| `NodeDebug`    | `--node-debug`    | `#94a3b8` | 调试/未知节点                 |

### Shadows

| 属性       | CSS 变量         | 默认值 (Light)                 |
| ---------- | ---------------- | ------------------------------ |
| `ShadowSm` | `--su-shadow-sm` | `0 1px 2px rgba(0,0,0,0.04)`   |
| `ShadowMd` | `--su-shadow-md` | `0 4px 12px rgba(0,0,0,0.06)`  |
| `ShadowLg` | `--su-shadow-lg` | `0 12px 32px rgba(0,0,0,0.10)` |

### Radii

| 属性       | CSS 变量         | 默认值 | 用途                     |
| ---------- | ---------------- | ------ | ------------------------ |
| `RadiusSm` | `--su-radius-sm` | `4px`  | 小元素（标签、徽章）     |
| `RadiusMd` | `--su-radius-md` | `6px`  | 默认圆角（按钮、输入框） |
| `RadiusLg` | `--su-radius-lg` | `10px` | 大元素（卡片、模态框）   |

### Spacing（8 级间距）

| 属性      | CSS 变量        | 默认值 |
| --------- | --------------- | ------ |
| `Space1`  | `--su-space-1`  | `4px`  |
| `Space2`  | `--su-space-2`  | `8px`  |
| `Space3`  | `--su-space-3`  | `12px` |
| `Space4`  | `--su-space-4`  | `16px` |
| `Space5`  | `--su-space-5`  | `20px` |
| `Space6`  | `--su-space-6`  | `24px` |
| `Space8`  | `--su-space-8`  | `32px` |
| `Space10` | `--su-space-10` | `40px` |

### Typography

| 属性       | CSS 变量         | 默认值                                                                       |
| ---------- | ---------------- | ---------------------------------------------------------------------------- |
| `FontSans` | `--su-font-sans` | `-apple-system, BlinkMacSystemFont, 'Segoe UI', 'Inter', Roboto, sans-serif` |
| `FontMono` | `--su-font-mono` | `'SF Mono', 'Fira Code', 'Cascadia Code', Consolas, monospace`               |
| `FontXs`   | `--su-font-xs`   | `11px`                                                                       |
| `FontSm`   | `--su-font-sm`   | `12px`                                                                       |
| `FontBase` | `--su-font-base` | `13px`                                                                       |
| `FontMd`   | `--su-font-md`   | `14px`                                                                       |
| `FontLg`   | `--su-font-lg`   | `16px`                                                                       |

### Misc

| 属性         | CSS 变量           | 默认值       | 用途                |
| ------------ | ------------------ | ------------ | ------------------- |
| `BorderSize` | `--su-border-size` | `1px`        | 全局边框宽度        |
| `Transition` | `--su-transition`  | `150ms ease` | 全局过渡动画        |
| `CustomCss`  | (直接注入)         | —            | 用户自定义 CSS 覆盖 |

---

## ThemeServiceV2 服务

`ShimmerChat/Singletons/ThemeServiceV2.cs` 实现了 `IThemeService` 接口，以单例模式注入。

### 核心方法

| 方法                 | 说明                                              |
| -------------------- | ------------------------------------------------- |
| `CurrentTheme`       | 获取当前激活的主题对象                            |
| `AvailableThemes`    | 获取所有可用主题列表                              |
| `SetTheme(id)`       | 切换主题，触发 `OnThemeChanged` 事件              |
| `CreateTheme(theme)` | 创建新用户主题                                    |
| `UpdateTheme(theme)` | 更新已有主题（仅非 BuiltIn 可编辑）               |
| `DeleteTheme(id)`    | 删除用户主题（不可删除 BuiltIn/default）          |
| `ExportTheme(id)`    | 导出主题为 JSON 字符串                            |
| `ImportTheme(json)`  | 从 JSON 导入主题                                  |
| `GetBuiltInThemes()` | 返回两个内置主题 `light_default` / `dark_default` |

### 持久化

- 所有主题列表存在 KVData 的 `shimmerchat_all_themes` 键下
- 当前选择的主题 ID 存在 `shimmerchat_current_theme_id` 键下
- 用户主题与内置主题合并存储

### 主题切换机制

当 `SetTheme` 或 `UpdateTheme` 被调用时：

1. 触发 `OnThemeChanged` 事件
2. 订阅方（通常是 `MainLayout`）调用 JS 互操作
3. JS 将 `Theme` 对象的所有属性映射为 `--su-*` / `--node-*` CSS 变量设置到 `<html>` 元素
4. 对于暗色主题，同时设置 `data-theme="dark"` 属性
5. 所有 CSS 引用这些变量，立即生效无需刷新

---

## theme.css — 默认 Token 定义

`ShimmerChat/wwwroot/theme.css` 在应用启动时加载，提供**初始值**，确保在 `ThemeServiceV2` 注入运行时变量之前页面有正确的默认样式。

```css
:root {
  /* 为所有 --su-* 和 --node-* 变量提供默认值 */
  --su-surface-0: #f8f9fa;
  --su-surface-1: #ffffff;
  /* ... 等 */
}

[data-theme="dark"] {
  /* 暗色覆盖 */
  --su-surface-0: #0d0d0d;
  --su-surface-1: #1a1a1a;
  /* ... 等 */
}
```

这些值与 `Theme.cs` 中 `GetBuiltInThemes()` 的两个内置主题完全一致。`theme.css` 只提供**后备**，运行时由 `ThemeServiceV2` 通过 JS 注入覆盖。

---

## ShimmerUI 组件库 (`shimmer-ui.css`)

`ShimmerChat/wwwroot/shimmer-ui.css` 是一套自建的 CSS 组件库，取代 Bootstrap。所有样式都通过 `--su-*` CSS 变量消费主题 Token。

### Button 按钮

```html
<button class="su-btn su-btn-primary">Primary</button>
```

| Class                     | 说明                           |
| ------------------------- | ------------------------------ |
| `.su-btn`                 | 基础按钮                       |
| `.su-btn-primary`         | 主按钮 (Accent 背景)           |
| `.su-btn-secondary`       | 次要按钮 (边框 + 透明背景)     |
| `.su-btn-danger`          | 危险按钮 (Danger 背景)         |
| `.su-btn-success`         | 成功按钮 (Success 背景)        |
| `.su-btn-ghost`           | 幽灵按钮 (无背景无边框)        |
| `.su-btn-outline`         | 轮廓按钮 (边框 + hover accent) |
| `.su-btn-outline-primary` | 轮廓主色按钮 (accent 边框)     |
| `.su-btn-outline-danger`  | 轮廓危险按钮 (danger 边框)     |
| `.su-btn-outline-success` | 轮廓成功按钮 (success 边框)    |
| `.su-btn-sm`              | 小按钮                         |
| `.su-btn-lg`              | 大按钮                         |
| `.su-btn-group`           | 按钮组 (无缝连接)              |
| `.su-btn-close`           | 关闭按钮 (×)                   |

### Form 表单

```html
<input class="su-input" type="text" placeholder="输入..." />
<textarea class="su-textarea"></textarea>
<select class="su-select">
  <option>...</option>
</select>
<label class="su-label">字段名</label>
<div class="su-form-group">...</div>
```

| Class                        | 说明                                    |
| ---------------------------- | --------------------------------------- |
| `.su-input`                  | 文本输入框 (100% 宽, focus accent 边框) |
| `.su-textarea`               | 文本域 (可调整大小, 最小高度 80px)      |
| `.su-select`                 | 下拉选择 (自定义箭头)                   |
| `.su-label`                  | 表单标签 (sm 字体, 粗体, text-1 色)     |
| `.su-form-group`             | 表单组 (vertical flex, gap 4px)         |
| `.su-checkbox` / `.su-radio` | 复选框/单选 (accent 色)                 |
| `.su-input-sm`               | 小输入框                                |
| `.su-input-group`            | 输入框组 (输入框 + 附加标签)            |

### Card 卡片

```html
<div class="su-card">
  <div class="su-card-header">标题</div>
  <div class="su-card-body">内容</div>
</div>
```

### Alert 提示

```html
<div class="su-alert su-alert-info">消息</div>
```

| Class                 | 说明           |
| --------------------- | -------------- |
| `.su-alert-info`      | 信息 (info 色) |
| `.su-alert-success`   | 成功           |
| `.su-alert-warning`   | 警告           |
| `.su-alert-danger`    | 危险           |
| `.su-alert-secondary` | 次要 (灰色)    |

### Badge 徽章

```html
<span class="su-badge su-badge-accent">新</span>
```

### Modal 模态框

```html
<div class="su-modal-mask">
  <div class="su-modal su-modal-sm">
    <div class="su-modal-header">标题</div>
    <div class="su-modal-body">内容</div>
    <div class="su-modal-footer">
      <button class="su-btn su-btn-primary">确认</button>
    </div>
  </div>
</div>
```

### Table 表格

```html
<table class="su-table su-table-sm">
  <thead>
    <tr>
      <th>列1</th>
      <th>列2</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td>...</td>
      <td>...</td>
    </tr>
  </tbody>
</table>
```

### List 列表

```html
<div class="su-list su-list-flush">
  <div class="su-list-item">项目 1</div>
  <div class="su-list-item active">项目 2 (选中)</div>
</div>
```

### Tabs 标签页

```html
<div class="su-tabs">
  <button class="su-tab active">标签 1</button>
  <button class="su-tab">标签 2</button>
</div>
```

### Grid 网格

```html
<div class="su-row">
  <div class="su-col-4">33%</div>
  <div class="su-col-8">66%</div>
</div>
```

### Utility 工具类

| 类别        | 示例                                                                                                                                                                           |
| ----------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Layout**  | `.su-flex`, `.su-flex-col`, `.su-flex-wrap`, `.su-flex-1`, `.su-items-center`, `.su-justify-between`, `.su-justify-center`, `.su-justify-end`, `.su-w-full`, `.su-h-full`      |
| **Gap**     | `.su-gap-1` 到 `.su-gap-4`                                                                                                                                                     |
| **Spacing** | `.su-mt-1..6`, `.su-mb-0..4`, `.su-ms-1..2`, `.su-me-1..2`, `.su-p-0..4`, `.su-pt-1..4`, `.su-pb-0..3`, `.su-ps-1..3`, `.su-pe-1..2`, `.su-py-1..3`, `.su-px-2..4`, `.su-my-3` |
| **Text**    | `.su-text-0/1/2`, `.su-text-accent/success/warning/danger/info`, `.su-text-center/right`, `.su-text-xs/sm/lg`, `.su-font-bold/mono/italic`, `.su-truncate`                     |
| **Misc**    | `.su-rounded/lg`, `.su-border-0/top/bottom/start`, `.su-shadow-sm/md`, `.su-cursor-pointer`, `.su-select-none`, `.su-d-none`, `.su-d-block`                                    |

---

## 自定义 CSS (`CustomCss`)

主题可以包含 `CustomCss` 字段，这是一个原始 CSS 字符串，在主题应用时会直接注入到页面中。用于覆盖组件库默认行为或添加项目特定的样式覆盖。

```json
{
  "customCss": ".my-special-panel { border-radius: 20px; }"
}
```

---

## 节点编辑器样式 (`node-editor.css`)

`ShimmerChat/wwwroot/node-editor.css` 是节点编辑器的全局样式（非 scoped CSS，因为 `GenericNodeEditor` 使用 `RenderTreeBuilder` 无法发出 scoped 属性）。

所有颜色都引用 `--su-*` 或 `--node-*` 变量：

| 组件                    | 前缀        | 主要变量                                                                |
| ----------------------- | ----------- | ----------------------------------------------------------------------- |
| Tree node card          | `.tn-*`     | `--su-surface-1/3`, `--su-border-0`, `--su-text-0/1/2`                  |
| Node buttons            | `.tn-btn-*` | `--su-success/soft`, `--su-accent/soft`, `--su-danger/soft`             |
| Add menu                | `.tn-add-*` | `--su-surface-1/3`, `--su-border-0/1`, `--su-accent`, `--su-text-0/1/2` |
| Generic editor fields   | `.ge-*`     | `--su-surface-3`, `--su-border-1`, `--su-text-0/1`                      |
| Tool instantiate editor | `.ti-*`     | `--su-surface-3`, `--su-border-1`, `--su-text-0/1/2`                    |
| Call node editor        | `.call-*`   | `--su-surface-3`, `--su-border-0/1`, `--su-text-0/1/2`                  |

---

## 创建新主题

1. 打开 Theme Manager (`/thememanager`)
2. 点击 **Create Theme**
3. 编辑名称、描述，选择是否为暗色模式
4. 使用颜色选择器或直接输入 HEX 值编辑各属性
5. 点击 **Save** — 主题立即生效

也可以通过 JSON 导入/导出主题文件进行分享。

## 在代码中使用主题变量

### Razor / HTML

直接使用 CSS 变量：

```html
<div style="color: var(--su-text-0); background: var(--su-surface-1)">
  自动跟随主题
</div>
```

### CSS

```css
.my-component {
  color: var(--su-text-0);
  background: var(--su-surface-1);
  border: var(--su-border-size) solid var(--su-border-0);
  border-radius: var(--su-radius-md);
  transition: background var(--su-transition);
}
.my-component:hover {
  border-color: var(--su-accent);
}
```

### C# 代码

注入 `IThemeService` 获取当前主题属性：

```csharp
@inject IThemeService ThemeService

var accent = ThemeService.CurrentTheme.Accent; // "#5e6ad2"
```
