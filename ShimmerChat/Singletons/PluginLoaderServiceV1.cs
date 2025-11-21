using System.Reflection;

namespace ShimmerChat.Singletons
{
    public class PluginLoaderServiceV1 : IPluginLoaderService
    {
        public List<T> LoadImplementations<T>()
        {
            var implementations = new List<T>();
            
            // 加载所有已加载的程序集中的实现
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var assemblyImplementations = LoadImplementationsFromAssembly<T>(assembly);
                implementations.AddRange(assemblyImplementations);
            }
            
            return implementations;
        }

        public List<T> LoadImplementationsFromAssembly<T>(Assembly assembly)
        {
            var implementations = new List<T>();
            
            try
            {
                var targetType = typeof(T);
                var types = assembly.GetTypes()
                    .Where(t => targetType.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var type in types)
                {
                    try
                    {
                        if (Activator.CreateInstance(type) is T instance)
                        {
                            implementations.Add(instance);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"无法创建 {type.Name} 的实例: {ex.Message}");
                    }
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                Console.WriteLine($"从程序集 {assembly.FullName} 加载类型时出错: {ex.Message}");
                foreach (var loaderException in ex.LoaderExceptions)
                {
                    Console.WriteLine($"加载异常: {loaderException.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理程序集 {assembly.FullName} 时出错: {ex.Message}");
            }
            
            return implementations;
        }

        public List<T> LoadImplementationsFromPlugins<T>(string pluginsFolder)
        {
            var implementations = new List<T>();
            
            if (!Directory.Exists(pluginsFolder))
            {
                return implementations;
            }
            
            foreach (var dll in Directory.GetFiles(pluginsFolder, "*.dll"))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(dll);
                    var assemblyImplementations = LoadImplementationsFromAssembly<T>(assembly);
                    implementations.AddRange(assemblyImplementations);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"加载插件 {dll} 失败: {ex.Message}");
                }
            }
            
            return implementations;
        }


        public List<Type> GetTypesWithAttributeFromAssembly<TAttribute>(Assembly assembly) where TAttribute : Attribute
        {
            var types = new List<Type>();
            
            try
            {
                var attributeType = typeof(TAttribute);
                var assemblyTypes = assembly.GetTypes()
                    .Where(t => t.GetCustomAttributes(attributeType, false).Any());
                
                types.AddRange(assemblyTypes);
            }
            catch (ReflectionTypeLoadException ex)
            {
                Console.WriteLine($"从程序集 {assembly.FullName} 获取标记类型时出错: {ex.Message}");
                foreach (var loaderException in ex.LoaderExceptions)
                {
                    Console.WriteLine($"加载异常: {loaderException.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理程序集 {assembly.FullName} 时出错: {ex.Message}");
            }
            
            return types;
        }

        public List<Type> GetTypesWithAttributeFromPlugins<TAttribute>(string pluginsFolder) where TAttribute : Attribute
        {
            var types = new List<Type>();
            
            if (!Directory.Exists(pluginsFolder))
            {
                return types;
            }
            
            foreach (var dll in Directory.GetFiles(pluginsFolder, "*.dll"))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(dll);
                    var assemblyTypes = GetTypesWithAttributeFromAssembly<TAttribute>(assembly);
                    types.AddRange(assemblyTypes);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"加载插件 {dll} 失败: {ex.Message}");
                }
            }
            
            return types;
        }
    }
}