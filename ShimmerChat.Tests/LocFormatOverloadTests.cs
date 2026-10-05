using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests;

/// <summary>
/// <see cref="ILocService"/> 有两个 <c>Format</c> 重载（位置插值 / 命名插值），
/// 且两者的首个参数都是字符串，因此「传一个纯字符串参数」时的重载选择很容易出错：
/// 若命中命名插值重载，占位符 <c>{0}</c> 不会被替换，界面上会直接显示花括号。
/// 这里把该契约固化下来。
/// </summary>
public class LocFormatOverloadTests
{
    /// <summary>记录实际命中的重载，并模拟生产行为（位置插值走 string.Format）。</summary>
    private sealed class RecordingLoc : ILocService
    {
        public string CurrentCulture => "en-US";
        public IReadOnlyList<string> SupportedCultures => ["en-US"];
        public string this[string key] => key;
        public void SetCulture(string culture) { }

        public int PositionalCalls { get; private set; }
        public int NamedCalls { get; private set; }

        public string Format(string key, params object[] args)
        {
            PositionalCalls++;
            return string.Format(key, args);
        }

        public string Format(string key, params (string name, object value)[] args)
        {
            NamedCalls++;
            return key;
        }
    }

    [Fact]
    public void SingleStringArgument_ResolvesToPositionalOverload()
    {
        ILocService loc = new RecordingLoc();
        var recording = (RecordingLoc)loc;

        var result = loc.Format("变量 '{0}' 已添加", "x");

        Assert.Equal(1, recording.PositionalCalls);
        Assert.Equal(0, recording.NamedCalls);
        Assert.Equal("变量 'x' 已添加", result);
    }

    [Fact]
    public void ExplicitObjectArray_ResolvesToPositionalOverload()
    {
        ILocService loc = new RecordingLoc();
        var recording = (RecordingLoc)loc;

        var result = loc.Format("错误: {0}", new object[] { "boom" });

        Assert.Equal(1, recording.PositionalCalls);
        Assert.Equal("错误: boom", result);
    }
}
