using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using ShimmerChatLib.Generation;

namespace ShimmerChatLib.Components;

/// <summary>
/// Non-generic bridge that instantiates <see cref="GenericNodeEditor{TNode}"/>
/// from an <see cref="ITreeNode"/> wrapper parameter. The generated form edits the
/// wrapper's <see cref="ITreeNode.Payload"/>; the wrapper itself is not a node type.
/// Custom editor resolution is handled by <see cref="TreeEditor"/>.
/// </summary>
public class NodeBodyRenderer : ComponentBase
{
    [Parameter] public required ITreeNode Node { get; set; }
    [Parameter] public int Depth { get; set; }
    [Parameter] public Action<ITreeNode>? RemoveMe { get; set; }
    [Parameter] public Action<ITreeNode>? CopyMe { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (Node.Payload is not { } payload)
            return;

        var componentType = typeof(GenericNodeEditor<>).MakeGenericType(payload.GetType());

        builder.OpenComponent(0, componentType);
        builder.AddAttribute(1, "Node", payload);
        builder.AddAttribute(2, "Depth", Depth);
        builder.AddAttribute(3, "RemoveMe", RemoveMe);
        builder.AddAttribute(4, "CopyMe", CopyMe);
        builder.CloseComponent();
    }
}
