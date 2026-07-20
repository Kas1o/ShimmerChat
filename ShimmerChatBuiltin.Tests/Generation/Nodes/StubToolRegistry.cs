namespace ShimmerChatBuiltin.Tests.Generation.Nodes;

/// <summary>
/// IToolRegistry 测试桩，允许手动注册工具元数据和实例
/// </summary>
public class StubToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ToolMetadata> _byName = new();
    private readonly Dictionary<string, IToolV2> _instancesByTypeName = new();
    private readonly Dictionary<Type, IToolV2> _instancesByType = new();

    public IReadOnlyList<ToolMetadata> AllTools => _byName.Values.ToList().AsReadOnly();

    public StubToolRegistry Register(string nameKey, Type type)
    {
        var meta = new ToolMetadata(nameKey, $"{nameKey}.desc", [], type);
        _byName[nameKey] = meta;
        return this;
    }

    public StubToolRegistry SetInstance(string typeName, IToolV2 instance)
    {
        _instancesByTypeName[typeName] = instance;
        return this;
    }

    public StubToolRegistry SetInstance(Type type, IToolV2 instance)
    {
        _instancesByType[type] = instance;
        return this;
    }

    public ToolMetadata? FindByName(string name) =>
        _byName.TryGetValue(name, out var meta) ? meta : null;

    public ToolMetadata? FindByTypeName(string typeName) =>
        _byName.Values.FirstOrDefault(t => t.TypeName == typeName);

    public IAutoCreateToolV2? CreateInstance(string typeName, PersistentEnv env)
    {
        if (_instancesByTypeName.TryGetValue(typeName, out var tool) && tool is IAutoCreateToolV2 ac)
            return ac;
        return null;
    }

    public IAutoCreateToolV2? CreateInstance(Type type, PersistentEnv env)
    {
        if (_instancesByType.TryGetValue(type, out var tool) && tool is IAutoCreateToolV2 ac)
            return ac;
        return null;
    }
}
