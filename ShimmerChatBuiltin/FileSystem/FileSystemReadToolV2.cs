using System.Text;
using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.FileSystem
{
    public class FileSystemReadToolV2 : IAutoCreateToolV2
    {
        private readonly IKVDataService _kvData;

        public static string Name => "read_file";
        public static string Description => "Read a file's content from the local file system.";
        public static string CategoryPath => "文件系统/读写";

        public FileSystemReadToolV2() { }

        private FileSystemReadToolV2(IKVDataService kvData)
        {
            _kvData = kvData;
        }

        public static IAutoCreateToolV2 Create(PersistentEnv env) =>
            new FileSystemReadToolV2(env.KVData);

        public Tool GetDefinition() => new()
        {
            name = "read_file",
            description = "Read a file's content from the local file system. Automatically detects text encoding and returns a hint for binary files.",
            parameters =
            [
                (new ToolParameter
                {
                    name = "path",
                    type = ParameterType.String,
                    description = "The file path to read. Can be absolute or relative."
                }, true)
            ]
        };

        public Task<string> ExecuteAsync(string input)
        {
            var a = JsonConvert.DeserializeObject<ReadInput>(input);
            return Task.FromResult(Read(a.path));
        }

        private string Read(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "Error: path parameter is required.";

            try
            {
                path = Path.GetFullPath(path);
                var config = FileSystemConfigManager.Load(_kvData);

                if (!FileSystemConfigManager.IsPathAllowed(path, config))
                    return $"Access denied: path '{path}' is not allowed.";

                if (!File.Exists(path))
                    return $"File not found: {path}";

                var fi = new FileInfo(path);
                const long maxSize = 500 * 1024;

                byte[] bytes;
                if (fi.Length > maxSize)
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                    bytes = new byte[maxSize];
                    fs.ReadExactly(bytes, 0, (int)maxSize);
                }
                else
                {
                    bytes = File.ReadAllBytes(path);
                }

                var (content, encoding, isBinary) = FileSystemHelper.DetectEncodingAndRead(bytes);

                var sb = new StringBuilder();
                sb.AppendLine($"File: {path}");
                sb.AppendLine($"Size: {FileSystemHelper.FormatSize(fi.Length)}");

                if (isBinary)
                {
                    sb.AppendLine("Type: Binary file detected.");
                    sb.AppendLine($"Last Modified: {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                    return sb.ToString();
                }

                sb.AppendLine($"Encoding: {encoding}");
                sb.AppendLine($"Last Modified: {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}");

                if (fi.Length > maxSize)
                    sb.AppendLine($"Note: Showing first {maxSize / 1024}KB only.");

                sb.AppendLine();
                sb.AppendLine("--- Content ---");
                sb.Append(content);

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Error reading file: {ex.Message}";
            }
        }

        private struct ReadInput { public string? path { get; set; } }
    }
}
