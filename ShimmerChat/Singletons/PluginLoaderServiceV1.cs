using Microsoft.Extensions.Logging;
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
        private readonly ILogger<PluginLoaderServiceV1> _logger;

        public PluginLoaderServiceV1(IServiceProvider serviceProvider, ILogger<PluginLoaderServiceV1> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            PreloadPlugins();
        }

        private void PreloadPlugins()
        {
            var pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
            if (!Directory.Exists(pluginsDir))
                return;

            foreach (var dir in Directory.GetDirectories(pluginsDir))
            {
                var ctxLogger = _serviceProvider.GetRequiredService<ILogger<PluginLoadContext>>();
                var ctx = new PluginLoadContext(dir, ctxLogger);
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
                    _logger.LogError(ex, "Plugin initializer {TypeName} failed: {Message}", type.FullName, ex.Message);
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
                    _logger.LogError(ex, "从程序集 {AssemblyName} 加载类型时出错: {Message}", assembly.FullName, ex.Message);
                    foreach (var loaderException in ex.LoaderExceptions)
                        _logger.LogError("加载异常: {Message}", loaderException.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理程序集 {AssemblyName} 时出错: {Message}", assembly.FullName, ex.Message);
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
                        _logger.LogError(ex, "无法创建 {TypeName} 的实例: {Message}", type.Name, ex.Message);
                    }
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                _logger.LogError(ex, "从程序集 {AssemblyName} 加载类型时出错: {Message}", assembly.FullName, ex.Message);
                foreach (var loaderException in ex.LoaderExceptions)
                    _logger.LogError("加载异常: {Message}", loaderException.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理程序集 {AssemblyName} 时出错: {Message}", assembly.FullName, ex.Message);
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
                _logger.LogError(ex, "从程序集 {AssemblyName} 获取标记类型时出错: {Message}", assembly.FullName, ex.Message);
                foreach (var loaderException in ex.LoaderExceptions)
                    _logger.LogError("加载异常: {Message}", loaderException.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理程序集 {AssemblyName} 时出错: {Message}", assembly.FullName, ex.Message);
            }

            return types;
        }
    }
}
