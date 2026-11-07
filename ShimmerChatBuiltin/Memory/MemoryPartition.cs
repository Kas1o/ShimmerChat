namespace ShimmerChatBuiltin.Memory
{
    /// <summary>
    /// 记忆分区的唯一格式化入口。节点、工具与面板必须共用同一格式，
    /// 否则会出现"工具写了、面板看不到"的隐性不一致。
    /// </summary>
    public static class MemoryPartition
    {
        public static string ForAgent(Guid agentGuid) => agentGuid.ToString("D");
    }
}
