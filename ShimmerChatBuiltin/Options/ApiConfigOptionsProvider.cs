using Microsoft.Extensions.DependencyInjection;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Options
{
    /// <summary>
    /// API 配置下拉：按类型分组，副标题带上模型名与是否流式，
    /// 全局选中的那条追加 ★（空值选项仍表示「跟随全局选中」）。
    /// </summary>
    public sealed class ApiConfigOptionsProvider : KVListOptionsProvider<ApiConfig>
    {
        private const string Space = "ApiSettings";
        private const string Store = "apiSetting";
        private const string SelectedGuidKey = "selectedAPIGuid";

        public override string Key => "api_configs";

        protected override string SpaceId => Space;

        protected override string StoreKey => Store;

        protected override string GetId(ApiConfig item, IServiceProvider services) => item.Id.ToString();

        protected override string GetLabel(ApiConfig item, IServiceProvider services)
        {
            if (services.GetService(typeof(IKVDataService)) is IKVDataService kv
                && string.Equals(kv.Read(Space, SelectedGuidKey), item.Id.ToString(), StringComparison.Ordinal))
            {
                return $"{item.Name} ★";
            }

            return item.Name;
        }

        protected override string? GetGroup(ApiConfig item, IServiceProvider services) => item.Type.ToString();

        protected override string? GetDescription(ApiConfig item, IServiceProvider services)
        {
            var stream = item.EnableStream
                ? OptionsProviderLoc.Get(services, "node.api_select.streaming", "streaming")
                : OptionsProviderLoc.Get(services, "node.api_select.non_streaming", "non-streaming");

            var model = item.ModelName;
            return string.IsNullOrWhiteSpace(model) ? stream : $"{model} · {stream}";
        }

        protected override IReadOnlyList<string>? GetSearchTerms(ApiConfig item, IServiceProvider services) =>
            [item.Type.ToString(), item.Id.ToString()];
    }
}
