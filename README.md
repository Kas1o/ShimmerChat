<div align="center">

# ✨ ShimmerChat ✨

**一个功能丰富的 AI 聊天应用，支持多种 LLM API 接入**

[![License](https://img.shields.io/github/license/Kas1o/ShimmerChat)](LICENSE.txt)
[![.NET](https://img.shields.io/badge/.NET-10-blue)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-Server-orange)](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)

</div>

---

## 🌟 功能特性

| 特性 | 描述 |
|------|------|
| 🤖 **多 API 支持** | 支持 Kobold、OpenAI、Ollama 等多种 LLM API |
| 🎭 **智能代理系统** | 可创建和管理多个 AI 代理，每个代理可配置不同角色和设置 |
| 💾 **聊天历史管理** | 完整的聊天记录保存和管理功能 |
| 🎨 **主题切换** | 支持深色/浅色主题切换 |
| 🔌 **插件系统** | 内置插件系统，支持动态加载和扩展功能 |
| 🌐 **多语言支持** | 支持中英文界面 |
| 🖼️ **自定义背景** | 支持为代理设置自定义背景和头像 |
| ⚙️ **对话配置** | 丰富的对话参数配置选项（温度、最大长度、重复惩罚等） |

<div align="center">

### 🖼️ 界面预览

<img width="3840" height="2160" alt="image" src="https://github.com/user-attachments/assets/2fd4dfe7-52ec-4585-a09c-393d23768ed1" />
> *主界面 - 欢迎页面，显示当前时间*

<img width="3840" height="2160" alt="image" src="https://github.com/user-attachments/assets/f6b1944e-49ac-4aac-ad1b-f23f8286d292" />
> *API 配置页面 - 支持多种 LLM 服务配置*

<img width="3840" height="2160" alt="image" src="https://github.com/user-attachments/assets/f45b0eb3-7a12-4710-873a-fdb2f32263c1" />
> *插件管理页面 - 创建和管理不同的 AI 插件*

</div>

---

## 🛠️ 技术栈

<div align="center">

| 技术 | 描述 |
|------|------|
| **.NET 10** | 底层运行时框架 |
| **Blazor Server** | Web 应用框架 |
| **SharperLLM** | 自研 LLM 多功能工具库 |
| **Markdig** | Markdown 渲染 |
| **Newtonsoft.Json** | JSON 序列化 |
| **Bootstrap Icons** | UI 图标 |

</div>

---

## 📋 系统要求

- **.NET 10 SDK** 或更高版本
- **操作系统**: Windows, Linux, 或 macOS (未经测试)

---

## 🚀 安装说明

### 在跑之前……

安装 .NET 10。

- **Windows 用户**: [下载 .NET 10](https://learn.microsoft.com/en-us/dotnet/core/install/windows)
- **Linux 用户**: [下载 .NET 10](https://learn.microsoft.com/en-us/dotnet/core/install/linux)  
- **macOS 用户**: [下载 .NET 10](https://learn.microsoft.com/en-us/dotnet/core/install/macos)

> ⚠️ 注意：macOS 未经测试。

### 克隆仓库以及子模块
```bash
git clone --recurse-submodules https://github.com/Kas1o/ShimmerChat.git
```

<details>
<summary>我没用 --recurse-submodules 怎么办？</summary>

先 <code>cd ShimmerChat</code> 到目录里，<code>git submodule init</code> 初始化子模块，<code>git submodule update</code> 拉取子模块。

</details>

### 构建运行

```bash
cd ./ShimmerChat
dotnet run
```

> 💡 **提示**: 请在项目根目录下的 ShimmerChat 目录中执行 `dotnet run`

---

## ⚙️ 配置说明

### API 配置
ShimmerChat 支持多种 LLM API，您可以在 "API 设置" 页面进行配置：

- **Kobold API**: 支持完整的 Kobold 参数配置，包括温度、上下文长度、重复惩罚、采样参数等
- **OpenAI API**: 支持 OpenAI 兼容的 API 配置，包括 URL、密钥、模型等
- **Ollama API**: 支持本地 Ollama 服务接入

### 端口配置
如果需要更改默认端口（5000），请在 `appsettings.json` 中添加 Kestrel 配置节点：

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:你的端口号"
      }
    }
  }
}
```

---

## 🔌 插件系统

ShimmerChat 提供了灵活的插件系统，自带了以下功能扩展：

- 🧠 **动态提示（DynPrompt）**: 智能动态提示系统
- 📝 **变量管理**: 灵活的变量注入和管理
- 🧠 **记忆注入**: 持久化记忆功能
- 🧪 **自定义 C# 代码执行**: 运行自定义代码片段
- 📄 **消息打印和格式化**: 高级消息处理功能

---

## 📄 第三方许可

- [Markdig](https://github.com/xoofx/markdig) - BSD-2-Clause license
- [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) - MIT license
- [qdrant-dotnet](https://github.com/qdrant/qdrant-dotnet) - Apache-2.0 license
- [SharperLLM](https://github.com/Kas1o/SharperLLM) - MIT license
