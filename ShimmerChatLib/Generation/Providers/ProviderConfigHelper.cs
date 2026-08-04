using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 提供器配置序列化辅助。配置 = 提供器实例中标记了
    /// <see cref="NodePropertyAttribute"/> 或 <see cref="PipelineTreePropertyAttribute"/>
    /// 的公有可读写属性，避免把构造函数注入的服务等无关成员写入 ConfigJson。
    /// </summary>
    public static class ProviderConfigHelper
    {
        /// <summary>序列化提供器配置属性为 JSON 字符串</summary>
        public static string Serialize(object instance)
        {
            var obj = new JObject();
            foreach (var prop in GetConfigProperties(instance.GetType()))
            {
                var value = prop.GetValue(instance);
                obj[prop.Name] = value != null ? JToken.FromObject(value) : JValue.CreateNull();
            }
            return obj.ToString(Formatting.None);
        }

        /// <summary>用 ConfigJson 填充提供器实例的配置属性（空 JSON 时不做任何操作）</summary>
        public static void Populate(object instance, string? configJson)
        {
            if (string.IsNullOrEmpty(configJson)) return;
            JsonConvert.PopulateObject(configJson, instance);
        }

        /// <summary>
        /// 读取指定管线树属性（<see cref="PipelineTreePropertyAttribute"/>）的树 JSON。
        /// 属性不存在或未标记时抛出 ArgumentException（调用方需明确报错）。
        /// </summary>
        public static string? GetTreeJson(object instance, string propertyName)
            => GetTreeProperty(instance.GetType(), propertyName).GetValue(instance) as string;

        /// <summary>写入指定管线树属性的树 JSON（null = 清除覆盖，继承 Agent）</summary>
        public static void SetTreeJson(object instance, string propertyName, string? treeJson)
            => GetTreeProperty(instance.GetType(), propertyName).SetValue(instance, treeJson);

        /// <summary>获取类型中全部配置属性（NodeProperty 或 PipelineTreeProperty 标记）</summary>
        public static IEnumerable<PropertyInfo> GetConfigProperties(Type type)
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite
                    && (p.GetCustomAttribute<NodePropertyAttribute>() != null
                        || p.GetCustomAttribute<PipelineTreePropertyAttribute>() != null));
        }

        private static PropertyInfo GetTreeProperty(Type type, string propertyName)
        {
            var prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new ArgumentException(
                    $"Property '{propertyName}' not found on provider type '{type.FullName}'.");
            if (prop.GetCustomAttribute<PipelineTreePropertyAttribute>() == null)
                throw new ArgumentException(
                    $"Property '{propertyName}' on '{type.FullName}' is not a pipeline tree property.");
            return prop;
        }
    }
}
