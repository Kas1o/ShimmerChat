namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 可通过通用节点（ToolInstantiateNode / ToolPresetNode）自动创建的工具接口。
    /// 继承 <see cref="IToolV2"/>，额外提供 NameKey / DescriptionKey / CategoryKeys 元数据
    /// 和 static abstract Create 工厂方法，调用时由节点传入当前 PersistentEnv。
    /// 所有 *Key 属性均为本地化 Key，由 LocService 解析为显示字符串。
    /// </summary>
    public interface IAutoCreateToolV2 : IToolV2
    {
        /// <summary>
        /// 工具标识 Key，也用作本地化查找 Key（建议前缀 "tool.xxx"）
        /// </summary>
        static abstract string NameKey { get; }

        /// <summary>
        /// 工具描述本地化 Key（建议前缀 "tool.xxx.desc"）
        /// </summary>
        static abstract string DescriptionKey { get; }

        /// <summary>
        /// 分类路径本地化 Key 数组，每段为独立 Key（建议前缀 "category.xxx"）
        /// </summary>
        static abstract string[] CategoryKeys { get; }

        /// <summary>
        /// 从持久化环境创建工具实例
        /// </summary>
        static abstract IAutoCreateToolV2 Create(PersistentEnv env);
    }
}
