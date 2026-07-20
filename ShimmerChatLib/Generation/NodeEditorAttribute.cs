namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 标记节点对应的 Blazor 编辑器组件类型。
    /// 编辑器组件应与 Node 类放在同一个程序集中。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class NodeEditorAttribute : Attribute
    {
        /// <summary>编辑器组件的 Type</summary>
        public Type EditorType { get; }

        public NodeEditorAttribute(Type editorType)
        {
            EditorType = editorType;
        }
    }
}
