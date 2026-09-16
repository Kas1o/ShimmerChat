using System;
using System.Collections.Generic;

namespace ShimmerChatLib.Interface
{
	/// <summary>
	/// 插件面板草稿存储。宿主按回路（circuit）保存，
	/// 使面板的输入草稿在折叠、重新渲染或切换面板后依然存在。
	/// </summary>
	public interface IPanelDraftStore
	{
		/// <summary>取得指定键的值；不存在时用 <paramref name="factory"/> 创建并保存。</summary>
		string GetOrCreate(string key, Func<string> factory);

		/// <summary>按键读写草稿。</summary>
		string this[string key] { get; set; }

		/// <summary>清除指定键的草稿。</summary>
		void Remove(string key);

		/// <summary>当前保存的草稿数量。</summary>
		int Count { get; }
	}
}
