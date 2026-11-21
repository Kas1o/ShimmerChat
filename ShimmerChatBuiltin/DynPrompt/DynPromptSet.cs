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

		/// <summary>合并到第一个 system 片段。依赖上下文里已有 system 片段，位置不灵活，不推荐。</summary>
		BeforeSystem,

		/// <summary>合并到第一个 system 片段。依赖上下文里已有 system 片段，位置不灵活，不推荐。</summary>
		AfterSystem,

		/// <summary>
		/// 注入到 <see cref="DynPromptSlotNode"/> 标记的唯一槽位；位置显式、唯一，推荐使用。
		/// 新增值只能追加在末尾：旧数据按枚举数值序列化，插入中间会改变既有词条的含义。
		/// </summary>
		InjectionSlot,
	}
}
