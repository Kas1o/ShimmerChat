using System.Reflection;
using System.Text.Json;
using ShimmerChatLib.Tool;
using SharperLLM.FunctionCalling;

namespace ShimmerChat.Singletons
{
	public class ToolServiceV1 : IToolService
	{
		private readonly string PluginsFolder = Path.Combine(AppContext.BaseDirectory, "./Plugins");
		private readonly string ConfigFile = Path.Combine(AppContext.BaseDirectory, "enabled_tools.json");
		private readonly IPluginLoaderService _pluginLoaderService;

		public List<ITool> LoadedTools { get; private set; } = new();
		public List<ITool> EnabledTools { get; private set; } = new();

		public ToolServiceV1(IPluginLoaderService pluginLoaderService)
		{
			_pluginLoaderService = pluginLoaderService;
			LoadAllTools();
			LoadEnabledTools();
		}

		private void LoadAllTools()
		{
			var toolDict = new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);

			// 1. 加载 ShimmerChatBuiltinTools 项目的工具
			try
			{
				var builtinAssembly = typeof(ShimmerChatBuiltinTools.Target).Assembly;
				var builtinTools = _pluginLoaderService.LoadImplementationsFromAssembly<ITool>(builtinAssembly);
				
				foreach (var tool in builtinTools)
				{
					var name = tool.GetToolDefinition().name;
					if (toolDict.ContainsKey(name))
						throw new Exception($"工具名称冲突: {name}");
					toolDict[name] = tool;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"加载内置工具失败: {ex.Message}");
			}

			// 2. 加载插件工具
			var pluginTools = _pluginLoaderService.LoadImplementationsFromPlugins<ITool>(PluginsFolder);
			foreach (var tool in pluginTools)
			{
				var name = tool.GetToolDefinition().name;
				if (toolDict.ContainsKey(name))
					throw new Exception($"工具名称冲突: {name}");
				toolDict[name] = tool;
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
