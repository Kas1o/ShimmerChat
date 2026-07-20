namespace ShimmerChatLib.Generation
{
    public interface IToolRegistry
    {
        IReadOnlyList<ToolMetadata> AllTools { get; }
        ToolMetadata? FindByName(string name);
        ToolMetadata? FindByTypeName(string typeName);
        IAutoCreateToolV2? CreateInstance(string typeName, PersistentEnv env);
        IAutoCreateToolV2? CreateInstance(Type type, PersistentEnv env);
    }
}
