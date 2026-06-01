namespace ShimmerChatLib.Context
{
	[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
	public class UiHintAttribute : Attribute
	{
		public string Label { get; }
		public string Description { get; }
		public object? Min { get; init; }
		public object? Max { get; init; }

		public UiHintAttribute(string label, string description)
		{
			Label = label;
			Description = description;
		}
	}
}
