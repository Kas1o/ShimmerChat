using System.Text;
using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.FileSystem
{
    public class FileSystemBrowseToolV2 : IAutoCreateToolV2
    {
        private readonly IKVDataService _kvData;

        public static string NameKey => "tool.browse_directory";
        public static string DescriptionKey => "tool.browse_directory.desc";
        public static string[] CategoryKeys => ["category.file_system", "category.browse"];

        public FileSystemBrowseToolV2() { }

        private FileSystemBrowseToolV2(IKVDataService kvData)
        {
            _kvData = kvData;
        }

        public static IAutoCreateToolV2 Create(PersistentEnv env) =>
            new FileSystemBrowseToolV2(env.KVData);

        public Tool GetDefinition() => new()
        {
            name = "browse_directory",
            description = "List contents of a directory. Shows folders and files with their sizes.",
            parameters =
            [
                (new ToolParameter { name = "path", type = ParameterType.String, description = "The directory path to browse." }, true),
                (new ToolParameter { name = "include_subdirs", type = ParameterType.Boolean, description = "Whether to include first-level subdirectories." }, false)
            ]
        };

        public Task<string> ExecuteAsync(string input)
        {
            var a = JsonConvert.DeserializeObject<BrowseInput>(input);
            return Task.FromResult(Browse(a.path ?? ".", a.include_subdirs ?? false));
        }

        private string Browse(string path, bool includeSubdirs)
        {
            try
            {
                path = Path.GetFullPath(path);
                var config = FileSystemConfigManager.Load(_kvData);

                if (!FileSystemConfigManager.IsPathAllowed(path, config))
                    return $"Access denied: path '{path}' is not allowed.";

                if (!Directory.Exists(path)) return $"Directory not found: {path}";

                var sb = new StringBuilder();
                sb.AppendLine($"Directory: {path}\n");
                var dirs = Directory.GetDirectories(path);
                var files = Directory.GetFiles(path);

                sb.AppendLine($"--- Folders ({dirs.Length}) ---");
                foreach (var dir in dirs.OrderBy(d => d))
                    sb.AppendLine($"  [DIR]  {Path.GetFileName(dir)}");

                sb.AppendLine($"\n--- Files ({files.Length}) ---");
                foreach (var file in files.OrderBy(f => f))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        sb.AppendLine($"  [FILE] {Path.GetFileName(file)}  ({FileSystemHelper.FormatSize(fi.Length)})");
                    }
                    catch { sb.AppendLine($"  [FILE] {Path.GetFileName(file)}"); }
                }

                if (includeSubdirs && dirs.Length > 0)
                {
                    sb.AppendLine("\n--- Subdirectories ---");
                    foreach (var dir in dirs.OrderBy(d => d))
                    {
                        sb.AppendLine($"  [{Path.GetFileName(dir)}]");
                        try
                        {
                            var subFiles = Directory.GetFiles(dir);
                            foreach (var sf in subFiles.OrderBy(f => f).Take(50))
                                sb.AppendLine($"    {Path.GetFileName(sf)}");
                            if (subFiles.Length > 50)
                                sb.AppendLine($"    ... and {subFiles.Length - 50} more files");
                        }
                        catch { sb.AppendLine("    [access denied]"); }
                    }
                }

                return sb.ToString();
            }
            catch (Exception ex) { return $"Error browsing directory: {ex.Message}"; }
        }

        private struct BrowseInput { public string? path { get; set; } public bool? include_subdirs { get; set; } }
    }
}
