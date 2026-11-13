using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.CodeAct
{
    /// <summary>
    /// CodeAct 前生成节点：把 <see cref="CodeActToolV2"/> 加入本轮工具列表。
    /// <para>
    /// 必须通过本节点而不是通用工具节点创建工具：CodeAct 需要的是
    /// <c>TransientEnv.Tools</c> 的活列表引用（执行时枚举，才能看到同一轮里
    /// 本节点之后添加的工具），而 <c>IAutoCreateToolV2.Create(PersistentEnv)</c>
    /// 拿不到瞬态环境。
    /// </para>
    /// </summary>
    [NodeInfo("node.codeact",
        Icon = "🧩",
        Color = "var(--node-tool)",
        CategoryKeys = ["category.tool", "category.code"],
        DescriptionKey = "node.codeact.desc")]
    public class CodeActNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = "CodeAct C#";

        /// <summary>
        /// 是否允许脚本输出图像。关闭后：被调用工具返回的图像不会随结果回传，
        /// 脚本调用 AddImageAsync 会得到明确的 TOOL_IMAGE_OUTPUT_DISABLED 失败。
        /// </summary>
        [NodeProperty("prop.codeact.enable_image_output",
            HintKey = "prop.codeact.enable_image_output.hint", Order = 10)]
        public bool EnableImageOutput { get; set; } = true;

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            if (context.Env.Transient.Tools.OfType<CodeActToolV2>().Any())
            {
                var loc = context.Env.Persistent.LocService;
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.ParameterError,
                    loc["node_err.codeact_duplicate"],
                    nodeId: Id,
                    nodeName: Name));
            }

            // 传入的是活列表引用，不是快照：执行脚本时才枚举工具，
            // 因此同一轮里稍后添加的工具也能被脚本调用。
            context.Env.Transient.Tools.Add(new CodeActToolV2(
                context.Env.Transient.Tools,
                context.Env.Persistent,
                EnableImageOutput));

            return Task.FromResult(NodeResult.SuccessResult());
        }
    }
}
