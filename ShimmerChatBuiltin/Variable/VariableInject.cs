using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using ShimmerChatLib.Interface;
using ShimmerChatBuiltin.Variable;

namespace ShimmerChatBuiltin.Variable
{
	public class VariableInjectConfig : ModifierConfig
	{
		public string ScopeAndName { get; set; } = "all";
	}

	public class VariableInject : IContextModifier
	{
		private readonly IKVDataService _kvData;

		public VariableInject(IKVDataService kvData)
		{
			_kvData = kvData;
		}

		ContextModifierInfo IContextModifier.info => new ContextModifierInfo
		{
			Name = "VariableInject",
			Description = "Injects variables into the prompt. Use 'all' for all variables, or 'scope:name' to filter by scope."
		};

		public Type ConfigType => typeof(VariableInjectConfig);

		public (bool IsValid, string Error) Validate(ModifierConfig config) => (true, "");

		void IContextModifier.ModifyContext(ContextDocument context, ModifierConfig config, Chat chat, Agent agent)
		{
			var vConfig = (VariableInjectConfig)config;
			var input = vConfig.ScopeAndName;

			if (string.IsNullOrWhiteSpace(input))
			{
				input = "all";
			}

			var variables = VariableManager.GetAggregatedVariables(_kvData, chat.Guid, agent.guid);
			var selectedVariables = new List<Variable>();

			if (input.Equals("all", StringComparison.OrdinalIgnoreCase))
			{
				selectedVariables = variables.Variables;
			}
			else if (input.Contains(':'))
			{
				var parts = input.Split(':', 2);
				var scope = parts[0].ToLowerInvariant();
				var namePattern = parts[1];

				selectedVariables = scope switch
				{
					"agent" => variables.Variables.Where(v => v.Scope == VariableScope.Agent && v.Name.Contains(namePattern, StringComparison.OrdinalIgnoreCase)).ToList(),
					"chat" => variables.Variables.Where(v => v.Scope == VariableScope.Chat && v.Name.Contains(namePattern, StringComparison.OrdinalIgnoreCase)).ToList(),
					_ => variables.Variables.Where(v => v.Name.Contains(namePattern, StringComparison.OrdinalIgnoreCase)).ToList()
				};
			}
			else
			{
				selectedVariables = variables.Variables.Where(v => v.Name.Contains(input, StringComparison.OrdinalIgnoreCase)).ToList();
			}

			if (selectedVariables.Count == 0)
			{
				return;
			}

			var variableContext = string.Join("\n", selectedVariables.Select(v =>
			{
				var scopeStr = v.Scope == VariableScope.Agent ? "[Agent]" : "[Chat]";
				var typeStr = v.Type.ToString().ToLower();
				return $"{scopeStr} Variable: {v.Name} ({typeStr}) = {v.GetValueAsString()}";
			}));

			context.Segments.Add(new ContextSegment
			{
				SourceType = typeof(VariableInject),
				Message = new ChatMessage { Content = $"System Variables:\n{{\n{variableContext}\n}}" },
				From = PromptBuilder.From.system
			});
		}
	}
}
