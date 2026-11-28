using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.NodeBasic.Render
{
    /// <summary>
    /// 按渲染位置分支。用于让同一棵渲染树对正文、思维链等区域执行不同的节点，
    /// 例如默认树只对正文执行 Markdown 渲染。
    /// </summary>
    [NodeInfo("node.render_if",
        Icon = "◇",
        Color = "var(--node-branch)",
        CategoryKeys = ["category.render", "category.flow", "category.branching"],
        DescriptionKey = "node.render_if.desc")]
    public class RenderIfNode : IRenderModifierNode
    {
        /// <summary>命中该位置时执行 <see cref="Then"/>，否则执行 <see cref="Else"/>。</summary>
        [NodeProperty("prop.render_if.position", HintKey = "prop.render_if.position.hint", Order = 1)]
        public RenderPosition Position { get; set; } = RenderPosition.Body;

        [NodeProperty("prop.render_if.then", Order = 2)]
        public RenderModifierNodeWrapper? Then { get; set; }

        [NodeProperty("prop.render_if.else", Order = 3)]
        public RenderModifierNodeWrapper? Else { get; set; }

        public void Execute(RenderNodeExecutionContext context)
        {
            var branch = context.Position == Position ? Then : Else;
            branch?.Execute(context);
        }
    }
}
