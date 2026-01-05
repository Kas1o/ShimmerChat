using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShimmerChatBuiltin.Misc
{
	public static class JsonColorizer
	{
		// ANSI 颜色定义（可配置）
		private const string Reset = "\x1b[0m";
		private const string KeyColor = "\x1b[35m"; // Magenta
		private const string StringColor = "\x1b[32m"; // Green
		private const string NumberColor = "\x1b[34m"; // Blue
		private const string BooleanColor = "\x1b[33m"; // Yellow
		private const string NullColor = "\x1b[33m"; // Yellow
		private const string BraceColor = "\x1b[37m"; // Light Gray
		private const string CommaColor = "\x1b[37m"; // Light Gray

		// 共享 JsonSerializer（避免重复创建）
		private static readonly JsonSerializer _serializer = new JsonSerializer
		{
			// 可按需配置：日期格式、命名策略等
			DateFormatHandling = DateFormatHandling.IsoDateFormat,
			// ContractResolver = new CamelCasePropertyNamesContractResolver(),
		};

		/// 直接传入任意 .NET 对象 → 彩色 JSON
		public static string Colorize(object obj, int indentLevel = 0)
		{
			try
			{
				// ⚡ 关键：直接转为 JToken，不经过 string
				JToken token = JToken.FromObject(obj, _serializer);
				var sb = new StringBuilder();
				AppendToken(sb, token, indentLevel);
				return sb.ToString();
			}
			catch (Exception ex)
			{
				return $"{Reset}\x1b[31m[对象转 JSON 失败: {ex.Message}]{Reset}";
			}
		}

		/// 保留原 string 版本（兼容性）
		public static string Colorize(string json, int indentLevel = 0)
		{
			try
			{
				JToken token = JToken.Parse(json);
				var sb = new StringBuilder();
				AppendToken(sb, token, indentLevel);
				return sb.ToString();
			}
			catch (JsonException ex)
			{
				return $"{Reset}\x1b[31m[JSON 解析错误: {ex.Message}]{Reset}";
			}
		}

		private static void AppendToken(StringBuilder sb, JToken token, int indentLevel)
		{
			string indent = new string(' ', indentLevel * 2);

			switch (token)
			{
				case JValue value:
					AppendValue(sb, value);
					break;

				case JObject obj:
					AppendObject(sb, obj, indentLevel);
					break;

				case JArray array:
					AppendArray(sb, array, indentLevel);
					break;

				default:
					sb.Append($"{Reset}{token}{Reset}");
					break;
			}
		}

		private static void AppendValue(StringBuilder sb, JValue value)
		{
			switch (value.Type)
			{
				case JTokenType.String:
					// 用 JsonConvert.ToString 处理转义
					string quoted = JsonConvert.ToString(value.Value?.ToString());
					sb.Append($"{StringColor}{quoted}{Reset}");
					break;

				case JTokenType.Integer:
				case JTokenType.Float:
					sb.Append($"{NumberColor}{value}{Reset}");
					break;

				case JTokenType.Boolean:
					sb.Append($"{BooleanColor}{value}{Reset}");
					break;

				case JTokenType.Null:
					sb.Append($"{NullColor}null{Reset}");
					break;

				case JTokenType.Date:
					// 日期按字符串处理（已由 serializer 格式化）
					string dateQuoted = JsonConvert.ToString(value.Value?.ToString());
					sb.Append($"{StringColor}{dateQuoted}{Reset}");
					break;

				default:
					// Fallback: 尝试 ToString
					sb.Append($"{Reset}{value}{Reset}");
					break;
			}
		}

		private static void AppendObject(StringBuilder sb, JObject obj, int indentLevel)
		{
			string indent = new string(' ', indentLevel * 2);
			string innerIndent = new string(' ', (indentLevel + 1) * 2);

			sb.Append($"{BraceColor}{{{Reset}");

			if (obj.Count == 0)
			{
				sb.Append($"{BraceColor}}}{Reset}");
				return;
			}

			sb.AppendLine();

			int count = obj.Count;
			int i = 0;
			foreach (var property in obj.Properties())
			{
				sb.Append($"{innerIndent}{KeyColor}\"{property.Name}\"{Reset}: ");
				AppendToken(sb, property.Value, indentLevel + 1);

				if (i < count - 1)
					sb.Append($"{CommaColor},{Reset}");
				sb.AppendLine();
				i++;
			}

			sb.Append($"{indent}{BraceColor}}}{Reset}");
		}

		private static void AppendArray(StringBuilder sb, JArray array, int indentLevel)
		{
			string indent = new string(' ', indentLevel * 2);
			string innerIndent = new string(' ', (indentLevel + 1) * 2);

			sb.Append($"{BraceColor}[{Reset}");

			if (array.Count == 0)
			{
				sb.Append($"{BraceColor}]{Reset}");
				return;
			}

			sb.AppendLine();

			int count = array.Count;
			for (int i = 0; i < count; i++)
			{
				sb.Append(innerIndent);
				AppendToken(sb, array[i], indentLevel + 1);

				if (i < count - 1)
					sb.Append($"{CommaColor},{Reset}");
				sb.AppendLine();
			}

			sb.Append($"{indent}{BraceColor}]{Reset}");
		}

		/// 直接输出 object
		public static void WriteToConsole(object obj)
		{
			string colored = Colorize(obj);
			Console.WriteLine(colored);
		}

		public static void WriteToConsole(string json)
		{
			Console.WriteLine(Colorize(json));
		}

		/// 检测 ANSI 支持（简化版）
		public static bool IsAnsiSupported()
		{
			return Environment.OSVersion.Platform != PlatformID.Win32NT
				|| Environment.GetEnvironmentVariable("WT_SESSION") is not null
				|| Console.BufferWidth > 0; // 启发式判断
		}
	}
}
