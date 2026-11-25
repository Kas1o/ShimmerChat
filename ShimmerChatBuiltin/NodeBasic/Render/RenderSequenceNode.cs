using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.NodeBasic.Render
{
    [NodeInfo("node.render_sequence",
        Icon = "▶",
        Color = "var(--node-sequence)",
        CategoryKeys = ["category.flow"],
        DescriptionKey = "node.render_sequence.desc")]
    public class RenderSequenceNode : IRenderModifierNode
    {


        [NodeProperty("prop.sequence.children", Order = 100)]
        public List<RenderModifierNodeWrapper> Children { get; set; } = new();

        /// <summary>
        /// 缓存整段子树的输出。启用后，同一份输入、同一棵渲染树、同一语言下命中缓存会直接
        /// 跳过 children；只应对确定性节点开启，否则会显示过期内容。
        /// </summary>
        [NodeProperty("prop.sequence.cache_output",
            HintKey = "prop.sequence.cache_output.hint", Order = 110)]
        public bool CacheOutput { get; set; }

        /// <summary>
        /// 缓存子树的渲染失败。默认关闭，避免把临时故障（外部文件、网络、配置）固定下来；
        /// 开启后同一 key 的后续渲染会直接重抛缓存的错误，不再执行 children。
        /// </summary>
        [NodeProperty("prop.sequence.cache_errors",
            HintKey = "prop.sequence.cache_errors.hint", Order = 120)]
        public bool CacheErrors { get; set; }

        public void Execute(RenderNodeExecutionContext context)
        {
            if (!CacheOutput && !CacheErrors)
            {
                ExecuteChildren(context);
                return;
            }

            // 没有包装器提供的稳定 Id 时无法区分两个同输入、不同配置的 Sequence，
            // 直接执行而不缓存。
            var nodeId = context.CurrentNodeId;
            if (string.IsNullOrEmpty(nodeId))
            {
                ExecuteChildren(context);
                return;
            }

            var key = new RenderSequenceCache.Key(
                nodeId,
                context.Env.GetContent() ?? string.Empty,
                context.Env.Loc?.CurrentCulture,
                context.Env.TreeRevision);

            if (RenderSequenceCache.TryGet(key, out var cached))
            {
                if (cached.Failure != null)
                {
                    if (CacheErrors)
                        throw new RenderNodeException(
                            cached.Failure.Code,
                            cached.Failure.Message,
                            cached.Failure.NodeId,
                            cached.Failure.NodeName);

                    // 用户关掉了错误缓存：旧的失败条目不再使用，继续实际执行。
                }
                else if (cached.Html != null && CacheOutput)
                {
                    context.Env.UpdateContent(
                        cached.Html,
                        context.CurrentNodeName ?? GetType().Name,
                        GetType().Name);
                    return;
                }
            }

            try
            {
                ExecuteChildren(context);
            }
            catch (RenderNodeException ex)
            {
                if (CacheErrors && context.Env.AllowCacheWrites)
                    RenderSequenceCache.SetFailure(key, ex);
                throw;
            }

            if (CacheOutput && context.Env.AllowCacheWrites)
                RenderSequenceCache.SetHtml(key, context.Env.GetContent());
        }

        private void ExecuteChildren(RenderNodeExecutionContext context)
        {
            foreach (var child in Children)
                child.Execute(context);
        }
    }
}
