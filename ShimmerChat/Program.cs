using ShimmerChat.Components;
using ShimmerChat.Singletons;
using Microsoft.AspNetCore.Localization;
using System.Globalization;
using ShimmerChatLib.Interface;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddLocalization(option => option.ResourcesPath = "Resources");

builder.Services.AddSingleton<ICompletionService, CompletionServiceV1>();
builder.Services.AddSingleton<ICompletionServiceV2, CompletionServiceV2>();
builder.Services.AddSingleton<IContextBuilderService, ContextBuilderServiceV1>();
builder.Services.AddSingleton<IAIGenerationService, AIGenerationServiceV1>();
builder.Services.AddSingleton<IPluginLoaderService, PluginLoaderServiceV1>();
builder.Services.AddSingleton<IPluginPanelService, PluginPanelServiceV1>();
builder.Services.AddSingleton<IToolService, ToolServiceV1>();
builder.Services.AddSingleton<IPopupService, PopupService>();
builder.Services.AddSingleton<IMessageDisplayService, MessageDisplayServiceV1>();
builder.Services.AddSingleton<IKVDataService, LocalFileStorageKVData>();
builder.Services.AddSingleton<IContextModifierService, ContextModifierServiceV1>();
builder.Services.AddScoped<IThemeService, ThemeServiceV1>();

var app = builder.Build();

var supportedCultures = new[]
{
    new CultureInfo("en-US"),
    new CultureInfo("zh-CN")
};

app.UseRequestLocalization(new RequestLocalizationOptions
{
	DefaultRequestCulture = new RequestCulture("zh-CN"),
	SupportedCultures = supportedCultures,
	SupportedUICultures = supportedCultures
});


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
