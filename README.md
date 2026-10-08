<div align="center">

# ✨ ShimmerChat 2 ✨

**给桌面用户的基于可编辑节点树管线的 AI 聊天应用 — 自由编排 LLM 生成全链路**

[![License](https://img.shields.io/github/license/Kas1o/ShimmerChat)](LICENSE.txt)
[![.NET](https://img.shields.io/badge/.NET-10-blue)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-Server-orange)](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)


</div>

> 📖 [English Version](README_EN.md)

---

## 🌟 核心概念

ShimmerChat 2.0 的核心是**三管线可编辑节点树**。每次 AI 生成分为三个阶段，每个阶段由用户通过可视化节点编辑器自由编排：

```
Pre-Generation Tree ──→ LLM 生成 ──→ Post-Generation Tree ──→ Render Modifier Tree ──→ 显示
```

| 管线 | 职责 |
|------|------|
| **Pre-Generation** | 构建上下文：注入 system prompt、聊天历史、工具声明、选择 API |
| **Post-Generation** | 处理 LLM 原始响应：后处理、格式化、结果转换 |
| **Render Modifier** | Markdown → HTML 渲染管线：正则替换、显示修改 |

## 🌟 功能特性

| 特性 | 描述 |
|------|------|
| 🧩 **可视化节点编辑器** | 拖拽式节点树编辑，自由组合 22+ 内置节点（条件分支、子代理、动态模板、工具预设等） |
| 🤖 **智能代理系统** | 每个 Agent 拥有独立的三管线节点树配置，实现差异化行为 |
| 🔧 **Tool Call 系统** | 完整的 Function Calling 支持，流式 Tool Call 循环，自动工具发现与实例化 |
| 🧠 **子代理（SubAgent）** | 节点树中嵌套调用其他 Agent，构建复杂协作流程 |
| 🎨 **主题系统** | 完整的主题 CRUD + 导入/导出，CSS 变量驱动 |
| 💾 **持久化存储** | LiteDB 嵌入式数据库 / JSON 文件双模式，自动迁移 |
| 🔌 **插件系统** | 动态程序集加载，节点/工具/面板全插件化，与内置功能同权 |
| 🌐 **多语言** | zh-CN / en-US 本地化，即时切换 |
| 🖥️ **桌面应用** | Tauri v2 桌面壳，一键打包为原生桌面应用 |
| ⚙️ **多 API 支持** | OpenAI、Ollama、Kobold 等多种 LLM API |

<div align="center">

### 🖼️ 界面预览

<img src="README/AgentChatPage2.0.png" alt="聊天界面" />
> *聊天界面*

<img src="README/AgentPage2.0.png" alt="Agent 配置页面" />
> *Agent 配置页面*

<img src="README/NodeSystem2.0.png" alt="节点编辑器" />
> *节点编辑器*

</div>

---

## 🛠️ 技术栈

| 技术 | 用途 |
|------|------|
| **.NET 10** | 运行时框架 |
| **Blazor Server** | Web 应用框架 |
| **SharperLLM** | 自研 LLM 连接库（Tool Call、流式响应、多 API 适配） |
| **LiteDB** | 嵌入式 NoSQL 数据库 |
| **Markdig** | Markdown → HTML 渲染 |
| **Newtonsoft.Json** | JSON 序列化 |
| **Tauri v2** | 桌面应用打包 |
| **Bootstrap Icons** | UI 图标 |

---

## 📋 系统要求

- **.NET 10 SDK** 或更高版本
- **操作系统**: Windows、Linux、macOS（macOS 未经充分测试）

---

## 🚀 快速开始

### 安装 .NET 10

- [Windows](https://learn.microsoft.com/en-us/dotnet/core/install/windows)
- [Linux](https://learn.microsoft.com/en-us/dotnet/core/install/linux)
- [macOS](https://learn.microsoft.com/en-us/dotnet/core/install/macos)

### 克隆仓库

```bash
git clone --recurse-submodules https://github.com/Kas1o/ShimmerChat.git
```

<details>
<summary>忘了加 --recurse-submodules？</summary>

```bash
cd ShimmerChat
git submodule init
git submodule update
```

</details>

### 构建运行

```bash
cd ./ShimmerChat
dotnet run
```

---

## ⚙️ 配置

### API 配置

在 "API 设置" 页面配置 LLM 后端：

- **OpenAI API** — 兼容 OpenAI 协议的 API（URL、密钥、模型）
- **Ollama API** — 本地 Ollama 服务
- **Kobold API** — 完整的 Kobold 参数（温度、上下文长度、重复惩罚、采样器等）

### 端口配置

在 `appsettings.json` 中添加 Kestrel 节点：

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:5000"
      }
    }
  }
}
```

---

## 🔌 插件系统

插件放在 `Plugins/` 目录下，启动时自动发现并加载，与内置功能完全同权。内置扩展包括：

| 模块 | 功能 |
|------|------|
| **DynPrompt** | 动态模板提示系统 |
| **Variable** | 变量定义与注入 |
| **Memory** | 持久化记忆存储 |
| **SubAgent** | 子代理嵌套调用节点 |
| **FileSystem** | 文件读写工具集 |
| **RegexModifier** | 正则替换渲染修改器 |
| **InvokeCSharp** | 自定义 C# 代码执行 |

---

## 🏗️ 项目结构

```
ShimmerChat/
├── SharperLLM/           # LLM 连接库（子模块）
├── ShimmerChatLib/       # 共享库：模型、接口、管线核心抽象
├── ShimmerChat/          # 主项目：Blazor Server 宿主、服务实现、UI
├── ShimmerChatBuiltin/   # 内置插件：默认节点、工具、面板
├── src-tauri/            # Tauri 桌面发布项目
└── Docs/                 # 开发文档
```

---

## 📄 第三方许可

- [LiteDB](https://github.com/mbdavid/LiteDB) — MIT
- [Markdig](https://github.com/xoofx/markdig) — BSD-2-Clause
- [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning) — MIT
- [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) — MIT
- [qdrant-dotnet](https://github.com/qdrant/qdrant-dotnet) — Apache-2.0
- [SharperLLM](https://github.com/Kas1o/SharperLLM) — MIT
- [Tokenizers.HuggingFace.DotNet](https://github.com/IgnaciodelaTorreArias/Tokenizers.HuggingFace.DotNet) — Apache-2.0
- [Bootstrap Icons](https://github.com/uiwjs/bootstrap-icons) — MIT
