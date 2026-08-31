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
//
// Scoped, not singleton: in this host a scope is one user's SignalR circuit. As singletons, every
// visitor shared one dataset and one tail — whoever loaded a folder showed their logs to everybody
// else on the server, the "include DEBUG" checkbox was global, and either user could stop the
// other's watch. Scoped still outlives page navigation, which is all the pages rely on.
//
// NOTE: this host has no authentication, and both the file browser and the watcher will read any
// path the machine can reach. Bind it to localhost, or put it behind auth before exposing it.
builder.Services.AddScoped<LogStore>();
builder.Services.AddScoped<LogWatcher>();
// Per-circuit UI state so filters persist across page navigation.
builder.Services.AddScoped<SessionState>();
// Recently used paths, persisted in the browser's localStorage.
builder.Services.AddScoped<PathHistory>();

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
