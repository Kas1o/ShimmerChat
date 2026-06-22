using System.Runtime.InteropServices;
using System.Text;

namespace ShimmerChatBuiltin.FileSystem
{
	public static class FileSystemHelper
	{
		public static string FormatSize(long bytes)
		{
			string[] sizes = ["B", "KB", "MB", "GB", "TB"];
			double len = bytes;
			int order = 0;
			while (len >= 1024 && order < sizes.Length - 1)
			{
				order++;
				len /= 1024;
			}
			return $"{len:0.##} {sizes[order]}";
		}

		public static string GetDownloadsPath()
		{
			var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			return Path.Combine(profile, "Downloads");
		}

		public static (string? content, string encoding, bool isBinary) DetectEncodingAndRead(byte[] bytes)
		{
			if (bytes.Length == 0)
				return ("", "empty", false);

			int checkLen = Math.Min(bytes.Length, 8192);
			int nullCount = 0;
			int controlCount = 0;
			for (int i = 0; i < checkLen; i++)
			{
				if (bytes[i] == 0) nullCount++;
				if (bytes[i] < 0x20 && bytes[i] != 0x09 && bytes[i] != 0x0A && bytes[i] != 0x0D)
					controlCount++;
			}

			if (nullCount > checkLen * 0.3 || (nullCount > 0 && controlCount > checkLen * 0.3))
				return (null, "binary", true);

			if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
				return (Encoding.UTF32.GetString(bytes, 4, bytes.Length - 4), "UTF-32-BE", false);
			if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
				return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "UTF-8-BOM", false);
			if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
				return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16-LE", false);
			if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
				return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16-BE", false);

			if (IsValidUtf8(bytes))
				return (Encoding.UTF8.GetString(bytes), "UTF-8", false);

			try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }

			Encoding[] fallbackEncodings;
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				fallbackEncodings =
				[
					Encoding.GetEncoding("gb18030"),
					Encoding.GetEncoding("shift_jis"),
					Encoding.GetEncoding("euc-kr"),
					Encoding.GetEncoding("big5"),
					Encoding.Latin1
				];
			}
			else
			{
				fallbackEncodings = [Encoding.Latin1];
			}

			foreach (var enc in fallbackEncodings)
			{
				try
				{
					var text = enc.GetString(bytes);
					if (text.Count(c => c == '\uFFFD') < text.Length * 0.05)
						return (text, enc.EncodingName, false);
				}
				catch { }
			}

			return (Encoding.UTF8.GetString(bytes), "UTF-8 (fallback)", false);
		}

		private static bool IsValidUtf8(byte[] bytes)
		{
			int i = 0;
			while (i < bytes.Length)
			{
				byte b = bytes[i];
				int charLen;

				if (b <= 0x7F)
					charLen = 1;
				else if (b >= 0xC2 && b <= 0xDF)
					charLen = 2;
				else if (b >= 0xE0 && b <= 0xEF)
					charLen = 3;
				else if (b >= 0xF0 && b <= 0xF4)
					charLen = 4;
				else
					return false;

				if (i + charLen > bytes.Length)
					return false;

				for (int j = 1; j < charLen; j++)
				{
					if ((bytes[i + j] & 0xC0) != 0x80)
						return false;
				}

				if (charLen == 3 && b == 0xE0 && bytes[i + 1] < 0xA0) return false;
				if (charLen == 3 && b == 0xED && bytes[i + 1] > 0x9F) return false;
				if (charLen == 4 && b == 0xF0 && bytes[i + 1] < 0x90) return false;
				if (charLen == 4 && b == 0xF4 && bytes[i + 1] > 0x8F) return false;

				i += charLen;
			}
			return true;
		}
	}
}
