using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using ShimmerChatLib.Generation;

namespace ShimmerChat.Components.SubComponents;

/// <summary>
/// Dynamically instantiates the correct editor for a node.
/// If the node has a registered [NodeEditor], that custom component is used.
/// Otherwise falls back to GenericNodeEditor&lt;T&gt;.
/// </summary>
public class NodeBodyRenderer : ComponentBase
{
    [Parameter] public required IGenerationNode Node { get; set; }
    [Parameter] public int Depth { get; set; }
    [Parameter] public EventCallback OnTreeChanged { get; set; }
    [Parameter] public Action<IGenerationNode>? RemoveMe { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var nodeType = Node.GetType();
        var editorAttr = nodeType.GetCustomAttributes(false)
            .OfType<NodeEditorAttribute>()
            .FirstOrDefault();

        Type componentType;
        if (editorAttr != null)
        {
            componentType = NodeEditorAttribute.Resolve(editorAttr.EditorTypeName) ?? typeof(GenericNodeEditor<>).MakeGenericType(nodeType);
        }
        else
        {
            componentType = typeof(GenericNodeEditor<>).MakeGenericType(nodeType);
        }

        builder.OpenComponent(0, componentType);
        builder.AddAttribute(1, "Node", Node);
        builder.AddAttribute(2, "Depth", Depth);
        builder.AddAttribute(3, "OnTreeChanged", OnTreeChanged);
        builder.AddAttribute(4, "RemoveMe", RemoveMe);
        builder.CloseComponent();
    }
}
