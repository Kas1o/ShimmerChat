using System;
using System.Collections.Generic;
using System.Text;

namespace ShimmerChatBuiltin.DynPrompt
{
	public class DynPromptSet
	{
		/// <summary>稳定持久化标识；旧数据缺失时在加载阶段补齐。</summary>
		public string Id { get; set; } = Guid.NewGuid().ToString("N");

		public required string Name { get; set; }
		public List<DynPromptTerm> Terms { get; set; } = new List<DynPromptTerm>();

		/// <summary>收藏标记；只影响集合选择器与检索列表的排序。</summary>
		public bool Pinned { get; set; }

		/// <summary>复制集合；Id 由调用方重新分配。</summary>
		public DynPromptSet Clone() => new()
		{
			Id = string.Empty,
			Name = Name,
			Terms = Terms.Select(term => term.Clone()).ToList(),
			Pinned = Pinned
		};
	}

	public class DynPromptTerm
	{
		public required string Name { get; set; }
		public required string Content { get; set; }
		public DynPromptTermInjectionMode InjectionMode { get; set; } = DynPromptTermInjectionMode.BeforeSystem;
		public int InjectionDepth { get; set; } = 4;
		public string TriggerRule { get; set; } = "";
		public bool AllowBeTriggeredByRecursive { get; set; } = false;

		public DynPromptTerm Clone() => new()
		{
			Name = Name,
			Content = Content,
			InjectionMode = InjectionMode,
			InjectionDepth = InjectionDepth,
			TriggerRule = TriggerRule,
			AllowBeTriggeredByRecursive = AllowBeTriggeredByRecursive
		};
	}

	public enum DynPromptTermInjectionMode
	{
		AtDepth,
		BeforeSystem,
		AfterSystem,
	}
}
