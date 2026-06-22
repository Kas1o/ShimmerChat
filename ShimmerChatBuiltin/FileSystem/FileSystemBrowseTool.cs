using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using ShimmerChatLib;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Tool;
using System.Text;

namespace ShimmerChatBuiltin.FileSystem
{
	public class FileSystemBrowseTool : ITool
	{
		string ITool.Path => "Filesystem";

		private readonly IKVDataService _kvData;

		public FileSystemBrowseTool(IKVDataService kvData)
		{
			_kvData = kvData;
		}

		async Task<string> ITool.Execute(string input, Chat? chat, Agent? agent)
		{
			var a = JsonConvert.DeserializeObject<BrowseInput>(input);
			return Browse(a.path ?? ".", a.include_subdirs ?? false);
		}

		Tool ITool.GetToolDefinition() => new()
		{
			name = "browse_directory",
			description = "List contents of a directory. Shows folders and files with their sizes.",
			parameters =
			[
				(new ToolParameter
				{
					name = "path",
					type = ParameterType.String,
					description = "The directory path to browse. Can be absolute or relative."
				}, true),
				(new ToolParameter
				{
					name = "include_subdirs",
					type = ParameterType.Boolean,
					description = "Whether to include first-level subdirectory contents. Default is false."
				}, false)
			]
		};

		private string Browse(string path, bool includeSubdirs)
		{
			try
			{
				path = Path.GetFullPath(path);
				var config = FileSystemConfigManager.Load(_kvData);

				if (!FileSystemConfigManager.IsPathAllowed(path, config))
					return $"Access denied: path '{path}' is not allowed.";

				if (!Directory.Exists(path))
					return $"Directory not found: {path}";

				var sb = new StringBuilder();
				sb.AppendLine($"Directory: {path}");
				sb.AppendLine();

				var dirs = Directory.GetDirectories(path);
				var files = Directory.GetFiles(path);

				sb.AppendLine($"--- Folders ({dirs.Length}) ---");
				foreach (var dir in dirs.OrderBy(d => d))
					sb.AppendLine($"  [DIR]  {Path.GetFileName(dir)}");

				sb.AppendLine();
				sb.AppendLine($"--- Files ({files.Length}) ---");
				foreach (var file in files.OrderBy(f => f))
				{
					try
					{
						var fi = new FileInfo(file);
						sb.AppendLine($"  [FILE] {Path.GetFileName(file)}  ({FileSystemHelper.FormatSize(fi.Length)})");
					}
					catch
					{
						sb.AppendLine($"  [FILE] {Path.GetFileName(file)}");
					}
				}

				if (includeSubdirs && dirs.Length > 0)
				{
					sb.AppendLine();
					sb.AppendLine("--- Subdirectories (max depth 1) ---");
					foreach (var dir in dirs.OrderBy(d => d))
					{
						var dirName = Path.GetFileName(dir);
						sb.AppendLine($"  [{dirName}]");
						try
						{
							var subFiles = Directory.GetFiles(dir);
							foreach (var sf in subFiles.OrderBy(f => f).Take(50))
								sb.AppendLine($"    {Path.GetFileName(sf)}");
							if (subFiles.Length > 50)
								sb.AppendLine($"    ... and {subFiles.Length - 50} more files");
						}
						catch
						{
							sb.AppendLine($"    [access denied]");
						}
					}
				}

				return sb.ToString();
			}
			catch (Exception ex)
			{
				return $"Error browsing directory: {ex.Message}";
			}
		}

		private struct BrowseInput
		{
			public string? path { get; set; }
			public bool? include_subdirs { get; set; }
		}
	}
}
