using System;
using System.Collections.Generic;
using System.Text;

namespace ShimmerChatBuiltin.DynPrompt
{
	public class DynPromptSet
	{
		public required string Name { get; set; }
		public List<DynPromptTerm> Terms { get; set; } = new List<DynPromptTerm>();
	}

	public class DynPromptTerm
	{
		public required string Name { get; set; }
		public required string Content { get; set; }
		public DynPromptTermInjectionMode InjectionMode { get; set; } = DynPromptTermInjectionMode.BeforeSystem;
		public int InjectionDepth { get; set; } = 4;
		public string TriggerRule { get; set; } = "";
		public bool AllowBeTriggeredByRecursive { get; set; } = false;
	}

	public enum DynPromptTermInjectionMode
	{
		AtDepth,
		BeforeSystem,
		AfterSystem,
	}
}
