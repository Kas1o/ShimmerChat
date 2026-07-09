using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 在终端打印当前瞬态状态（TransientEnv）的调试节点。
    /// 消息内容只显示前 20 字符和后 20 字符，避免输出过长。
    /// </summary>
    [NodeInfo("Transient Probe", Icon = "◉", Color = "#f0a030", Category = "Debug")]
    public class TransientProbeNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Transient Probe";

        [NodeProperty("Print Fragments", Hint = "Show context fragments")]
        public bool PrintFragments { get; set; } = true;

        [NodeProperty("Print SharedState", Hint = "Show shared state keys")]
        public bool PrintSharedState { get; set; } = true;

        [NodeProperty("Print Tools", Hint = "Show available tools")]
        public bool PrintTools { get; set; } = true;

        [NodeProperty("Print API", Hint = "Show API status")]
        public bool PrintAPI { get; set; } = true;

        public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            var transient = context.Env.Transient;

            Console.WriteLine("===== TransientProbe =====");

            if (PrintFragments)
            {
                Console.WriteLine("Fragments ({0}):", transient.Fragments.Count);

                for (int i = 0; i < transient.Fragments.Count; i++)
                {
                    var seg = transient.Fragments[i];
                    var content = seg.Message.Content ?? "";
                    string display;

                    if (content.Length > 40)
                        display = content[..20] + "..." + content[^20..];
                    else
                        display = content;

                    Console.WriteLine("  [{0}] From={1}, Source={2}", i, seg.From, seg.SourceType?.Name ?? "(none)");

                    if (seg.Metadata.Count > 0)
                        Console.WriteLine("       Meta: {0}",
                            string.Join("; ", seg.Metadata.Select(kv => $"{kv.Key}={kv.Value}")));

                    Console.WriteLine("       Content: \"{0}\"", display);
                }
            }

            if (PrintSharedState)
                Console.WriteLine("SharedState keys: {0}", string.Join(", ", transient.SharedState.Keys));

            if (PrintTools)
                Console.WriteLine("Tools ({0}): {1}", transient.Tools.Count,
                    string.Join(", ", transient.Tools.Select(t => t.GetType().Name)));

            if (PrintAPI)
                Console.WriteLine("API: {0}", transient.API != null ? "set" : "null");

            Console.WriteLine("===== End TransientProbe =====");

            return Task.FromResult(NodeResult.SuccessResult());
        }
    }
}
