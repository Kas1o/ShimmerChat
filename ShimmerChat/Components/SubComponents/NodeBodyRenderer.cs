using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using ShimmerChatLib.Generation;

namespace ShimmerChat.Components.SubComponents;

/// <summary>
/// Non-generic wrapper that dynamically instantiates GenericNodeEditor&lt;T&gt;
/// for the concrete node type using BuildRenderTree.
/// </summary>
public class NodeBodyRenderer : ComponentBase
{
    [Parameter] public required IGenerationNode Node { get; set; }
    [Parameter] public int Depth { get; set; }
    [Parameter] public EventCallback OnTreeChanged { get; set; }
    [Parameter] public Action<IGenerationNode>? RemoveMe { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var genericType = typeof(GenericNodeEditor<>).MakeGenericType(Node.GetType());

        builder.OpenComponent(0, genericType);
        builder.AddAttribute(1, "Node", Node);
        builder.AddAttribute(2, "Depth", Depth);
        builder.AddAttribute(3, "OnTreeChanged", OnTreeChanged);
        builder.AddAttribute(4, "RemoveMe", RemoveMe);
        builder.CloseComponent();
    }
}
