// Local stand-in for the Azure API Management gateway. The AppHost runs this project on the
// well-known gateway port whenever no APIM connection is configured (no Apim:* user secrets -
// notably CI, which has no Azure credentials), so the browser-facing base addresses can point
// at the gateway origin unconditionally. The route table in appsettings.json mirrors the APIM
// API definitions in infra/modules/apim.bicep; keep the two in sync.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// The proxy copies the backend's X-Trace-Id through; this covers the responses the gateway
// itself produces (502/504, /healthz). YARP re-injects traceparent from the gateway's own span,
// so the id is the same one the backend saw.
app.UseTraceIdResponseHeader();

// A literal endpoint outranks the proxy's catch-all in endpoint routing, so this stays local.
app.MapGet("/healthz", () => "gateway");
app.MapDefaultEndpoints();

app.MapReverseProxy();

await app.RunAsync();
