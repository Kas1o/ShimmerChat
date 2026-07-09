using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin
{
    /// <summary>
    /// IAutoCreateToolV2 版本的 SetChatNameTool。
    /// </summary>
    public class SetChatNameToolV2 : IAutoCreateToolV2
    {
        private readonly IKVDataService _kvData;
        private readonly Guid _chatGuid;

        public static string Name => "set_chat_name";
        public static string Description => "Set name for current chat. Invoke when topic updates or on first user input.";
        public static string CategoryPath => "对话";

        public SetChatNameToolV2() { }

        private SetChatNameToolV2(IKVDataService kvData, Guid chatGuid)
        {
            _kvData = kvData;
            _chatGuid = chatGuid;
        }

        public static IAutoCreateToolV2 Create(PersistentEnv env) =>
            new SetChatNameToolV2(env.KVData, env.ChatGuid);

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
