namespace ShimmerChatBuiltin.ImageGeneration
{
    /// <summary>
    /// 文生图后端协议的静态描述。设置面板、配置校验、工具定义与连接探测都读这一份目录，
    /// 不在各自位置分别判断「哪种协议支持什么」。
    /// </summary>
    /// <param name="Kind">协议标识。</param>
    /// <param name="DisplayName">给模型与日志看的英文技术名，不本地化。</param>
    /// <param name="NameKey">面板显示名称的本地化 Key。</param>
    /// <param name="DescriptionKey">面板说明的本地化 Key。</param>
    /// <param name="DefaultBaseUrl">默认 API 根地址。</param>
    /// <param name="SupportsCustomWorkflow">是否接受用户提供的自定义工作流。</param>
    /// <param name="SupportsScheduler">是否使用独立于采样器的调度器参数。</param>
    /// <param name="DocsUrl">协议文档地址。</param>
    public sealed record ImageApiProviderDescriptor(
        ImageApiProvider Kind,
        string DisplayName,
        string NameKey,
        string DescriptionKey,
        string DefaultBaseUrl,
        bool SupportsCustomWorkflow,
        bool SupportsScheduler,
        string DocsUrl);

    /// <summary>内置的文生图服务方目录。</summary>
    public static class ImageApiProviderCatalog
    {
        public static IReadOnlyList<ImageApiProviderDescriptor> All { get; } =
        [
            new(ImageApiProvider.ComfyUi,
                "ComfyUI",
                "imagegen.provider.comfyui",
                "imagegen.provider.comfyui.desc",
                "http://127.0.0.1:8188",
                SupportsCustomWorkflow: true,
                SupportsScheduler: true,
                "https://docs.comfy.org/development/comfyui-server/comms_routes"),

            new(ImageApiProvider.Automatic1111,
                "A1111",
                "imagegen.provider.a1111",
                "imagegen.provider.a1111.desc",
                "http://127.0.0.1:7860",
                SupportsCustomWorkflow: false,
                SupportsScheduler: false,
                "https://github.com/AUTOMATIC1111/stable-diffusion-webui/wiki/API")
        ];

        public static ImageApiProviderDescriptor? Find(ImageApiProvider kind) =>
            All.FirstOrDefault(d => d.Kind == kind);

        /// <summary>取描述，找不到时抛出——调用前必须保证 kind 来自本目录。</summary>
        public static ImageApiProviderDescriptor Get(ImageApiProvider kind) =>
            Find(kind) ?? throw new ArgumentOutOfRangeException(nameof(kind), kind,
                "Unknown image API provider. Only providers listed in ImageApiProviderCatalog are supported.");
    }
}
