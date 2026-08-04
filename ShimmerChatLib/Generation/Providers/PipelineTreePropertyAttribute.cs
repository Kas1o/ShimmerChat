using System;

namespace ShimmerChatLib.Generation
{
    /// <summary>管线类别，决定树属性由哪个树编辑页编辑。</summary>
    public enum ProviderPipelineKind
    {
        PreGeneration,
        PostGeneration,
        RenderModifier
    }

    /// <summary>
    /// 标记提供器配置中的一个管线树参数（属性类型必须为 string?，存储树 JSON，null = 继承 Agent）。
    ///
    /// 与固定的"事件树覆盖"不同，管线树由提供器自行声明：
    /// 事件配置表单（ObjectPropertyForm）会为其渲染"编辑/清除"入口，
    /// 树编辑页通过事件 + 属性名读写 ConfigJson 中对应的值；
    /// 触发生成时宿主通过 <see cref="IGenerationProvider.GetPipelineOverrides"/>
    /// 获取提供器声明的覆盖树。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public class PipelineTreePropertyAttribute : Attribute
    {
        /// <summary>显示名本地化 Key</summary>
        public string LabelKey { get; }

        /// <summary>管线类别</summary>
        public ProviderPipelineKind Kind { get; }

        /// <summary>提示本地化 Key（可选）</summary>
        public string? HintKey { get; init; }

        /// <summary>表单排序</summary>
        public int Order { get; init; }

        public PipelineTreePropertyAttribute(string labelKey, ProviderPipelineKind kind)
        {
            LabelKey = labelKey;
            Kind = kind;
        }
    }

    /// <summary>ObjectPropertyForm 请求编辑某个管线树属性时回传的参数。</summary>
    public record PipelineTreeEditArgs(string PropertyName, ProviderPipelineKind Kind);
}
