using ShimmerChat.Components;
using ShimmerChat.Singletons;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;
using System.Globalization;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddLocalization(option => option.ResourcesPath = "Resources");

builder.Services.AddSingleton<IUserData, LocalFileStorageUserData>();
builder.Services.AddSingleton<ICompletionService, CompletionServiceV1>();
builder.Services.AddSingleton<IToolService, ToolServiceV1>();
builder.Services.AddSingleton<IPopupService, PopupService>();

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

app.Run();
