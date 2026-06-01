using Newtonsoft.Json;
using SharperLLM.Util;
using ShimmerChatLib.Context;
using ShimmerChatLib.Interface;
using ShimmerChatLib;

namespace ShimmerChatBuiltin.DynPrompt
{
	public class DynPromptConfig : ModifierConfig
	{
		public string SetName { get; set; } = "";
	}

	public class DynPrompt : IContextModifier
	{
		IKVDataService pluginData;

		public DynPrompt(IKVDataService pluginDataService)
		{
			this.pluginData = pluginDataService;
		}

		public ContextModifierInfo info => new ContextModifierInfo
		{
			Name = "DynPrompt",
			Description = "Inject prompt dynamically based on trigger rules. Select a DynPromptSet by name."
		};

		public Type ConfigType => typeof(DynPromptConfig);

		public (bool IsValid, string Error) Validate(ModifierConfig config)
		{
			var cfg = (DynPromptConfig)config;
			if (string.IsNullOrWhiteSpace(cfg.SetName))
				return (false, "SetName cannot be empty");
			return (true, "");
		}

		public void ModifyContext(ContextDocument context, ModifierConfig config, Chat chat, Agent agent)
		{
			var cfg = (DynPromptConfig)config;
			var input = cfg.SetName;

			var data = pluginData.Read("DynPrompt", "DynPromptSets");
			var sets = JsonConvert.DeserializeObject<List<DynPromptSet>>(data ?? "[]") ?? [];

			var set = sets.FindLast(s => s.Name.Trim() == input.Trim());
			if (set == null)
				throw new InvalidOperationException($"No DynPromptSet found with name '{input}'");

			ProcessDynPromptSet(context, set, sets, new HashSet<string>());
		}

		private void ProcessDynPromptSet(ContextDocument context, DynPromptSet set, List<DynPromptSet> allSets, HashSet<string> processedTermNames)
		{
			foreach (var term in set.Terms)
			{
				string contextText = CollectContextText(context);

				if (string.IsNullOrEmpty(term.TriggerRule) || EvaluateTriggerRule(term.TriggerRule, contextText))
				{
					InjectTerm(context, term);

					if (term.AllowBeTriggeredByRecursive && !processedTermNames.Contains(term.Name))
					{
						var nestedSet = allSets.FindLast(s => s.Name.Trim() == term.Name.Trim());
						if (nestedSet != null)
						{
							processedTermNames.Add(term.Name);
							ProcessDynPromptSet(context, nestedSet, allSets, processedTermNames);
							processedTermNames.Remove(term.Name);
						}
					}
				}
			}
		}

		private string CollectContextText(ContextDocument context)
		{
			var sb = new System.Text.StringBuilder();

			if (!string.IsNullOrEmpty(context.Template.System))
			{
				sb.Append(context.Template.System);
				sb.Append(Environment.NewLine);
			}

			foreach (var segment in context.Segments)
			{
				sb.Append(segment.Message.Content);
				sb.Append(Environment.NewLine);
			}

			return sb.ToString();
		}

		private bool EvaluateTriggerRule(string rule, string contextText)
		{
			try
			{
				return DynPromptEvaluator.Evaluate(rule, contextText);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error evaluating trigger rule: {ex.Message}");
				return false;
			}
		}

		private void InjectTerm(ContextDocument context, DynPromptTerm term)
		{
			var newMessage = new ChatMessage { Content = term.Content };

			switch (term.InjectionMode)
			{
				case DynPromptTermInjectionMode.BeforeSystem:
					int systemIndex = context.Segments.FindIndex(s => s.From == PromptBuilder.From.system);
					if (systemIndex >= 0)
					{
						var segment = context.Segments[systemIndex];
						segment.Message.Content = term.Content + Environment.NewLine + segment.Message.Content;
					}
					else
					{
						string oldSystem = context.Template.System;
						context.Template.System = term.Content;
						if (!string.IsNullOrEmpty(oldSystem))
						{
							context.Template.System += Environment.NewLine + oldSystem;
						}
					}
					break;

				case DynPromptTermInjectionMode.AfterSystem:
					systemIndex = context.Segments.FindIndex(s => s.From == PromptBuilder.From.system);
					if (systemIndex >= 0)
					{
						var segment = context.Segments[systemIndex];
						segment.Message.Content = segment.Message.Content + Environment.NewLine + term.Content;
					}
					else
					{
						if (!string.IsNullOrEmpty(context.Template.System))
						{
							context.Template.System += Environment.NewLine;
						}
						context.Template.System += term.Content;
					}
					break;

				case DynPromptTermInjectionMode.AtDepth:
					InjectAtDepth(context, term);
					break;
			}
		}

		private void InjectAtDepth(ContextDocument context, DynPromptTerm term)
		{
			int injectionDepth = term.InjectionDepth;

			if (injectionDepth < 0)
			{
				injectionDepth = Math.Max(0, context.Segments.Count + 1 + injectionDepth);
			}

			if (injectionDepth >= context.Segments.Count)
			{
				context.Segments.Add(new ContextSegment
				{
					SourceType = typeof(DynPrompt),
					Message = new ChatMessage { Content = term.Content },
					From = PromptBuilder.From.system
				});
			}
			else
			{
				context.Segments.Insert(injectionDepth, new ContextSegment
				{
					SourceType = typeof(DynPrompt),
					Message = new ChatMessage { Content = term.Content },
					From = PromptBuilder.From.system
				});
			}
		}
	}
}
