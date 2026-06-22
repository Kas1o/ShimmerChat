using Newtonsoft.Json;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.FileSystem
{
	public class FileSystemConfig
	{
		public List<string> AllowList { get; set; } = [];
		public List<string> BanList { get; set; } = [];
	}

	public static class FileSystemConfigManager
	{
		private const string SpaceId = "Filesystem";
		private const string Key = "Config";

		public static FileSystemConfig Load(IKVDataService kvData)
		{
			var json = kvData.Read(SpaceId, Key);
			if (string.IsNullOrEmpty(json))
				return new FileSystemConfig();
			try
			{
				return JsonConvert.DeserializeObject<FileSystemConfig>(json) ?? new FileSystemConfig();
			}
			catch
			{
				return new FileSystemConfig();
			}
		}

		public static void Save(IKVDataService kvData, FileSystemConfig config)
		{
			var json = JsonConvert.SerializeObject(config, Formatting.Indented);
			kvData.Write(SpaceId, Key, json);
		}

		public static bool IsPathAllowed(string path, FileSystemConfig config)
		{
			var normalized = System.IO.Path.GetFullPath(path).Replace('\\', '/');

			foreach (var pattern in config.BanList)
			{
				if (string.IsNullOrWhiteSpace(pattern)) continue;
				try
				{
					if (System.Text.RegularExpressions.Regex.IsMatch(normalized, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
						return false;
				}
				catch { }
			}

			if (config.AllowList.Count == 0)
				return true;

			foreach (var pattern in config.AllowList)
			{
				if (string.IsNullOrWhiteSpace(pattern)) continue;
				try
				{
					if (System.Text.RegularExpressions.Regex.IsMatch(normalized, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
						return true;
				}
				catch { }
			}

			return false;
		}
	}
}
