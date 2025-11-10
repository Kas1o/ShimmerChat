using System.Reflection;
using System.Text.Json;
using ShimmerChatLib.Tool;
using SharperLLM.FunctionCalling;

namespace ShimmerChat.Singletons
{
	public class ToolServiceV1 : IToolService
	{
		private const string PluginsFolder = "./Plugins";
		private const string ConfigFile = "enabled_tools.json";

		public List<ITool> LoadedTools { get; private set; } = new();
		public List<ITool> EnabledTools { get; private set; } = new();

		public ToolServiceV1()
		{
			LoadAllTools();
			LoadEnabledTools();
		}

		private void LoadAllTools()
		{
			var toolDict = new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);

			// 1. 加载 ShimmerChatBuiltinTools 项目的工具
			var builtinAssembly = typeof(ShimmerChatBuiltinTools.Target).Assembly;
			var builtinTypes = builtinAssembly
				.GetTypes()
				.Where(t => typeof(ITool).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

			foreach (var type in builtinTypes)
			{
				if (Activator.CreateInstance(type) is ITool tool)
				{
					var name = tool.GetToolDefinition().name;
					if (toolDict.ContainsKey(name))
						throw new Exception($"工具名称冲突: {name}");
					toolDict[name] = tool;
				}
			}

			// 2. 加载插件工具
			if (Directory.Exists(PluginsFolder))
			{
				foreach (var dll in Directory.GetFiles(PluginsFolder, "*.dll"))
				{
					try
					{
						var asm = Assembly.LoadFrom(dll);
						var pluginTypes = asm.GetTypes()
							.Where(t => typeof(ITool).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

						foreach (var type in pluginTypes)
						{
							if (Activator.CreateInstance(type) is ITool tool)
							{
								var name = tool.GetToolDefinition().name;
								if (toolDict.ContainsKey(name))
									throw new Exception($"工具名称冲突: {name}");
								toolDict[name] = tool;
							}
						}
					}
					catch (Exception ex)
					{
						// 插件加载失败，记录日志或忽略
						Console.WriteLine($"插件加载失败: {dll} - {ex.Message}");
					}
				}
			}

			LoadedTools = toolDict.Values.ToList();
		}

		private void LoadEnabledTools()
		{
			EnabledTools.Clear();
			if (!File.Exists(ConfigFile))
				return;

			try
			{
				var enabledNames = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(ConfigFile));
				foreach (var name in enabledNames ?? Enumerable.Empty<string>())
				{
					var tool = LoadedTools.FirstOrDefault(t => t.GetToolDefinition().name == name);
					if (tool != null)
						EnabledTools.Add(tool);
					// 配置文件中不存在的工具自动忽略
				}
			}
			catch
			{
				// 配置文件损坏或格式错误，忽略
			}
		}

		private void SaveEnabledTools()
		{
			var names = EnabledTools.Select(t => t.GetToolDefinition().name).ToList();
			File.WriteAllText(ConfigFile, JsonSerializer.Serialize(names));
		}

		public void EnableTool(string name)
		{
			var tool = LoadedTools.FirstOrDefault(t => t.GetToolDefinition().name == name);
			if (tool != null && !EnabledTools.Contains(tool))
			{
				EnabledTools.Add(tool);
				SaveEnabledTools();
			}
		}

		public void DisableTool(string name)
		{
			var tool = EnabledTools.FirstOrDefault(t => t.GetToolDefinition().name == name);
			if (tool != null)
			{
				EnabledTools.Remove(tool);
				SaveEnabledTools();
			}
		}

		public IEnumerable<Tool> GetEnabledToolDefinitions()
		{
			return EnabledTools.Select(t => t.GetToolDefinition());
		}

		public async Task<string?> ExecuteToolAsync(string toolName, string arguments)
		{
			var tool = EnabledTools.FirstOrDefault(t => t.GetToolDefinition().name == toolName);
			if (tool == null)
				return null;
			return await tool.Execute(arguments);
		}
	}
}
