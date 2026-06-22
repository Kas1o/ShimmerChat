using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using ShimmerChatLib;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Tool;
using System.Text;

namespace ShimmerChatBuiltin.FileSystem
{
	public class FileSystemWriteTool : ITool
	{
		string ITool.Path => "Filesystem";

		private readonly IKVDataService _kvData;

		public FileSystemWriteTool(IKVDataService kvData)
		{
			_kvData = kvData;
		}

		async Task<string> ITool.Execute(string input, Chat? chat, Agent? agent)
		{
			var a = JsonConvert.DeserializeObject<WriteInput>(input);
			return Write(a.path, a.content, a.encoding ?? "utf-8");
		}

		Tool ITool.GetToolDefinition() => new()
		{
			name = "write_file",
			description = "Write plain text content to a file. Creates the file if it does not exist, overwrites if it does. Only accepts text content.",
			parameters =
			[
				(new ToolParameter
				{
					name = "path",
					type = ParameterType.String,
					description = "The file path to write to. Can be absolute or relative."
				}, true),
				(new ToolParameter
				{
					name = "content",
					type = ParameterType.String,
					description = "The text content to write to the file."
				}, true),
				(new ToolParameter
				{
					name = "encoding",
					type = ParameterType.String,
					description = "The text encoding to use. Valid values: utf-8, utf-8-bom, utf-16-le, utf-16-be. Default is utf-8.",
					@enum = ["utf-8", "utf-8-bom", "utf-16-le", "utf-16-be"]
				}, false)
			]
		};

		private string Write(string? path, string? content, string encoding)
		{
			if (string.IsNullOrWhiteSpace(path))
				return "Error: path parameter is required.";

			if (content == null)
				return "Error: content parameter is required.";

			try
			{
				path = Path.GetFullPath(path);
				var config = FileSystemConfigManager.Load(_kvData);

				if (!FileSystemConfigManager.IsPathAllowed(path, config))
					return $"Access denied: path '{path}' is not allowed.";

				var dir = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
					return $"Error: parent directory does not exist: {dir}";

				Encoding enc = encoding.ToLowerInvariant() switch
				{
					"utf-8-bom" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
					"utf-16-le" => Encoding.Unicode,
					"utf-16-be" => Encoding.BigEndianUnicode,
					_ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
				};

				File.WriteAllText(path, content, enc);

				var fi = new FileInfo(path);
				return $"File written successfully.\nPath: {path}\nSize: {FileSystemHelper.FormatSize(fi.Length)}\nEncoding: {encoding}\nLines: {content.Split('\n').Length}";
			}
			catch (Exception ex)
			{
				return $"Error writing file: {ex.Message}";
			}
		}

		private struct WriteInput
		{
			public string? path { get; set; }
			public string? content { get; set; }
			public string? encoding { get; set; }
		}
	}
}
