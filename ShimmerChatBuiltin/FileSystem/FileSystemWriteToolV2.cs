using System.Text;
using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.FileSystem
{
    public class FileSystemWriteToolV2 : IAutoCreateToolV2
    {
        private readonly IKVDataService _kvData;

        public static string Name => "write_file";
        public static string Description => "Write plain text content to a file.";
        public static string CategoryPath => "文件系统/读写";

        public FileSystemWriteToolV2() { }

        private FileSystemWriteToolV2(IKVDataService kvData)
        {
            _kvData = kvData;
        }

        public static IAutoCreateToolV2 Create(PersistentEnv env) =>
            new FileSystemWriteToolV2(env.KVData);

        public Tool GetDefinition() => new()
        {
            name = "write_file",
            description = "Write plain text content to a file. Creates the file if it does not exist, overwrites if it does.",
            parameters =
            [
                (new ToolParameter { name = "path", type = ParameterType.String, description = "The file path to write to." }, true),
                (new ToolParameter { name = "content", type = ParameterType.String, description = "The text content to write." }, true),
                (new ToolParameter { name = "encoding", type = ParameterType.String, description = "Text encoding: utf-8, utf-8-bom, utf-16-le, utf-16-be. Default utf-8.", @enum = ["utf-8", "utf-8-bom", "utf-16-le", "utf-16-be"] }, false)
            ]
        };

        public Task<string> ExecuteAsync(string input)
        {
            var a = JsonConvert.DeserializeObject<WriteInput>(input);
            return Task.FromResult(Write(a.path, a.content, a.encoding ?? "utf-8"));
        }

        private string Write(string? path, string? content, string encoding)
        {
            if (string.IsNullOrWhiteSpace(path)) return "Error: path parameter is required.";
            if (content == null) return "Error: content parameter is required.";

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

        private struct WriteInput { public string? path { get; set; } public string? content { get; set; } public string? encoding { get; set; } }
    }
}
