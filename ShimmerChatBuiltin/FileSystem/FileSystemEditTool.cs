using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using ShimmerChatLib;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Tool;
using System.Text;

namespace ShimmerChatBuiltin.FileSystem
{
	public class FileSystemEditTool : ITool
	{
		string ITool.Path => "Filesystem";

		private readonly IKVDataService _kvData;

		public FileSystemEditTool(IKVDataService kvData)
		{
			_kvData = kvData;
		}

		async Task<string> ITool.Execute(string input, Chat? chat, Agent? agent)
		{
			var a = JsonConvert.DeserializeObject<EditInput>(input);
			return Edit(a.path, a.old_string, a.new_string, a.replace_all ?? false);
		}

		Tool ITool.GetToolDefinition() => new()
		{
			name = "edit_file",
			description = "Edit a plain text file by replacing a specific string with a new string. Only works on text files. Use replace_all to replace all occurrences.",
			parameters =
			[
				(new ToolParameter
				{
					name = "path",
					type = ParameterType.String,
					description = "The file path to edit. Can be absolute or relative. Must be a plain text file."
				}, true),
				(new ToolParameter
				{
					name = "old_string",
					type = ParameterType.String,
					description = "The exact text to find and replace in the file. Must match exactly including whitespace and indentation."
				}, true),
				(new ToolParameter
				{
					name = "new_string",
					type = ParameterType.String,
					description = "The new text to replace the old text with."
				}, true),
				(new ToolParameter
				{
					name = "replace_all",
					type = ParameterType.Boolean,
					description = "If true, replace all occurrences of old_string. If false (default), replace only the first occurrence."
				}, false)
			]
		};

		private string Edit(string? path, string? oldString, string? newString, bool replaceAll)
		{
			if (string.IsNullOrWhiteSpace(path))
				return "Error: path parameter is required.";

			if (oldString == null)
				return "Error: old_string parameter is required.";

			if (newString == null)
				newString = "";

			if (oldString == newString)
				return "Error: old_string and new_string are identical. No changes needed.";

			try
			{
				path = Path.GetFullPath(path);
				var config = FileSystemConfigManager.Load(_kvData);

				if (!FileSystemConfigManager.IsPathAllowed(path, config))
					return $"Access denied: path '{path}' is not allowed.";

				if (!File.Exists(path))
					return $"File not found: {path}";

				var bytes = File.ReadAllBytes(path);
				var (_, _, isBinary) = FileSystemHelper.DetectEncodingAndRead(bytes);

				if (isBinary)
					return "Error: target file appears to be binary. Edit tool only supports plain text files.";

				var encoding = DetectFileEncoding(bytes);
				var originalContent = encoding.GetString(bytes);

				if (!originalContent.Contains(oldString))
					return $"Error: old_string not found in file.\nFile: {path}\nThe specified text was not found in the file. Check whitespace and indentation.";

				int occurrences;
				string newContent;

				if (replaceAll)
				{
					occurrences = CountOccurrences(originalContent, oldString);
					newContent = originalContent.Replace(oldString, newString);
				}
				else
				{
					occurrences = 1;
					int index = originalContent.IndexOf(oldString, StringComparison.Ordinal);
					newContent = originalContent[..index] + newString + originalContent[(index + oldString.Length)..];
				}

				Encoding writeEnc = ShouldUseBom(encoding)
					? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
					: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

				if (encoding == Encoding.Unicode)
					writeEnc = Encoding.Unicode;
				else if (encoding == Encoding.BigEndianUnicode)
					writeEnc = Encoding.BigEndianUnicode;
				else if (encoding == Encoding.UTF32)
					writeEnc = Encoding.UTF32;

				File.WriteAllText(path, newContent, writeEnc);

				var name = GetEncodingName(encoding);
				var occurrenceText = replaceAll ? $"{occurrences} occurrences" : "1 occurrence";

				return $"File edited successfully.\nFile: {path}\nReplaced: {occurrenceText}\nEncoding: {name}";
			}
			catch (Exception ex)
			{
				return $"Error editing file: {ex.Message}";
			}
		}

		private static Encoding DetectFileEncoding(byte[] bytes)
		{
			if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
				return Encoding.UTF8;
			if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
				return Encoding.Unicode;
			if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
				return Encoding.BigEndianUnicode;
			if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
				return Encoding.UTF32;

			if (FileSystemHelper.DetectEncodingAndRead(bytes).encoding == "UTF-8")
				return Encoding.UTF8;

			try
			{
				Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
				return Encoding.GetEncoding("gb18030");
			}
			catch
			{
				return Encoding.UTF8;
			}
		}

		private static bool ShouldUseBom(Encoding enc) =>
			enc == Encoding.UTF8 || enc == Encoding.Unicode || enc == Encoding.BigEndianUnicode || enc == Encoding.UTF32;

		private static string GetEncodingName(Encoding enc)
		{
			if (enc == Encoding.UTF8) return "UTF-8";
			if (enc == Encoding.Unicode) return "UTF-16-LE";
			if (enc == Encoding.BigEndianUnicode) return "UTF-16-BE";
			if (enc == Encoding.UTF32) return "UTF-32-BE";
			return enc.EncodingName;
		}

		private static int CountOccurrences(string text, string search)
		{
			int count = 0, i = 0;
			while ((i = text.IndexOf(search, i, StringComparison.Ordinal)) != -1)
			{
				count++;
				i += search.Length;
			}
			return count;
		}

		private struct EditInput
		{
			public string? path { get; set; }
			public string? old_string { get; set; }
			public string? new_string { get; set; }
			public bool? replace_all { get; set; }
		}
	}
}
