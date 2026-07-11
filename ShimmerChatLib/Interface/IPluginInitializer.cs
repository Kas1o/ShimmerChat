namespace ShimmerChatLib.Interface
{
    /// <summary>
    /// 插件初始化接口。实现此接口的类会在启动时由宿主自动发现并调用，
    /// 确保插件在首次使用前完成必要的初始化（如写入默认 KVData 等）。
    /// 依赖通过构造函数注入。
    /// </summary>
    public interface IPluginInitializer
    {
        Task InitializeAsync();
    }
}
