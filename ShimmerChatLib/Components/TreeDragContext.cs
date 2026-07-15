using System.Collections;
using ShimmerChatLib.Generation;

namespace ShimmerChatLib.Components;

/// <summary>
/// Holds the transient state of an ongoing drag-and-drop operation within the tree editor.
/// Passed via CascadingValue so every TreeEditor and GenericNodeEditor in the tree shares the same instance.
/// Uses non-generic <see cref="IList"/> to work with any ITreeNode list (List&lt;IPreGenerationNode&gt; etc.).
/// </summary>
public class TreeDragContext
{
    /// <summary>The node currently being dragged.</summary>
    public ITreeNode? DraggedNode { get; private set; }

    /// <summary>
    /// The list the dragged node currently belongs to — we remove from here on successful drop.
    /// Null for single-node slots where removal is handled by <see cref="_removeAction"/>.
    /// </summary>
    public IList? SourceList { get; private set; }

    /// <summary>
    /// Callback to invoke on the source component after the node is removed, so it can re-render.
    /// </summary>
    public Func<Task>? SourceNotify { get; private set; }

    /// <summary>
    /// For single-node slots: action to remove the node from its parent property.
    /// </summary>
    private Func<Task>? _removeAction;

    /// <summary>Whether a drag operation is currently in progress.</summary>
    public bool IsDragging => DraggedNode != null;

    /// <summary>Called by TreeEditor on ondragstart for list-based nodes.</summary>
    public void BeginDrag(ITreeNode node, IList sourceList, Func<Task> sourceNotify)
    {
        DraggedNode = node;
        SourceList = sourceList;
        SourceNotify = sourceNotify;
        _removeAction = null;
    }

    /// <summary>Called by TreeEditor on ondragstart for single-slot nodes.</summary>
    public void BeginDrag(ITreeNode node, Func<Task> removeAction, Func<Task> sourceNotify)
    {
        DraggedNode = node;
        SourceList = null;
        SourceNotify = sourceNotify;
        _removeAction = removeAction;
    }

    /// <summary>
    /// Move the dragged node from its source list into <paramref name="targetList"/>
    /// at <paramref name="insertIndex"/>. Returns true on success.
    /// </summary>
    public async Task<bool> CommitDrop(IList targetList, int insertIndex)
    {
        if (DraggedNode == null)
            return false;

        // Prevent dropping a node into its own descendant subtree
        if (IsDescendantOf(DraggedNode, targetList))
            return false;

        if (SourceList != null)
        {
            bool sameList = ReferenceEquals(targetList, SourceList);
            int sourceIndex = sameList ? SourceList.IndexOf(DraggedNode) : -1;

            if (sameList)
            {
                if (insertIndex == sourceIndex || insertIndex == sourceIndex + 1)
                    return false;
                if (insertIndex > sourceIndex)
                    insertIndex--;
            }

            SourceList.Remove(DraggedNode);
        }
        else if (_removeAction != null)
        {
            await _removeAction();
        }
        else
        {
            return false;
        }

        if (insertIndex >= targetList.Count)
            targetList.Add(DraggedNode);
        else
            targetList.Insert(insertIndex, DraggedNode);

        if (SourceNotify != null)
            await SourceNotify();

        EndDrag();
        return true;
    }

    /// <summary>Called by TreeEditor on ondragend. Cancels the drag if not committed.</summary>
    public void EndDrag()
    {
        DraggedNode = null;
        SourceList = null;
        SourceNotify = null;
        _removeAction = null;
    }

    /// <summary>Remove the dragged node from its source (list or single-slot).</summary>
    public async Task RemoveFromSource()
    {
        if (DraggedNode == null) return;

        if (SourceList != null)
        {
            SourceList.Remove(DraggedNode);
        }
        else if (_removeAction != null)
        {
            await _removeAction();
        }

        if (SourceNotify != null)
            await SourceNotify();
    }

    /// <summary>
    /// Checks whether <paramref name="node"/> contains <paramref name="targetList"/>
    /// anywhere in its descendant hierarchy.
    /// </summary>
    private static bool IsDescendantOf(ITreeNode node, IList targetList)
    {
        return ContainsTargetList(node, targetList, new HashSet<string>());
    }

    private static bool ContainsTargetList(ITreeNode node, IList target, HashSet<string> visited)
    {
        if (!visited.Add(node.Id))
            return false;

        foreach (var prop in node.GetType().GetProperties())
        {
            if (!prop.CanRead) continue;

            if (TreeNodeReflection.IsListOfTreeNode(prop))
            {
                var list = prop.GetValue(node) as IList;
                if (ReferenceEquals(list, target))
                    return true;
                if (list != null)
                {
                    foreach (var child in list)
                    {
                        if (child is ITreeNode treeChild && ContainsTargetList(treeChild, target, visited))
                            return true;
                    }
                }
            }
            else if (TreeNodeReflection.IsSingleTreeNode(prop))
            {
                var child = prop.GetValue(node) as ITreeNode;
                if (child != null && ContainsTargetList(child, target, visited))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether <paramref name="ancestor"/> is an ancestor of <paramref name="descendant"/>.
    /// </summary>
    public static bool IsAncestorOf(ITreeNode ancestor, ITreeNode descendant)
    {
        return ContainsNode(ancestor, descendant, new HashSet<string>());
    }

    private static bool ContainsNode(ITreeNode container, ITreeNode target, HashSet<string> visited)
    {
        if (!visited.Add(container.Id))
            return false;

        foreach (var prop in container.GetType().GetProperties())
        {
            if (!prop.CanRead) continue;

            if (TreeNodeReflection.IsListOfTreeNode(prop))
            {
                var list = prop.GetValue(container) as IList;
                if (list != null)
                {
                    foreach (var child in list)
                    {
                        if (ReferenceEquals(child, target))
                            return true;
                        if (child is ITreeNode treeChild && ContainsNode(treeChild, target, visited))
                            return true;
                    }
                }
            }
            else if (TreeNodeReflection.IsSingleTreeNode(prop))
            {
                var child = prop.GetValue(container) as ITreeNode;
                if (child != null)
                {
                    if (ReferenceEquals(child, target))
                        return true;
                    if (ContainsNode(child, target, visited))
                        return true;
                }
            }
        }

        return false;
    }
}
