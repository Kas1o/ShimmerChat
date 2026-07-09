using ShimmerChatLib.Interface;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 提供 IToolV2 无参工具所需的全局服务引用。
    /// 在应用启动时由 Program.cs 设置一次。
    /// </summary>
    public static class ToolEnvironment
    {
        public static IKVDataService? KVData { get; set; }
    }
}
