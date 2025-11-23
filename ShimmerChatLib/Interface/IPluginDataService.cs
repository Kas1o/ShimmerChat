using System;
using System.Collections.Generic;
using System.Text;

namespace ShimmerChatLib.Interface
{
	public interface IPluginDataService
	{
		public string? Read(string pluginId, string key);
		public void Write(string pluginId, string key, string value);
	}
}
