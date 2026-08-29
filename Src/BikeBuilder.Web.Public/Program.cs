using BikeBuilder.Web.Public.Components;
using BikeBuilder.Web.Public.Services;
using MudBlazor.Services;

// Azure SDK messaging tracing (the ServiceBusProcessor.ProcessMessage span that continues
// the API's trace into this app) is still behind this experimental switch. Must be set
// before any ServiceBusClient is constructed.
AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(configureTracing: tracing => tracing
    .AddSource("BikeBuilder.Web.Public")              // custom broadcast span in the listener
    .AddSource("Microsoft.AspNetCore.SignalR.Server") // client-invoked hub methods, if any appear
);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddSignalR();

// The "servicebus" connection string is injected by the AppHost (WithReference).
builder.AddAzureServiceBusClient("servicebus");
builder.Services.AddHostedService<ServiceBusListenerBackgroundService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
  app.UseExceptionHandler("/Error", createScopeForErrors: true);
  // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
  app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<NotificationHub>("/hubs/notifications");
app.MapDefaultEndpoints();

await app.RunAsync();
