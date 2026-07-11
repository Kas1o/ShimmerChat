using ShimmerChat.Components;
using ShimmerChat.Singletons;
using ShimmerChatLib.Generation;
using ShimmerChatLib;
using ShimmerChatLib.Interface;
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

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// 配置 KV 数据存储
ConfigureKVDataStorage(builder);

// ShimmerChat 2.0 服务
builder.Services.AddSingleton<IToolRegistry, ToolRegistry>();
builder.Services.AddSingleton<IGenerationNodeSerializer, GenerationNodeSerializer>();
builder.Services.AddSingleton<GenerationManagerV2>();
builder.Services.AddSingleton<AgentMigrationService>();
builder.Services.AddSingleton<IPluginLoaderService, PluginLoaderServiceV1>();
builder.Services.AddSingleton<IPluginPanelService, PluginPanelServiceV1>();
builder.Services.AddSingleton<IPopupService, PopupService>();
builder.Services.AddSingleton<IMessageDisplayService, MessageDisplayServiceV1>();
builder.Services.AddSingleton<ICompletionServiceV2, CompletionServiceV2>();
builder.Services.AddScoped<IThemeService, ThemeServiceV2>();
builder.Services.AddSingleton<ILocService, LocService>();

var app = builder.Build();

// 执行自动迁移（如果需要）
ExecuteAutoMigration(app);

// ShimmerChat 2.0: 执行 Agent 数据迁移
ExecuteAgentMigration(app);

// 执行插件初始化
ExecutePluginInitializers(app);

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
var userImagePath = Path.Combine(AppContext.BaseDirectory, "UserUploadImage");
if (!Directory.Exists(userImagePath))
    Directory.CreateDirectory(userImagePath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(userImagePath),
    RequestPath = "/userimages"
});

app.Run();

return;

// ShimmerChat 2.0: 自动迁移 Agent 到 2.0
static void ExecuteAgentMigration(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var migrationService = scope.ServiceProvider.GetRequiredService<AgentMigrationService>();
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

// 配置 KV 数据存储服务
static void ConfigureKVDataStorage(WebApplicationBuilder builder)
{
    var config = builder.Configuration.GetSection("KVDataStorage").Get<KVDataStorageConfig>() ?? new KVDataStorageConfig();
    builder.Services.AddSingleton(config);

    // 创建共享的 LiteDatabase 实例（消息存储和 KV 存储共用）
    string dbPath = Path.Combine(AppContext.BaseDirectory, "LiteDBKVData", "data.db");
    string? dbDir = Path.GetDirectoryName(dbPath);
    if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
    {
        Directory.CreateDirectory(dbDir);
    }
    var sharedDatabase = new LiteDB.LiteDatabase(dbPath);
    builder.Services.AddSingleton(sharedDatabase);

    // 始终注册两种消息存储实现（用于迁移服务）
    builder.Services.AddSingleton<FileMessageStoreService>();
    builder.Services.AddSingleton<LiteDBMessageStoreService>();
    builder.Services.AddSingleton<MessageStoreMigrationService>();

    // 注册迁移标记管理器
    builder.Services.AddSingleton<KVDataMigrationMarker>();

    // 始终注册两种 KV 存储实现（用于迁移服务）
    builder.Services.AddSingleton<LocalFileStorageKVData>();
    builder.Services.AddSingleton<LiteDBKVData>();
    builder.Services.AddSingleton<KVDataMigrationService>();

    // 根据配置注册 IKVDataService 和 IMessageStoreService 的实现
    switch (config.GetStorageType())
    {
        case KVStorageType.LiteDB:
            Console.WriteLine("Using LiteDB for KV data storage");
            builder.Services.AddSingleton<IKVDataService>(sp => sp.GetRequiredService<LiteDBKVData>());
            builder.Services.AddSingleton<IMessageStoreService>(sp => sp.GetRequiredService<LiteDBMessageStoreService>());
            break;
        case KVStorageType.LocalFileStorage:
        default:
            Console.WriteLine("Using LocalFileStorage for KV data storage");
            builder.Services.AddSingleton<IKVDataService>(sp => sp.GetRequiredService<LocalFileStorageKVData>());
            builder.Services.AddSingleton<IMessageStoreService>(sp => sp.GetRequiredService<FileMessageStoreService>());
            break;
    }
}

// 执行自动迁移
static void ExecuteAutoMigration(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var config = scope.ServiceProvider.GetRequiredService<KVDataStorageConfig>();

    if (!config.AutoMigrateOnStartup || string.IsNullOrEmpty(config.AutoMigrateFrom))
        return;

    var migrationService = scope.ServiceProvider.GetRequiredService<KVDataMigrationService>();
    var messageMigrationService = scope.ServiceProvider.GetRequiredService<MessageStoreMigrationService>();
    var marker = scope.ServiceProvider.GetRequiredService<KVDataMigrationMarker>();
    var migrateFrom = config.GetAutoMigrateFromType();

    if (migrateFrom == null)
    {
        Console.WriteLine($"Warning: Invalid AutoMigrateFrom value: {config.AutoMigrateFrom}");
        return;
    }

    var targetType = config.GetStorageType();

    if (migrateFrom == targetType)
    {
        Console.WriteLine("Warning: AutoMigrateFrom is the same as target storage type, skipping migration");
        return;
    }

    // 检查是否已经完成过迁移
    bool alreadyMigrated = marker.IsMigrationCompleted(migrateFrom.Value, targetType);
    if (alreadyMigrated && !config.ForceMigration)
    {
        Console.WriteLine($"Skipping migration: data was already migrated from {migrateFrom} to {targetType}.");
        Console.WriteLine("To force re-migration, set 'ForceMigration: true' in configuration.");
        return;
    }

    // 强制迁移需要 CLI 确认
    if (alreadyMigrated && config.ForceMigration)
    {
        Console.WriteLine("⚠️  WARNING: Force migration is enabled!");
        Console.WriteLine($"This will re-migrate all data from {migrateFrom} to {targetType}.");
        Console.WriteLine("Existing data in target storage may be duplicated.");
        Console.Write("Do you want to continue? (yes/no): ");

        string? response = Console.ReadLine();
        if (!string.Equals(response, "yes", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Force migration cancelled by user.");
            return;
        }

        Console.WriteLine("Force migration confirmed. Proceeding...");
    }

    try
    {
        int migratedCount = 0;

        if (migrateFrom == KVStorageType.LocalFileStorage && targetType == KVStorageType.LiteDB)
        {
            Console.WriteLine("Auto-migrating data from LocalFileStorage to LiteDB...");
            migratedCount = migrationService.MigrateToLiteDB(config.ClearSourceAfterMigration);

            Console.WriteLine("Auto-migrating messages from File to LiteDB...");
            int msgCount = messageMigrationService.MigrateFileToLite();
            Console.WriteLine($"Migrated {msgCount} messages.");
            migratedCount += msgCount;
        }
        else if (migrateFrom == KVStorageType.LiteDB && targetType == KVStorageType.LocalFileStorage)
        {
            Console.WriteLine("Auto-migrating data from LiteDB to LocalFileStorage...");
            migratedCount = migrationService.MigrateToLocalFileStorage(config.ClearSourceAfterMigration);

            Console.WriteLine("Auto-migrating messages from LiteDB to File...");
            int msgCount = messageMigrationService.MigrateLiteToFile();
            Console.WriteLine($"Migrated {msgCount} messages.");
            migratedCount += msgCount;
        }

        // 记录迁移标记
        marker.MarkMigrationCompleted(migrateFrom.Value, targetType, migratedCount, config.ClearSourceAfterMigration);

        Console.WriteLine($"Auto-migration completed. Migrated {migratedCount} entries.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error during auto-migration: {ex.Message}");
    }
}
