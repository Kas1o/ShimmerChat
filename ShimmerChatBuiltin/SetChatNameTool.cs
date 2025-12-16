using Microsoft.AspNetCore.Components;
using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using ShimmerChatLib;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Tool;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShimmerChatBuiltin
{
	public class SetChatNameTool : ITool
	{

		IKVDataService KVDataService { get; set; }
		public SetChatNameTool(IKVDataService kvDataService)
		{
			KVDataService = kvDataService;
		}

		public Task<string> Execute(string input, Chat? chat, Agent? agent)
		{
			var obj = JsonConvert.DeserializeObject<Dictionary<string, string>>(input);
			if (obj != null)
			{
				if (chat != null && obj.ContainsKey("name"))
				{
					chat.Name = obj["name"];
					chat.Save(KVDataService);
					return Task.FromResult($"Chat name updated to: {chat.Name}");
				}
				else
				{
					return Task.FromResult("Error: Chat is null or name parameter is missing.");
				}
			}
			else
			{
				return Task.FromResult("Error: Invalid input.");
			}
		}

		public Tool GetToolDefinition() => new Tool
		{
			name = "set_chat_name",
			description = "set name for current chat, invoke when topic update or user first input. do not use this tool frequently.",
			parameters = new List<(ToolParameter parameter, bool required)>
			{
				(new ToolParameter
				{
					name = "name",
					description = "new name for current chat",
					type = ParameterType.String
				}, true)
			}
		};
	}
}
