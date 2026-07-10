此文件是由人类写的。

把静态 CSS 挪到 同名文件。

完成文档，比如如何自定义一个GenerationNode。

进一步的，我们需要完善程序文档注释。这个项目注释率实在有点低了。

还有各种杂项问题，比如 Tool Manager 的预设管理能力欠缺。也没有默认预设，（默认的生成环境修改器预设也需要适配默认预设，用类似 API Select 节点的那套）

测试资源太匮乏了，很多东西都在靠人类手动测试。

DynPrompt 已经被删了，改成直接 Fragment 序列。 但目前还不支持递归搜寻， 需要结合 Fragment 给消息添加元信息（目前没有这个能力）加上 Sequence Repeat。 （更新：把DynPrompt 加回来，Fragment 作为纯Append，顺便调整一下，对话记录也改成节点插入）

本地化需要改，之前用的 IStringLocalizer + XML 资源，不太好用。

Agent 基础设施匮乏，需要把项目总览和各个子模块的 SKILL 做出来。

ContextModifier 没删完， ShimmerChatLib\Components\ConfigEditor.razor 之类的周边也没删完

SubAgent 没做 GenerationConfig 适配。

IfNode 根本没做