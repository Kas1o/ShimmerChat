using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 构造 SetChatNameTool（需要 Chat 引用），加入 Tools 列表
    /// </summary>
    [NodeInfo("Set Chat Name", Icon = "✏", Color = "#c0a060", Category = "Tool/Chat")]
    public class SetChatNameNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Set Chat Name Tool";

        public Task ExecuteAsync(NodeExecutionContext context)
        {
            var kvData = context.Env.Persistent.KVData;
            var chatGuid = context.Env.Persistent.ChatGuid;

            var tool = new SetChatNameToolV2(kvData, chatGuid);
            context.Env.Transient.Tools.Add(tool);

            return Task.CompletedTask;
        }
    }
}
