# ShimmerChat 2

最开始做 ShimmerChat 的时候，更多抱着对 SillyTavern 的不满，有太多的方式通过不透明的组合完成同一件事情，希望通过一个统一的抽象，也就是贯穿始终的 “上下文修改器” 对此予以抽象，并统一显式管理。

但随着项目的发展，加入了越来越多 “SillyTavern 本就没有的功能”，一切又变得糟糕起来了。

## 下决定的原因

最近我在思考 SubAgentCall，通过工具调用主动唤起 SubAgent。

很自然的有一个问题：能调用谁？

被调用方很简单，加个字段标记可被ToolCall调用就好了。

调用方能调用的列表……

谁来定义？

Agent 里面定义？       SubAgent 属于内置插件，不能让主体支持

上下文修改器里面定义？ 目前抽象不支持，而且好怪啊。

AgentPluginsPanel 里面定义？ 感觉还行，Agent 和 SubAgent 都能用。不过有些麻烦

嗯……等等，这样我们 Agent 自己能调用的SubAgent怎么和预设定义组合？

## 全局配置自身的不透明性

我发现我没有一个统一的层来处理配置间的组合，用一个普通的 KVData 来维护本来就很痛苦。

或者换个说法，生成环境修改器，用于替换原有的上下文修改器。

我对此感到兴奋，

是不是连 Agent 定义本身也不需要放在核心了。

它只是一个普通的数据层而已。

比如一个基础结构:

```plain
SystemPrompt -> ToolDefinition -> APIDefinition
```

SystemPrompt 在流程里面、Tool在流程里面、甚至连API配置都在里面（当然这不算安全）。

这意味着能让 Tool定义依赖上下文，让API定义依赖上下文（通常用于构建路由系统）。

到了这时候，我意识到这东西改变太大了，得做不兼容更新，所以我将其视为 ShimmerChat2.0。

## 大概会长什么样子？

2.0 最重要的抽象是生成环境修改器，首先我们需要定义生成环境，这点很基础，把刚才写的东西加进去就好了。

```plain
TransientEnv {
    Fragments : List
    Tools : List
    API : API
    SharedState : Dict
}
```

```plain
PersistentEnv {
    KVData
    ChatGuid
    AgentGuid
}
```

随后是修改时机，之前讲的SubAgentCall定义就有一个问题—— 你怎么给工具传上下文修改器共享的状态，我的想法是把工具不再做成单例的实例了，而是改成生成环境修改器阶段被实例化添加到列表中。

修改器原来是单纯的列表，我觉得这有些不够方便，改成树结构，同时每个节点带有可自由折叠的子视图

```plain
Sequence(Root):
    nodes(List) = [
        IfNode:
            Cond(string) = " kv['xxx'] == 123 "
            Then(Node) =
                Sequence:
                    ...
            Else(Node) =
                Call:
                    preset(GenerationModifersPreset) = ...
                    
    ]
```

这样处理流程就变成了一个DSL。

我会将 Agent 更新至 2.0，让它不再持有大部分生成相关定义。转而持有一个私有的修改器树。不能被其他树所调用，当作一个最终的根节点。

## 主页面的变更

```diff
主页
- API设置
- 上下文管理器
- ToolManager
+ 生成管理器
智能体
- Plugin Settings
+ Misc
+ ========Representation========
消息渲染
- Misc
主题管理
+ =========PluginPanels=========
+ API设置
+ ToolManager
+ (其余的各种Plugin Panel)
```

因为大部分与生成相关的全局配置都被抽象出去了，导致大部分核心无关的都被挪到了下面。
