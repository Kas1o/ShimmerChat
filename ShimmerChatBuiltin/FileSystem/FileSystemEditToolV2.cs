using System.Text;
using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.FileSystem
{
    public class FileSystemEditToolV2 : IToolV2
    {
        public string Name => "edit_file";
        public string Description => "Edit a plain text file by replacing a specific string.";

        public Tool GetDefinition() => new()
        {
            name = "edit_file",
            description = "Edit a plain text file by replacing a specific string with a new string.",
            parameters =
            [
                (new ToolParameter { name = "path", type = ParameterType.String, description = "The file path to edit." }, true),
                (new ToolParameter { name = "old_string", type = ParameterType.String, description = "The exact text to find and replace." }, true),
                (new ToolParameter { name = "new_string", type = ParameterType.String, description = "The new text to replace with." }, true),
                (new ToolParameter { name = "replace_all", type = ParameterType.Boolean, description = "Replace all occurrences." }, false)
            ]
        };

        public Task<string> ExecuteAsync(string input)
        {
            var a = JsonConvert.DeserializeObject<EditInput>(input);
            return Task.FromResult(Edit(a.path, a.old_string, a.new_string, a.replace_all ?? false));
        }

        private string Edit(string? path, string? oldString, string? newString, bool replaceAll)
        {
            if (string.IsNullOrWhiteSpace(path)) return "Error: path parameter is required.";
            if (oldString == null) return "Error: old_string parameter is required.";
            newString ??= "";
            if (oldString == newString) return "Error: old_string and new_string are identical.";

            try
            {
                path = Path.GetFullPath(path);
                var config = ShimmerChatLib.Generation.ToolEnvironment.KVData != null
                    ? FileSystemConfigManager.Load(ShimmerChatLib.Generation.ToolEnvironment.KVData)
                    : new FileSystemConfig();

                if (!FileSystemConfigManager.IsPathAllowed(path, config))
                    return $"Access denied: path '{path}' is not allowed.";

                if (!File.Exists(path)) return $"File not found: {path}";

                var bytes = File.ReadAllBytes(path);
                var (_, _, isBinary) = FileSystemHelper.DetectEncodingAndRead(bytes);
                if (isBinary) return "Error: target file appears to be binary.";

                var encoding = DetectFileEncoding(bytes);
                var originalContent = encoding.GetString(bytes);

                if (!originalContent.Contains(oldString))
                    return $"Error: old_string not found in file.";

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

                Encoding writeEnc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                if (encoding == Encoding.Unicode) writeEnc = Encoding.Unicode;
                else if (encoding == Encoding.BigEndianUnicode) writeEnc = Encoding.BigEndianUnicode;
                else if (encoding == Encoding.UTF32) writeEnc = Encoding.UTF32;

                File.WriteAllText(path, newContent, writeEnc);
                return $"File edited successfully.\nFile: {path}\nReplaced: {(replaceAll ? $"{occurrences} occurrences" : "1 occurrence")}";
            }
            catch (Exception ex) { return $"Error editing file: {ex.Message}"; }
        }

        private static Encoding DetectFileEncoding(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8;
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode;
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode;
            if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF) return Encoding.UTF32;
            return Encoding.UTF8;
        }

        private static int CountOccurrences(string text, string search)
        {
            int count = 0, i = 0;
            while ((i = text.IndexOf(search, i, StringComparison.Ordinal)) != -1) { count++; i += search.Length; }
            return count;
        }

        private struct EditInput { public string? path { get; set; } public string? old_string { get; set; } public string? new_string { get; set; } public bool? replace_all { get; set; } }
    }
}
