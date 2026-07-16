using LogAnalyzer.Components;
using LogAnalyzer.Services;
using Radzen;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Radzen Blazor component services (dialogs, notifications, tooltips, context menus).
builder.Services.AddRadzenComponents();

// Log-analysis services. LogStore holds the loaded dataset; LogWatcher tails a live file.
builder.Services.AddSingleton<LogStore>();
builder.Services.AddSingleton<LogWatcher>();
// Per-circuit UI state so filters persist across page navigation.
builder.Services.AddScoped<SessionState>();

// Allow big SignalR payloads (large filtered tables pushed to the browser).
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(o =>
{
    o.MaximumReceiveMessageSize = 10 * 1024 * 1024;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(LogAnalyzer.Services.LogStore).Assembly);

app.Run();
