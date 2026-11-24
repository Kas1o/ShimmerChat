<div align="center">

# ✨ ShimmerChat 2 ✨

**An AI Chat App for Desktop Users Powered by Editable Node-Tree Pipelines — Orchestrate the Full LLM Generation Chain**

[![License](https://img.shields.io/github/license/Kas1o/ShimmerChat)](LICENSE.txt)
[![.NET](https://img.shields.io/badge/.NET-10-blue)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-Server-orange)](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)


</div>

> 📖 [中文版本](README.md)

---

## 🌟 Core Concept

ShimmerChat 2.0 is built around **three editable node-tree pipelines**. Each AI generation pass is split into three stages, each freely configurable by the user through a visual node editor:

```
Pre-Generation Tree ──→ LLM Generation ──→ Post-Generation Tree ──→ Render Modifier Tree ──→ Display
```

| Pipeline | Responsibility |
|----------|---------------|
| **Pre-Generation** | Build context: inject system prompts, chat history, tool declarations, select API |
| **Post-Generation** | Process raw LLM response: post-processing, formatting, result transformation |
| **Render Modifier** | Markdown → HTML render pipeline: math formulas, regex replacements, display modifications |

## 🌟 Features

| Feature | Description |
|---------|-------------|
| 🧩 **Visual Node Editor** | Drag-and-drop node tree editing, freely combine 22+ built-in nodes (conditionals, sub-agents, dynamic templates, tool presets, etc.) |
| 🤖 **Smart Agent System** | Each Agent has independent three-pipeline node tree configuration for differentiated behavior |
| 🔧 **Tool Call System** | Full Function Calling support, streaming Tool Call loop, automatic tool discovery and instantiation |
| 🧠 **SubAgent** | Nest other Agents within a node tree, building complex collaboration workflows |
| 📐 **Math Formulas** | Renders LaTeX in messages (`$...$`, `$$...$$`, `\(...\)`, `\[...\]`), MathML by default, KaTeX markup optional |
| 🎨 **Theme System** | Full theme CRUD + import/export, CSS variable driven |
| 💾 **Persistent Storage** | Everything lives under `Data/`; KV, messages, debug output and vectors each pick LiteDB or JSON files, with automatic migration |
| 🔌 **Plugin System** | Dynamic assembly loading, nodes/tools/panels all plugin-based, equal status with built-in features |
| 🌐 **Multi-language** | zh-CN / en-US localization, instant switching |
| 🖥️ **Desktop App** | Tauri v2 desktop shell, one-click packaging as native desktop application |
| ⚙️ **Multi-API Support** | OpenAI, Ollama, Kobold and more LLM APIs |

<div align="center">

### 🖼️ Screenshots

<img src="README/AgentChatPage2.0.png" alt="Chat Interface" />
> *Chat Interface*

<img src="README/AgentPage2.0.png" alt="Agent Configuration" />
> *Agent Configuration*

<img src="README/NodeSystem2.0.png" alt="Node Editor" />
> *Node Editor*

</div>

---

## 🛠️ Tech Stack

| Technology | Purpose |
|-----------|---------|
| **.NET 10** | Runtime framework |
| **Blazor Server** | Web application framework |
| **SharperLLM** | In-house LLM connection library (Tool Call, streaming, multi-API adaptation) |
| **LiteDB** | Embedded NoSQL database |
| **Markdig** | Markdown → HTML rendering |
| **KStyleMath** | LaTeX math rendering (pure C# port of KaTeX 0.19.1, no JS runtime) |
| **Newtonsoft.Json** | JSON serialization |
| **Tauri v2** | Desktop app packaging |
| **Bootstrap Icons** | UI icons |

---

## 📋 System Requirements

- **.NET 10 SDK** or later
- **OS**: Windows, Linux, macOS (macOS not thoroughly tested)

---

## 🚀 Quick Start

### Install .NET 10

- [Windows](https://learn.microsoft.com/en-us/dotnet/core/install/windows)
- [Linux](https://learn.microsoft.com/en-us/dotnet/core/install/linux)
- [macOS](https://learn.microsoft.com/en-us/dotnet/core/install/macos)

### Clone the Repository

```bash
git clone --recurse-submodules https://github.com/Kas1o/ShimmerChat.git
```

<details>
<summary>Forgot --recurse-submodules?</summary>

```bash
cd ShimmerChat
git submodule init
git submodule update
```

</details>

### Build & Run

```bash
cd ./ShimmerChat
dotnet run
```

---

## ⚙️ Configuration

### API Configuration

Configure your LLM backend on the "API Settings" page:

- **OpenAI API** — OpenAI-protocol-compatible APIs (URL, key, model)
- **Ollama API** — Local Ollama service
- **Kobold API** — Full Kobold parameters (temperature, context length, repetition penalty, samplers, etc.)

### Port Configuration

Add a Kestrel node in `appsettings.json`:

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

## 🔌 Plugin System

Place plugins in the `Plugins/` directory — they are automatically discovered and loaded at startup, with full parity with built-in features. Built-in extensions include:

| Module | Function |
|--------|----------|
| **DynPrompt** | Dynamic template prompt system |
| **Variable** | Variable definition and injection |
| **Memory** | Persistent memory storage |
| **SubAgent** | Sub-agent nesting call node |
| **FileSystem** | File read/write toolset |
| **RegexModifier** | Regex replacement render modifier |
| **InvokeCSharp** | Custom C# code execution |
| **CodeAct** | C# code execution that can call every tool in the round and output images |

---

## 🏗️ Project Structure

```
ShimmerChat/
├── SharperLLM/           # LLM connection library (submodule)
├── KStyleMath/           # LaTeX math rendering library (submodule, C# port of KaTeX)
├── ShimmerChatLib/       # Shared library: models, interfaces, pipeline core abstractions
├── ShimmerChat/          # Main project: Blazor Server host, service implementations, UI
├── ShimmerChatBuiltin/   # Built-in plugins: default nodes, tools, panels
├── src-tauri/            # Tauri desktop publishing project
└── Docs/                 # Developer documentation
```

---

## 📄 Third-Party Licenses

The following third-party components are direct dependencies or are distributed with the app. Transitive NuGet / Cargo dependencies remain under their own licenses. Exact versions are defined in the project files (`*.csproj`, `src-tauri/Cargo.lock`).

**Runtime and .NET libraries**

- [.NET 10 / ASP.NET Core](https://github.com/dotnet/aspnetcore) — MIT
- [SharperLLM](https://github.com/Kas1o/SharperLLM) — MIT
- [KStyleMath](https://github.com/Kas1o/KStyleMath) — MIT (C# port of KaTeX 0.19.1)
- [LiteDB](https://github.com/mbdavid/LiteDB) — MIT
- [Markdig](https://github.com/xoofx/markdig) — BSD-2-Clause
- [Meziantou.Framework.SyntaxHighlighting](https://github.com/meziantou/Meziantou.Framework) — MIT
- [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) — MIT
- [Microsoft.CodeAnalysis.CSharp / Microsoft.CodeAnalysis.CSharp.Scripting](https://github.com/dotnet/roslyn) — MIT
- [ModelContextProtocol](https://github.com/modelcontextprotocol/csharp-sdk) (including `ModelContextProtocol.Core`) — Apache-2.0
- [Tokenizers.HuggingFace](https://github.com/IgnaciodelaTorreArias/Tokenizers.HuggingFace.DotNet) — Apache-2.0 (transitive Google.Protobuf is BSD-3-Clause)
- [SkiaSharp](https://github.com/mono/SkiaSharp) (including `SkiaSharp.NativeAssets.Linux.NoDependencies`) — MIT (native Skia is BSD-3-Clause)

**Desktop shell (Tauri / Rust)**

- [Tauri](https://github.com/tauri-apps/tauri), `tauri-plugin-shell`, `tauri-build` — MIT or Apache-2.0
- [`serde`](https://github.com/serde-rs/serde), `serde_json` — MIT or Apache-2.0
- [`tokio`](https://github.com/tokio-rs/tokio) — MIT

**Front-end static assets**

- [KaTeX](https://github.com/KaTeX/KaTeX) (`ShimmerChat/wwwroot/katex`) — code and CSS under MIT; fonts under `fonts/` are SIL OFL 1.1
- [Bootstrap Icons](https://github.com/twbs/icons) (`ShimmerChat/wwwroot/lib/bootstrap-icons`) — MIT

**Build and test tooling (not distributed with the app)**

- [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning) — MIT
- [xunit](https://github.com/xunit/xunit) / `xunit.runner.visualstudio` — Apache-2.0
- [Moq](https://github.com/moq/moq) — BSD-3-Clause
- [FluentAssertions](https://github.com/fluentassertions/fluentassertions) (6.x) — Apache-2.0
- [bUnit](https://github.com/bUnit-dev/bUnit) — MIT
- [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) — MIT

> KaTeX's MIT license covers only its code and CSS; the fonts are distributed under the SIL Open Font License 1.1. Full license texts are available from the linked repositories or the NuGet / crates.io packages.
