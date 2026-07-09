namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 标记节点对应的 Blazor 编辑器组件类型。
    /// 使用字符串以避免跨程序集类型引用问题。
    /// 在应用启动时调用 NodeEditorAttribute.RegisterEditor 注册实际类型。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class NodeEditorAttribute : Attribute
    {
        /// <summary>编辑器组件的完全限定类型名 (如 "MyApp.Components.MyEditor")</summary>
        public string EditorTypeName { get; }

        public NodeEditorAttribute(string editorTypeName)
        {
            EditorTypeName = editorTypeName;
        }

        /// <summary>全局类型注册表：类型名 → Type</summary>
        private static readonly Dictionary<string, Type> _registry = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>注册编辑器类型（在应用启动时调用）</summary>
        public static void RegisterEditor(string typeName, Type editorType)
        {
            if (!typeof(Microsoft.AspNetCore.Components.IComponent).IsAssignableFrom(editorType))
                throw new ArgumentException($"{editorType.FullName} must implement IComponent");
            _registry[typeName] = editorType;
        }

        /// <summary>获取已注册的编辑器类型</summary>
        public static Type? Resolve(string typeName)
        {
            return _registry.TryGetValue(typeName, out var t) ? t : null;
        }
    }
}
