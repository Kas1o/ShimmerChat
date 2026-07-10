namespace ShimmerChatLib.Interface
{
    /// <summary>
    /// 本地化服务接口。提供 Key → 显示字符串的查找，找不到时回退到 Key 本身。
    /// </summary>
    public interface ILocService
    {
        /// <summary>当前使用的区域性名称</summary>
        string CurrentCulture { get; }

        /// <summary>支持的区域性列表</summary>
        IReadOnlyList<string> SupportedCultures { get; }

        /// <summary>通过 Key 获取本地化字符串。找不到时返回 Key 本身。</summary>
        string this[string key] { get; }

        /// <summary>带位置插值的本地化字符串。占位符: {0}, {1}</summary>
        string Format(string key, params object[] args);

        /// <summary>带命名插值的本地化字符串。占位符: {name}</summary>
        string Format(string key, params (string name, object value)[] args);

        /// <summary>切换语言，立即生效并持久化</summary>
        void SetCulture(string culture);
    }
}
