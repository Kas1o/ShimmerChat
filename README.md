# ShimmerChat

## 开跑？

### 在跑之前……

安装 dotnet。

对于 Windows 用户：
* https://learn.microsoft.com/en-us/dotnet/core/install/windows

对于 Linux 用户：
* https://learn.microsoft.com/en-us/dotnet/core/install/linux

对于 MacOS 用户：
* https://learn.microsoft.com/en-us/dotnet/core/install/macos

> 注意：MacOS未经测试。

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
（注意，是要在项目根目录下面的ShimmerChat目录里面dotnet run）

## 第三方许可
[Markdig](https://github.com/xoofx/markdig) BSD-2-Clause license

[Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) MIT license

[qdrant-dotnet](https://github.com/qdrant/qdrant-dotnet) Apache-2.0 license

[SharperLLM](https://github.com/Kas1o/SharperLLM) MIT license
