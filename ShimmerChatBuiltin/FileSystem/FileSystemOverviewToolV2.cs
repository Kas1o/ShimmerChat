using System.Runtime.InteropServices;
using System.Text;
using SharperLLM.FunctionCalling;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.FileSystem
{
    /// <summary>
    /// 无依赖的 FileSystemOverviewTool V2 版本
    /// </summary>
    public class FileSystemOverviewToolV2 : IToolV2
    {
        public string Name => "file_system_overview";
        public string Description => "Get an overview of the local file system.";

        public Tool GetDefinition() => new()
        {
            name = "file_system_overview",
            description = "Get an overview of the local file system. On Windows shows all drives with capacity and free space.",
            parameters = null
        };

        public Task<string> ExecuteAsync(string input) => Task.FromResult(GetOverview());

        private static string GetOverview()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== File System Overview ===");
            sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");
            sb.AppendLine($"Architecture: {RuntimeInformation.OSArchitecture}\n");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var drives = DriveInfo.GetDrives();
                sb.AppendLine($"--- Drives ({drives.Length}) ---");
                foreach (var drive in drives)
                {
                    try
                    {
                        var total = drive.TotalSize > 0 ? FileSystemHelper.FormatSize(drive.TotalSize) : "N/A";
                        var free = drive.TotalSize > 0 ? FileSystemHelper.FormatSize(drive.AvailableFreeSpace) : "N/A";
                        var pct = drive.TotalSize > 0
                            ? ((double)(drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize * 100).ToString("F1") + "%"
                            : "N/A";
                        sb.AppendLine($"  {drive.Name} [{drive.DriveType}] {drive.VolumeLabel}  Total: {total}  Free: {free}  Used: {pct}");
                    }
                    catch { sb.AppendLine($"  {drive.Name} [{drive.DriveType}] (unavailable)"); }
                }
            }
            else
            {
                sb.AppendLine("--- Mount Points ---\n  Root: /");
                try { sb.AppendLine($"  Home: {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}"); } catch { }
            }

            sb.AppendLine("\n--- Special Folders ---");
            try { sb.AppendLine($"  Desktop:    {Environment.GetFolderPath(Environment.SpecialFolder.Desktop)}"); } catch { }
            try { sb.AppendLine($"  Documents:  {Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}"); } catch { }
            try { sb.AppendLine($"  Downloads:  {FileSystemHelper.GetDownloadsPath()}"); } catch { }
            try { sb.AppendLine($"  Current:    {Environment.CurrentDirectory}"); } catch { }

            return sb.ToString();
        }
    }
}
