using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using ShimmerChatLib.Generation.ToolUi;
using ShimmerChatLib.Interface;

namespace ShimmerChatLib.ChatView.ToolUi
{
    /// <summary>
    /// 工具交互视图契约：实现它的 Blazor 组件 + <see cref="ToolUiViewAttribute"/>
    /// 即被 <see cref="IToolUiViewRegistry"/> 发现，可被工具通过 <see cref="ToolInteractionRequest.ViewId"/> 引用。
    /// <para>
    /// 这是"工具添加特殊 UI"的逃生通道：绝大多数工具只需要 <see cref="ToolUiForm"/> 声明式表单，
    /// 只有真正需要自定义界面（diff 预览、文件选择器、图表）的工具才写组件。
    /// </para>
    /// <para>
    /// 组件不持有对话引用，一切能力都通过 <see cref="ToolUiContext"/> 获得；
    /// 必须同时支持"待回答"（可交互）与"已结束"（只读回显）两种形态。
    /// </para>
    /// </summary>
    public interface IToolUiView
    {
        /// <summary>宿主上下文。实现必须声明为 <c>[Parameter]</c> 属性。</summary>
        ToolUiContext Context { get; set; }
    }

    /// <summary>工具交互视图的注册元数据。</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class ToolUiViewAttribute : Attribute
    {
        public ToolUiViewAttribute(string id, string nameKey)
        {
            Id = id;
            NameKey = nameKey;
        }

        /// <summary>稳定标识，工具用 <see cref="ToolInteractionRequest.ViewId"/> 引用它。不要随重命名改动。</summary>
        public string Id { get; }

        /// <summary>显示名本地化 Key（诊断列表用）。</summary>
        public string NameKey { get; }

        public string? DescriptionKey { get; init; }
    }

    public sealed record ToolUiViewInfo(string Id, string NameKey, string? DescriptionKey, Type ComponentType);

    /// <summary>
    /// 工具交互视图注册表。扫描委托给 <see cref="IPluginLoaderService"/>，
    /// 因此插件提供的视图与内置视图同等被发现。
    /// </summary>
    public interface IToolUiViewRegistry
    {
        IReadOnlyList<ToolUiViewInfo> GetAll();

        /// <summary>按 Id 查找；Id 为空或未注册返回 null（调用方负责降级，注册表不做隐式修补）。</summary>
        ToolUiViewInfo? GetById(string? id);
    }

    public sealed class ToolUiViewRegistry : IToolUiViewRegistry
    {
        private readonly Lazy<IReadOnlyList<ToolUiViewInfo>> _views;

        public ToolUiViewRegistry(IPluginLoaderService pluginLoader, ILogger<ToolUiViewRegistry> logger)
        {
            _views = new Lazy<IReadOnlyList<ToolUiViewInfo>>(() => ScanAll(pluginLoader, logger));
        }

        public IReadOnlyList<ToolUiViewInfo> GetAll() => _views.Value;

        public ToolUiViewInfo? GetById(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            foreach (var view in _views.Value)
            {
                if (string.Equals(view.Id, id, StringComparison.Ordinal))
                    return view;
            }
            return null;
        }

        private static List<ToolUiViewInfo> ScanAll(IPluginLoaderService pluginLoader, ILogger<ToolUiViewRegistry> logger)
        {
            var types = pluginLoader.GetImplementingTypes(typeof(IToolUiView));
            var result = new List<ToolUiViewInfo>(types.Count);
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var type in types)
            {
                try
                {
                    var attribute = type.GetCustomAttribute<ToolUiViewAttribute>(inherit: false);
                    if (attribute == null)
                    {
                        logger.LogError(
                            "工具交互视图 {TypeName} 实现了 IToolUiView 但未标记 [ToolUiView]，已跳过。",
                            type.FullName);
                        continue;
                    }

                    if (!typeof(ComponentBase).IsAssignableFrom(type))
                    {
                        logger.LogError(
                            "工具交互视图 {TypeName} 不是 Blazor 组件（未继承 ComponentBase），已跳过。",
                            type.FullName);
                        continue;
                    }

                    if (!seenIds.Add(attribute.Id))
                    {
                        logger.LogError(
                            "工具交互视图 Id '{ViewId}' 重复（{TypeName}），该视图已被跳过；" +
                            "工具引用时会使用先注册的那个。",
                            attribute.Id, type.FullName);
                        continue;
                    }

                    result.Add(new ToolUiViewInfo(attribute.Id, attribute.NameKey, attribute.DescriptionKey, type));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "扫描工具交互视图类型 {TypeName} 时出错: {Message}", type.FullName, ex.Message);
                }
            }

            return result.OrderBy(v => v.Id, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>
    /// 交给工具交互视图的宿主契约。与 <see cref="ChatViewContext"/> 同风格：只暴露活对象与能力方法。
    /// </summary>
    public sealed class ToolUiContext
    {
        /// <summary>本次交互（含请求描述与创建时间）。</summary>
        public required ToolInteractionPending Pending { get; init; }

        /// <summary>当前对话（与宿主同一实例）。</summary>
        public required Chat Chat { get; init; }

        /// <summary>当前 Agent（与宿主同一实例）。</summary>
        public required Agent Agent { get; init; }

        /// <summary>是否仍在等待回答。false 表示交互已结束（只读回显）。</summary>
        public required Func<bool> IsAwaiting { get; init; }

        /// <summary>提交答案。返回失败时必须向用户说明原因（例如已在其它窗口回答）。</summary>
        public required Func<Newtonsoft.Json.Linq.JObject?, Task<ToolInteractionSubmitOutcome>> SubmitAsync { get; init; }

        /// <summary>跳过本次交互（"不等了，继续"）。</summary>
        public required Func<string, Task<ToolInteractionSubmitOutcome>> SkipAsync { get; init; }

        /// <summary>请求宿主重绘（组件在后台线程改动状态后调用）。</summary>
        public required Func<Task> RequestRefreshAsync { get; init; }
    }
}
