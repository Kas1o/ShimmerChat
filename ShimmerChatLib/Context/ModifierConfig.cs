using System.Reflection;

namespace ShimmerChatLib.Context
{
	public abstract class ModifierConfig
	{
		public override string ToString()
		{
			var props = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
				.Where(p => p.CanRead && p.DeclaringType != typeof(ModifierConfig))
				.Select(p => $"{p.Name}={p.GetValue(this)?.ToString() ?? "null"}")
				.ToList();

			return props.Count > 0 ? string.Join(", ", props) : GetType().Name;
		}
	}
}
