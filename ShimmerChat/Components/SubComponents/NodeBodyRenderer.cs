using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using ShimmerChatLib.Generation;

namespace ShimmerChat.Components.SubComponents;

/// <summary>
/// Non-generic bridge that instantiates <see cref="GenericNodeEditor{TNode}"/>
/// from an <see cref="IGenerationNode"/> parameter.
/// Custom editor resolution is handled by <see cref="TreeEditor"/>.
/// </summary>
public class NodeBodyRenderer : ComponentBase
{
    [Parameter] public required IGenerationNode Node { get; set; }
    [Parameter] public int Depth { get; set; }
    [Parameter] public EventCallback OnTreeChanged { get; set; }
    [Parameter] public Action<IGenerationNode>? RemoveMe { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var componentType = typeof(GenericNodeEditor<>).MakeGenericType(Node.GetType());

        builder.OpenComponent(0, componentType);
        builder.AddAttribute(1, "Node", Node);
        builder.AddAttribute(2, "Depth", Depth);
        builder.AddAttribute(3, "OnTreeChanged", OnTreeChanged);
        builder.AddAttribute(4, "RemoveMe", RemoveMe);
        builder.CloseComponent();
    }
}
