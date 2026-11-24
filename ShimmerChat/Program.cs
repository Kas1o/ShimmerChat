using ShimmerChat.Components;
using ShimmerChat.Singletons;
using ShimmerChat.Singletons.Storage;
using ShimmerChat.Singletons.VectorStore;
using ShimmerChatLib;
using ShimmerChatLib.ChatView;
using ShimmerChatLib.Generation;
using ShimmerChatLib.HomeWidget;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;
using ShimmerChatLib.Storage;
using Microsoft.Extensions.FileProviders;
using System.Reflection;

Console.WriteLine(
    """
    ===========================
    ShimmerChat Starting...
    ===========================
    """);

var infoVer = typeof(Program).Assembly
    .GetCustomAttributes<AssemblyInformationalVersionAttribute>()
    ?.First()?.InformationalVersion ?? "Unknown";

Console.WriteLine($"Version: {infoVer}");


var builder = WebApplication.CreateBuilder(args);

// 数据布局：所有持久化数据都在 Data/ 下。必须在任何数据库被打开前确定，
// 并在服务注册前完成旧目录结构的迁移。
var dataPaths = DataPaths.Create(
    AppContext.BaseDirectory,
    builder.Configuration[DataPaths.RootConfigurationKey]);
DataPaths.Configure(dataPaths);

var storageSettingsStore = new StorageSettingsStore(dataPaths);
var legacyStoreConfig = builder.Configuration.GetSection("KVDataStorage").Get<StoreStorageConfig>();
var legacyVectorConfig = builder.Configuration.GetSection("VectorStore").Get<VectorStoreConfig>();
var storageDefaults = StorageSettings.CreateDefaults(legacyStoreConfig, legacyVectorConfig);

if (!File.Exists(dataPaths.StorageSettingsFile))
{
    // 首次生成配置：旧版只有一个全局开关，且 appsettings 可能已被升级覆盖；
    // 按旧数据实际所在位置逐仓库推断，避免文件后端用户升级后看到"数据消失"。
    LegacyStorageDetector.ApplyToDefaults(
        storageDefaults,
        AppContext.BaseDirectory,
        legacyConfigPresent: legacyStoreConfig is not null,
        message => Console.WriteLine($"[Storage] {message}"));
}

var storageSettings = storageSettingsStore.LoadOrCreate(storageDefaults, out var storageSettingsCreated);

Console.WriteLine(storageSettingsCreated
    ? $"[Storage] 已生成存储配置：{dataPaths.StorageSettingsFile}"
    : $"[Storage] 使用存储配置：{dataPaths.StorageSettingsFile}");

if (legacyStoreConfig is not null || legacyVectorConfig is not null)
{
    Console.WriteLine(storageSettingsCreated
        ? "[Storage] 已把 appsettings.json 中的旧 KVDataStorage/VectorStore 写入初始 storage_settings.json。"
        : "[Storage] appsettings.json 中的 KVDataStorage/VectorStore 已被 Data/storage_settings.json 取代，本次启动忽略。");
}

var legacyLayoutMigrator = new StorageLayoutMigrator(
    AppContext.BaseDirectory,
    dataPaths,
    message => Console.WriteLine($"[Storage] {message}"));

legacyLayoutMigrator.Migrate(
    builder.Configuration["VectorStore:RootPath"],
    legacyStoreConfig?.GetStorageType() ?? KVStorageType.LiteDB);

builder.Services.AddSingleton(dataPaths);
builder.Services.AddSingleton(storageSettings);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSignalR(options =>
{
    // 编辑长文本时总是触发重连，从默认的 32KiB 提升到 50MiB。
	options.MaximumReceiveMessageSize = 50 * 1024 * 1024;
});

// 配置各数据仓库（KV / 聊天消息 / 调试输出）的存储实现
ConfigureStorageServices(builder, dataPaths, storageSettings);

// 配置向量存储（LiteDb / File / InMemory，实现由 storage_settings.json 的 VectorStore 决定）
builder.Services.AddShimmerVectorStore(storageSettings.VectorStore, dataPaths);

// ShimmerChat 2.0 服务
builder.Services.AddSingleton<IToolRegistry, ToolRegistry>();
builder.Services.AddSingleton<IPreGenerationNodeSerializer, PreGenerationNodeSerializer>();
builder.Services.AddSingleton<INodeTypeCatalog, NodeTypeCatalog>();
builder.Services.AddSingleton<IGenerationManagerV2, GenerationManagerV2>();
builder.Services.AddSingleton<GenerationSessionService>();

// Post-Generation 管线
builder.Services.AddSingleton<PostGenerationNodeSerializerService>();
builder.Services.AddSingleton<IPostGenerationNodeSerializerService>(sp => sp.GetRequiredService<PostGenerationNodeSerializerService>());
builder.Services.AddSingleton<IPostGenerationManagerService, PostGenerationManagerService>();

// Render Modifier 管线
builder.Services.AddSingleton<RenderModifierNodeSerializer>();
// 接口注册：Creator 工具集等按管线分派的调用方需要以统一方式解析三种序列化器
builder.Services.AddSingleton<IRenderModifierNodeSerializer>(sp => sp.GetRequiredService<RenderModifierNodeSerializer>());
builder.Services.AddSingleton<IRenderModifierManager, RenderModifierManager>();

// 生成提供器系统（Agent 级生成事件：CRON / Hook / 聊天软件集成等）
builder.Services.AddSingleton<IGenerationProviderRegistry, GenerationProviderRegistry>();
builder.Services.AddSingleton<IGenerationEventStore, GenerationEventStore>();
builder.Services.AddSingleton<GenerationProviderHostService>();
builder.Services.AddSingleton<IProviderTriggerService>(sp => sp.GetRequiredService<GenerationProviderHostService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<GenerationProviderHostService>());

builder.Services.AddSingleton<IAgentMigrationService, AgentMigrationService>();
builder.Services.AddSingleton<IChatViewRegistry, ChatViewRegistry>();
builder.Services.AddSingleton<IHomeWidgetRegistry, HomeWidgetRegistry>();
builder.Services.AddSingleton<IHomeWidgetLayoutService, HomeWidgetLayoutService>();
builder.Services.AddSingleton<IChatCreationService, ChatCreationService>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IGitHubReleaseService, GitHubReleaseService>();
builder.Services.AddSingleton<IToolInteractionService, ToolInteractionService>();
builder.Services.AddSingleton<ShimmerChatLib.ChatView.ToolUi.IToolUiViewRegistry,
    ShimmerChatLib.ChatView.ToolUi.ToolUiViewRegistry>();
builder.Services.AddSingleton<IPluginLoaderService, PluginLoaderServiceV1>();
builder.Services.AddSingleton<IImageAttachmentService, ImageAttachmentService>();
builder.Services.AddSingleton<IPluginPanelService, PluginPanelServiceV1>();
builder.Services.AddSingleton<IPopupService, PopupService>(); // TODO: 大概需要改成 Scoped。
builder.Services.AddSingleton<IMessageDisplayService, MessageDisplayServiceV1>();
builder.Services.AddScoped<IPanelDraftStore, PanelDraftStore>();
builder.Services.AddScoped<IThemeService, ThemeServiceV2>();
builder.Services.AddSingleton<ILocService, LocService>();

var app = builder.Build();

// 执行各仓库的存储后端迁移（如果配置要求）
ExecuteStorageAutoMigration(app);

// ShimmerChat 2.0: 执行 Agent 数据迁移
ExecuteAgentMigration(app);

// 执行插件初始化
ExecutePluginInitializers(app);

// 注册插件静态资源：/pluginstatic/{pluginName}/... → Plugins/{plugin}/www
RegisterPluginStaticFiles(app);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

 
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// 配置用户上传图片 Configure User Upload Image
var userImagePath = dataPaths.UserUploadImageDirectory;
if (!Directory.Exists(userImagePath))
    Directory.CreateDirectory(userImagePath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(userImagePath),
    RequestPath = "/userimages"
});

// Tauri sidecar mode: start async and emit ready signal on stdout
await app.StartAsync();

var urls = app.Urls;
var targetUrl = urls.FirstOrDefault(u => u.StartsWith("http://127.0.0.1:"))
                ?? urls.FirstOrDefault(u => u.StartsWith("http://localhost:"))
                ?? urls.FirstOrDefault()
                ?? "http://127.0.0.1:5000";
Console.WriteLine($"SHIMMER_READY:{targetUrl}");

await app.WaitForShutdownAsync();
return;

// ShimmerChat 2.0: 自动迁移 Agent 到 2.0
static void ExecuteAgentMigration(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var migrationService = scope.ServiceProvider.GetRequiredService<IAgentMigrationService>();
    try
    {
        int count = migrationService.MigrateAll();
        Console.WriteLine($"[ShimmerChat 2.0] Agent migration completed. {count} agents migrated.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ShimmerChat 2.0] Agent migration error: {ex.Message}");
    }
}

// 执行所有插件的初始化器
static void ExecutePluginInitializers(WebApplication app)
{
    try
    {
        var pluginLoader = app.Services.GetRequiredService<IPluginLoaderService>();
        pluginLoader.InitializePluginsAsync().GetAwaiter().GetResult();
        Console.WriteLine("[ShimmerChat] Plugin initializers completed.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ShimmerChat] Plugin initialization error: {ex.Message}");
    }
}

// 为在 plugin.json 中声明了 "static" 字段的插件注册静态文件中间件
static void RegisterPluginStaticFiles(WebApplication app)
{
    try
    {
        var loader = app.Services.GetRequiredService<IPluginLoaderService>() as PluginLoaderServiceV1;
        var mappings = loader?.GetStaticFileMappings() ?? [];

        foreach (var mapping in mappings)
        {
            if (!Directory.Exists(mapping.StaticDirPath))
            {
                Console.WriteLine($"[PluginStatic] {mapping.UrlSegment}: directory missing, skipped.");
                continue;
            }

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(mapping.StaticDirPath),
                RequestPath = $"/pluginstatic/{mapping.UrlSegment}"
            });
            Console.WriteLine($"[PluginStatic] /pluginstatic/{mapping.UrlSegment}/ -> {mapping.StaticDirPath}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[PluginStatic] Failed to register plugin static resources: {ex.Message}");
    }
}

// 按仓库（KV / 聊天消息 / 调试输出）分别注册存储实现与迁移服务
static void ConfigureStorageServices(
    WebApplicationBuilder builder,
    DataPaths paths,
    StorageSettings settings)
{
    builder.Services.AddSingleton<LiteDatabaseFactory>();
    builder.Services.AddSingleton<IStorageMigrationMarker>(_ => new StorageMigrationMarker(
        paths,
        // 旧版标记只读兼容：新标记缺失时仍能判断升级前的迁移状态。
        [Path.Combine(AppContext.BaseDirectory, "KVData", ".migration_marker")]));

    // 两种实现始终注册：迁移服务需要同时访问源与目标仓库。
    builder.Services.AddSingleton(_ => new FileMessageStoreService(paths.LocalChatMessageDirectory));
    builder.Services.AddSingleton(sp => new LiteDBMessageStoreService(
        sp.GetRequiredService<LiteDatabaseFactory>().Open(paths.ChatMessageDatabase)));
    builder.Services.AddSingleton<IMessageStoreMigrationService, MessageStoreMigrationService>();

    builder.Services.AddSingleton(sp => new LocalFileStorageKVData(
        sp.GetRequiredService<ILogger<LocalFileStorageKVData>>(),
        paths.LocalKvDataDirectory));
    builder.Services.AddSingleton(sp => new LiteDBKVData(
        sp.GetRequiredService<LiteDatabaseFactory>().Open(paths.KvDataDatabase),
        sp.GetRequiredService<ILogger<LiteDBKVData>>()));
    builder.Services.AddSingleton<IKVDataMigrationService, KVDataMigrationService>();

    builder.Services.AddSingleton(sp => new LiteDBDebugOutputService(
        sp.GetRequiredService<LiteDatabaseFactory>().Open(paths.DebugOutputDatabase)));
    builder.Services.AddSingleton(sp => new FileSystemDebugOutputService(
        sp.GetRequiredService<ILogger<FileSystemDebugOutputService>>(),
        paths.LocalDebugOutputDirectory));
    builder.Services.AddSingleton<IDebugOutputMigrationService, DebugOutputMigrationService>();

    switch (settings.KVData.GetStorageType())
    {
        case KVStorageType.LiteDB:
            Console.WriteLine($"[Storage] KVData → LiteDB ({paths.KvDataDatabase})");
            builder.Services.AddSingleton<IKVDataService>(sp => sp.GetRequiredService<LiteDBKVData>());
            break;
        case KVStorageType.LocalFileStorage:
            Console.WriteLine($"[Storage] KVData → LocalFileStorage ({paths.LocalKvDataDirectory})");
            builder.Services.AddSingleton<IKVDataService>(sp => sp.GetRequiredService<LocalFileStorageKVData>());
            break;
        default:
            throw new InvalidOperationException($"未实现的 KV 存储类型：{settings.KVData.StorageType}");
    }

    switch (settings.ChatMessage.GetStorageType())
    {
        case KVStorageType.LiteDB:
            Console.WriteLine($"[Storage] ChatMessage → LiteDB ({paths.ChatMessageDatabase})");
            builder.Services.AddSingleton<IMessageStoreService>(sp => sp.GetRequiredService<LiteDBMessageStoreService>());
            break;
        case KVStorageType.LocalFileStorage:
            Console.WriteLine($"[Storage] ChatMessage → LocalFileStorage ({paths.LocalChatMessageDirectory})");
            builder.Services.AddSingleton<IMessageStoreService>(sp => sp.GetRequiredService<FileMessageStoreService>());
            break;
        default:
            throw new InvalidOperationException($"未实现的消息存储类型：{settings.ChatMessage.StorageType}");
    }

    switch (settings.DebugOutput.GetStorageType())
    {
        case KVStorageType.LiteDB:
            Console.WriteLine($"[Storage] DebugOutput → LiteDB ({paths.DebugOutputDatabase})");
            builder.Services.AddSingleton<IDebugOutputService>(sp => sp.GetRequiredService<LiteDBDebugOutputService>());
            break;
        case KVStorageType.LocalFileStorage:
            Console.WriteLine($"[Storage] DebugOutput → LocalFileStorage ({paths.LocalDebugOutputDirectory})");
            builder.Services.AddSingleton<IDebugOutputService>(sp => sp.GetRequiredService<FileSystemDebugOutputService>());
            break;
        default:
            throw new InvalidOperationException($"未实现的调试输出存储类型：{settings.DebugOutput.StorageType}");
    }
}

// 按仓库执行存储后端迁移
static void ExecuteStorageAutoMigration(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var settings = scope.ServiceProvider.GetRequiredService<StorageSettings>();
    var marker = scope.ServiceProvider.GetRequiredService<IStorageMigrationMarker>();
    var services = scope.ServiceProvider;

    MigrateStore(
        StorageStores.KvData,
        settings.KVData,
        marker,
        clearSource => services.GetRequiredService<IKVDataMigrationService>().MigrateToLiteDB(clearSource),
        clearSource => services.GetRequiredService<IKVDataMigrationService>().MigrateToLocalFileStorage(clearSource));

    MigrateStore(
        StorageStores.ChatMessage,
        settings.ChatMessage,
        marker,
        clearSource => services.GetRequiredService<IMessageStoreMigrationService>().MigrateFileToLite(clearSource),
        clearSource => services.GetRequiredService<IMessageStoreMigrationService>().MigrateLiteToFile(clearSource));

    MigrateStore(
        StorageStores.DebugOutput,
        settings.DebugOutput,
        marker,
        clearSource => services.GetRequiredService<IDebugOutputMigrationService>().MigrateFileToLite(clearSource),
        clearSource => services.GetRequiredService<IDebugOutputMigrationService>().MigrateLiteToFile(clearSource));
}

static void MigrateStore(
    string store,
    StoreStorageConfig config,
    IStorageMigrationMarker marker,
    Func<bool, int> migrateToLite,
    Func<bool, int> migrateToFile)
{
    if (!config.AutoMigrateOnStartup)
        return;

    var migrateFrom = config.GetAutoMigrateFromType();
    if (migrateFrom is null)
        return;

    var targetType = config.GetStorageType();

    if (migrateFrom == targetType)
    {
        Console.WriteLine($"[Storage] {store}: AutoMigrateFrom 与目标存储相同，跳过迁移。");
        return;
    }

    bool alreadyMigrated = marker.IsMigrationCompleted(store, migrateFrom.Value, targetType);
    if (alreadyMigrated && !config.ForceMigration)
    {
        Console.WriteLine($"[Storage] {store}: 已从 {migrateFrom} 迁移到 {targetType}，跳过。");
        return;
    }

    if (alreadyMigrated && config.ForceMigration)
    {
        Console.WriteLine($"⚠️  WARNING: {store} 强制迁移已启用！");
        Console.WriteLine($"This will re-migrate all {store} data from {migrateFrom} to {targetType}.");
        Console.WriteLine("Existing data in target storage may be duplicated.");
        Console.Write("Do you want to continue? (yes/no): ");

        string? response = Console.ReadLine();
        if (!string.Equals(response, "yes", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[Storage] {store}: 强制迁移已取消。");
            return;
        }
    }

    int migratedCount;
    try
    {
        migratedCount = migrateFrom == KVStorageType.LocalFileStorage && targetType == KVStorageType.LiteDB
            ? migrateToLite(config.ClearSourceAfterMigration)
            : migrateToFile(config.ClearSourceAfterMigration);

        marker.MarkMigrationCompleted(store, migrateFrom.Value, targetType, migratedCount, config.ClearSourceAfterMigration);
    }
    catch (Exception ex)
    {
        // 迁移失败属于数据完整性问题：打印上下文后中止启动，不写迁移标记。
        Console.Error.WriteLine($"[Storage] {store} 迁移失败（{migrateFrom} → {targetType}）：{ex}");
        throw;
    }

    Console.WriteLine($"[Storage] {store}: 迁移完成，{migrateFrom} → {targetType}，{migratedCount} 条。");
}
