namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 可通过通用节点（ToolInstantiateNode / ToolPresetNode）自动创建的工具接口。
    /// 继承 <see cref="IToolV2"/>，额外提供 Name / Description 元数据
    /// 和 static abstract Create 工厂方法，调用时由节点传入当前 PersistentEnv。
    /// </summary>
    public interface IAutoCreateToolV2 : IToolV2
    {
        /// <summary>
        /// 工具名称（唯一标识），用于预设匹配和 UI 展示
        /// </summary>
        static abstract string Name { get; }

        /// <summary>
        /// 工具描述，用于 UI 展示
        /// </summary>
        static abstract string Description { get; }

        /// <summary>
        /// 分类路径，用于 UI 分组展示，如 "文件系统/读写"
        /// </summary>
        static abstract string CategoryPath { get; }

        /// <summary>
        /// 从持久化环境创建工具实例
        /// </summary>
        static abstract IAutoCreateToolV2 Create(PersistentEnv env);
    }
}
