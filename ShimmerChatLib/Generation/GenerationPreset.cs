namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 三种管线预设的公共读写契约。三个预设类结构完全一致但历史类型独立，
    /// 提取接口让泛型代码（<see cref="PipelinePresetStore"/>、Creator 工具集）
    /// 可以统一操作而不必维护平行的复制表。
    /// </summary>
    public interface IPipelinePreset
    {
        string Id { get; set; }
        string Name { get; set; }
        string RootNodeJson { get; set; }
        bool Pinned { get; set; }
    }

    /// <summary>
    /// 预生成预设：一个可复用的节点树模板
    /// </summary>
    public class PreGenerationPreset : IPipelinePreset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Preset";

        /// <summary>
        /// 根节点的 JSON 序列化
        /// </summary>
        public string RootNodeJson { get; set; } = "{}";

        /// <summary>收藏标记；只影响预设选择器与检索列表的排序。</summary>
        public bool Pinned { get; set; }
    }

    /// <summary>
    /// 后生成预设：一个可复用的后生成节点树模板
    /// </summary>
    public class PostGenerationPreset : IPipelinePreset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Preset";

        /// <summary>
        /// 根节点的 JSON 序列化
        /// </summary>
        public string RootNodeJson { get; set; } = "{}";

        /// <summary>收藏标记；只影响预设选择器与检索列表的排序。</summary>
        public bool Pinned { get; set; }
    }

    /// <summary>
    /// 渲染修改预设：一个可复用的渲染修改节点树模板
    /// </summary>
    public class RenderModifierPreset : IPipelinePreset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Preset";

        /// <summary>
        /// 根节点的 JSON 序列化
        /// </summary>
        public string RootNodeJson { get; set; } = "{}";

        /// <summary>收藏标记；只影响预设选择器与检索列表的排序。</summary>
        public bool Pinned { get; set; }
    }
}
