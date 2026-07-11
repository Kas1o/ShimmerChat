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
        private static readonly Assembly HostAssembly = typeof(Program).Assembly;
        private static readonly Assembly LibAssembly = typeof(IPluginInitializer).Assembly;
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
            return GetAssemblies().SelectMany(a => LoadFromAssembly<T>(a)).ToList();
        }

        public List<Type> GetTypesWithAttribute<TAttribute>() where TAttribute : Attribute
        {
            return GetAssemblies().SelectMany(a => GetAttrTypesFromAssembly<TAttribute>(a)).ToList();
        }

        public async Task InitializePluginsAsync()
        {
            var types = GetImplementingTypes(typeof(IPluginInitializer));
            foreach (var type in types)
            {
                try
                {
                    var initializer = (IPluginInitializer)ActivatorUtilities.CreateInstance(_serviceProvider, type);
                    await initializer.InitializeAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Plugin initializer {type.FullName} failed: {ex.Message}");
                }
            }
        }

        public List<Type> GetImplementingTypes(Type interfaceType)
        {
            var types = new List<Type>();

            foreach (var assembly in GetAssemblies())
            {
                try
                {
                    foreach (var t in assembly.GetTypes())
                    {
                        if (interfaceType.IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                            types.Add(t);
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
            }

            return types;
        }

        public IEnumerable<Assembly> GetAssemblies()
        {
            yield return LibAssembly;
            yield return BuiltinAssembly;
            yield return HostAssembly;

            foreach (var ctx in _pluginContexts)
                foreach (var asm in ctx.Assemblies)
                    yield return asm;
        }

        // ---- private helpers ----

        private List<T> LoadFromAssembly<T>(Assembly assembly)
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

        private List<Type> GetAttrTypesFromAssembly<TAttribute>(Assembly assembly) where TAttribute : Attribute
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
    }
}
