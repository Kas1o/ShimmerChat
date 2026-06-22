using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using ShimmerChatLib;
using ShimmerChatLib.Tool;
using System.Runtime.InteropServices;
using System.Text;

namespace ShimmerChatBuiltin.FileSystem
{
	public class FileSystemOverviewTool : ITool
	{
		string ITool.Path => "Filesystem";

		async Task<string> ITool.Execute(string input, Chat? chat, Agent? agent)
		{
			return GetOverview();
		}

		Tool ITool.GetToolDefinition() => new()
		{
			name = "file_system_overview",
			description = "Get an overview of the local file system. On Windows shows all drives with capacity and free space. On other systems shows mount points.",
			parameters = null
		};

		private static string GetOverview()
		{
			var sb = new StringBuilder();
			sb.AppendLine("=== File System Overview ===");
			sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");
			sb.AppendLine($"Architecture: {RuntimeInformation.OSArchitecture}");
			sb.AppendLine();

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
					catch
					{
						sb.AppendLine($"  {drive.Name} [{drive.DriveType}] (unavailable)");
					}
				}
			}
			else
			{
				sb.AppendLine("--- Mount Points ---");
				sb.AppendLine("  Root: /");
				try { sb.AppendLine($"  Home: {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}"); } catch { }
				try
				{
					foreach (var drive in DriveInfo.GetDrives())
					{
						try
						{
							sb.AppendLine($"  {drive.Name} [{drive.DriveType}] Total: {FileSystemHelper.FormatSize(drive.TotalSize)} Free: {FileSystemHelper.FormatSize(drive.AvailableFreeSpace)}");
						}
						catch { }
					}
				}
				catch { }
			}

			sb.AppendLine();
			sb.AppendLine("--- Special Folders ---");
			try { sb.AppendLine($"  Desktop:    {Environment.GetFolderPath(Environment.SpecialFolder.Desktop)}"); } catch { }
			try { sb.AppendLine($"  Documents:  {Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}"); } catch { }
			try { sb.AppendLine($"  Downloads:  {FileSystemHelper.GetDownloadsPath()}"); } catch { }
			try { sb.AppendLine($"  Current:    {Environment.CurrentDirectory}"); } catch { }

			return sb.ToString();
		}
	}
}
