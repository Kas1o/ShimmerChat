using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 在终端打印当前瞬态状态（TransientEnv）的调试节点。
    /// 消息内容只显示前 20 字符和后 20 字符，避免输出过长。
    /// </summary>
    [NodeInfo("node.transient_probe", Icon = "◉", Color = "var(--node-branch)", CategoryKeys = ["category.debug"])]
    public class TransientProbeNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Transient Probe";

        [NodeProperty("prop.transient_probe.print_fragments", HintKey = "prop.transient_probe.print_fragments.hint")]
        public bool PrintFragments { get; set; } = true;

        [NodeProperty("prop.transient_probe.print_shared_state", HintKey = "prop.transient_probe.print_shared_state.hint")]
        public bool PrintSharedState { get; set; } = true;

        [NodeProperty("prop.transient_probe.print_tools", HintKey = "prop.transient_probe.print_tools.hint")]
        public bool PrintTools { get; set; } = true;

        [NodeProperty("prop.transient_probe.print_api", HintKey = "prop.transient_probe.print_api.hint")]
        public bool PrintAPI { get; set; } = true;

        public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            var transient = context.Env.Transient;
            var output = context.Env.Persistent.DebugOutput;
            var source = nameof(TransientProbeNode);

            output.Write(source, "info", "===== TransientProbe =====");

            if (PrintFragments)
            {
                output.Write(source, "info", $"Fragments ({transient.Fragments.Count}):");

                for (int i = 0; i < transient.Fragments.Count; i++)
                {
                    var seg = transient.Fragments[i];
                    var content = seg.Message.Content ?? "";
                    string display;

                    if (content.Length > 40)
                        display = content[..20] + "..." + content[^20..];
                    else
                        display = content;

                    output.Write(source, "info", $"  [{i}] From={seg.From}, Source={seg.SourceType?.Name ?? "(none)"}");

                    if (seg.Metadata.Count > 0)
                        output.Write(source, "info", $"       Meta: {string.Join("; ", seg.Metadata.Select(kv => $"{kv.Key}={kv.Value}"))}");

                    output.Write(source, "info", $"       Content: \"{display}\"");
                }
            }

            if (PrintSharedState)
                output.Write(source, "info", $"SharedState keys: {string.Join(", ", transient.SharedState.Keys)}");

            if (PrintTools)
                output.Write(source, "info", $"Tools ({transient.Tools.Count}): {string.Join(", ", transient.Tools.Select(t => t.GetType().Name))}");

            if (PrintAPI)
                output.Write(source, "info", $"API: {(transient.API != null ? "set" : "null")}");

            output.Write(source, "info", "===== End TransientProbe =====");

            return Task.FromResult(NodeResult.SuccessResult());
        }
    }
}
