using Newtonsoft.Json;
using ShimmerChatLib.Interface;

namespace ShimmerChatLib.Generation;

/// <summary>
/// 三条生成管线的标识。仅用于代码内部分派，不直接持久化。
/// </summary>
public enum GenerationPipeline
{
    /// <summary>前生成管线（LLM 调用前构建上下文）。</summary>
    PreGeneration,

    /// <summary>后生成管线（LLM 响应后处理）。</summary>
    PostGeneration,

    /// <summary>渲染修改管线（消息显示前改写内容）。</summary>
    RenderModifier,
}

/// <summary>
/// 三条管线预设存储位置的唯一事实来源。
/// 管理器、CallNode 族、选项提供者与 Creator 工具集都必须经由本类读写，
/// 禁止在调用方再手写 ("SpaceId","StoreKey") 字符串形成平行复制表。
/// </summary>
public static class PipelinePresetStore
{
    /// <summary>获取指定管线预设列表在 KVData 中的存储位置。</summary>
    public static (string SpaceId, string StoreKey) GetLocation(GenerationPipeline pipeline) => pipeline switch
    {
        GenerationPipeline.PreGeneration => ("GenerationManager", "generation_presets"),
        GenerationPipeline.PostGeneration => ("PostGenerationManager", "post_generation_presets"),
        GenerationPipeline.RenderModifier => ("RenderModifierManager", "render_modifier_presets"),
        _ => throw new ArgumentOutOfRangeException(nameof(pipeline), pipeline, "Unknown pipeline."),
    };

    /// <summary>管线对应的业务节点接口（IPreGenerationNode 等）。</summary>
    public static Type NodeInterfaceType(GenerationPipeline pipeline) => pipeline switch
    {
        GenerationPipeline.PreGeneration => typeof(IPreGenerationNode),
        GenerationPipeline.PostGeneration => typeof(IPostGenerationNode),
        GenerationPipeline.RenderModifier => typeof(IRenderModifierNode),
        _ => throw new ArgumentOutOfRangeException(nameof(pipeline), pipeline, "Unknown pipeline."),
    };

    /// <summary>管线对应的根包装器类型。</summary>
    public static Type WrapperType(GenerationPipeline pipeline)
        => NodeWrapperTypes.Get(NodeInterfaceType(pipeline));

    /// <summary>管线对应的预设类型。</summary>
    public static Type PresetType(GenerationPipeline pipeline) => pipeline switch
    {
        GenerationPipeline.PreGeneration => typeof(PreGenerationPreset),
        GenerationPipeline.PostGeneration => typeof(PostGenerationPreset),
        GenerationPipeline.RenderModifier => typeof(RenderModifierPreset),
        _ => throw new ArgumentOutOfRangeException(nameof(pipeline), pipeline, "Unknown pipeline."),
    };

    /// <summary>
    /// 读取指定管线的全部预设。
    /// 存储缺失按「没有预设」处理；内容为 "null" 字面量时与管理器现有行为一致（按空列表处理）；
    /// JSON 结构损坏抛出 <see cref="JsonException"/>，由调用方转成显式失败，不允许静默吞掉。
    /// </summary>
    public static List<TPreset> ReadAll<TPreset>(IKVDataService kv, GenerationPipeline pipeline)
        where TPreset : class, IPipelinePreset
    {
        var (spaceId, storeKey) = GetLocation(pipeline);
        var json = kv.Read(spaceId, storeKey);
        if (string.IsNullOrEmpty(json))
            return [];

        return JsonConvert.DeserializeObject<List<TPreset>>(json) ?? [];
    }

    /// <summary>把指定管线的完整预设列表写回 KVData（整体覆盖，调用方自行保证读改写的原子性）。</summary>
    public static void WriteAll<TPreset>(IKVDataService kv, GenerationPipeline pipeline, List<TPreset> presets)
        where TPreset : class, IPipelinePreset
    {
        var (spaceId, storeKey) = GetLocation(pipeline);
        kv.Write(spaceId, storeKey, JsonConvert.SerializeObject(presets, Formatting.Indented));
    }
}
