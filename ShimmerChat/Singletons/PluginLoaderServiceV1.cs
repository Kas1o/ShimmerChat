using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using ShimmerChatBuiltin;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    public class PluginLoaderServiceV1 : IPluginLoaderService
    {
        private static readonly Assembly BuiltinAssembly = typeof(Target).Assembly;
        private readonly IServiceProvider _serviceProvider;
        private readonly List<AssemblyLoadContext> _pluginContexts = new();

        public PluginLoaderServiceV1(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            PreloadPlugins();
        }

        private void PreloadPlugins()
        {
            var pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
            if (!Directory.Exists(pluginsDir))
                return;

            foreach (var dir in Directory.GetDirectories(pluginsDir))
            {
                var ctx = new PluginLoadContext(dir);
                if (ctx.TryLoadFromManifest(dir))
                    _pluginContexts.Add(ctx);
            }
        }

        public List<T> LoadImplementations<T>()
        {
            return GetAllAssemblies().SelectMany(a => LoadImplementationsFromAssembly<T>(a)).ToList();
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
                        var instance = (T)ActivatorUtilities.CreateInstance(_serviceProvider, type);
                        if (instance != null)
                            implementations.Add(instance);
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
                    Console.WriteLine($"加载异常: {loaderException.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理程序集 {assembly.FullName} 时出错: {ex.Message}");
            }

            return implementations;
        }

        public List<T> LoadImplementationsFromPlugins<T>(string pluginsFolder)
        {
            return GetPluginAssemblies().SelectMany(a => LoadImplementationsFromAssembly<T>(a)).ToList();
        }

        public List<Type> GetTypesWithAttributeFromAssembly<TAttribute>(Assembly assembly) where TAttribute : Attribute
        {
            var types = new List<Type>();

            try
            {
                var attributeType = typeof(TAttribute);
                types.AddRange(assembly.GetTypes().Where(t => t.GetCustomAttributes(attributeType, false).Any()));
            }
            catch (ReflectionTypeLoadException ex)
            {
                Console.WriteLine($"从程序集 {assembly.FullName} 获取标记类型时出错: {ex.Message}");
                foreach (var loaderException in ex.LoaderExceptions)
                    Console.WriteLine($"加载异常: {loaderException.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理程序集 {assembly.FullName} 时出错: {ex.Message}");
            }

            return types;
        }

        public List<Type> GetTypesWithAttributeFromPlugins<TAttribute>(string pluginsFolder) where TAttribute : Attribute
        {
            return GetPluginAssemblies().SelectMany(a => GetTypesWithAttributeFromAssembly<TAttribute>(a)).ToList();
        }

        public List<Type> GetImplementingTypes(Type interfaceType)
        {
            var types = new List<Type>();

            foreach (var assembly in GetAllAssemblies())
            {
                try
                {
                    foreach (var t in assembly.GetExportedTypes())
                    {
                        if (interfaceType.IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                            types.Add(t);
                    }
                }
                catch { }
            }

            return types;
        }

        /// <summary>所有扫描范围：默认 ALC + 插件 ALC + Builtin 特例。</summary>
        private IEnumerable<Assembly> GetAllAssemblies()
        {
            var seen = new HashSet<Assembly>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                if (!asm.IsDynamic) seen.Add(asm);

            foreach (var ctx in _pluginContexts)
                foreach (var asm in ctx.Assemblies)
                    seen.Add(asm);

            seen.Add(BuiltinAssembly);
            return seen;
        }

        /// <summary>仅插件 ALC 中的程序集。</summary>
        private IEnumerable<Assembly> GetPluginAssemblies()
        {
            return _pluginContexts.SelectMany(ctx => ctx.Assemblies);
        }
    }
}
