using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin
{
    /// <summary>
    /// IToolV2 版本的 SetChatNameTool。由 SetChatNameNode 构造并注入依赖。
    /// </summary>
    public class SetChatNameToolV2 : IToolV2
    {
        private readonly IKVDataService _kvData;
        private readonly Guid _chatGuid;

        public string Name => "set_chat_name";
        public string Description => "Set name for current chat. Invoke when topic updates or on first user input.";

        public SetChatNameToolV2(IKVDataService kvData, Guid chatGuid)
        {
            _kvData = kvData;
            _chatGuid = chatGuid;
        }

        public Tool GetDefinition() => new()
        {
            name = "set_chat_name",
            description = "set name for current chat, invoke when topic update or user first input. do not use this tool frequently.",
            parameters =
            [
                (new ToolParameter { name = "name", type = ParameterType.String, description = "new name for current chat" }, true)
            ]
        };

        public Task<string> ExecuteAsync(string input)
        {
            var obj = JsonConvert.DeserializeObject<Dictionary<string, string>>(input);
            if (obj != null && obj.TryGetValue("name", out var name))
            {
                var chat = Chat.Load(_chatGuid, _kvData);
                chat.Name = name;
                chat.Save(_kvData);
                return Task.FromResult($"Chat name updated to: {name}");
            }
            return Task.FromResult("Error: name parameter is missing.");
        }
    }
}
