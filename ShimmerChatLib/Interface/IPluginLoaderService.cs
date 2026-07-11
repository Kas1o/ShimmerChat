using System.Reflection;

namespace ShimmerChatLib.Interface
{
    public interface IPluginLoaderService
    {
        /// <summary>
        /// 获取所有应用程序集（Builtin + 宿主 + 插件），确定性顺序。
        /// </summary>
        IEnumerable<Assembly> GetAssemblies();

        /// <summary>
        /// 从所有应用程序集加载指定类型的所有实现并实例化。
        /// </summary>
        List<T> LoadImplementations<T>();

        /// <summary>
        /// 从所有应用程序集获取标记了指定 Attribute 的所有类型（不实例化）。
        /// </summary>
        List<Type> GetTypesWithAttribute<TAttribute>() where TAttribute : Attribute;

        /// <summary>
        /// 从所有应用程序集获取实现了指定接口的具体类型（不实例化）。
        /// </summary>
        List<Type> GetImplementingTypes(Type interfaceType);

        /// <summary>
        /// 发现并执行所有 <see cref="IPluginInitializer"/> 实现。
        /// </summary>
        Task InitializePluginsAsync();
    }
}
