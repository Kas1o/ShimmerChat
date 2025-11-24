using System;
using System.Collections.Generic;
using System.Text;

namespace ShimmerChatLib.Interface
{
	public interface IKVDataService
	{
		public string? Read(string spaceId, string key);
		public void Write(string spaceId, string key, string value);
	}
}
