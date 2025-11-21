using System.Reflection;

namespace ShimmerChat.Singletons
{
    public interface IPluginLoaderService
    {
        /// <summary>
        /// 加载指定类型的所有实现
        /// </summary>
        /// <typeparam name="T">要加载的接口类型</typeparam>
        /// <returns>实现了指定接口的所有实例</returns>
        List<T> LoadImplementations<T>();

        /// <summary>
        /// 从指定程序集中加载实现
        /// </summary>
        /// <typeparam name="T">要加载的接口类型</typeparam>
        /// <param name="assembly">要加载的程序集</param>
        /// <returns>实现了指定接口的所有实例</returns>
        List<T> LoadImplementationsFromAssembly<T>(Assembly assembly);

        /// <summary>
        /// 从插件目录加载实现
        /// </summary>
        /// <typeparam name="T">要加载的接口类型</typeparam>
        /// <param name="pluginsFolder">插件目录路径</param>
        /// <returns>实现了指定接口的所有实例</returns>
        List<T> LoadImplementationsFromPlugins<T>(string pluginsFolder);

        /// <summary>
        /// 从指定程序集中获取标记了指定Attribute的所有类型
        /// </summary>
        /// <typeparam name="TAttribute">要查找的Attribute类型</typeparam>
        /// <param name="assembly">要加载的程序集</param>
        /// <returns>标记了指定Attribute的所有类型</returns>
        List<Type> GetTypesWithAttributeFromAssembly<TAttribute>(Assembly assembly) where TAttribute : Attribute;

        /// <summary>
        /// 从插件目录获取标记了指定Attribute的所有类型
        /// </summary>
        /// <typeparam name="TAttribute">要查找的Attribute类型</typeparam>
        /// <param name="pluginsFolder">插件目录路径</param>
        /// <returns>标记了指定Attribute的所有类型</returns>
        List<Type> GetTypesWithAttributeFromPlugins<TAttribute>(string pluginsFolder) where TAttribute : Attribute;
    }
}