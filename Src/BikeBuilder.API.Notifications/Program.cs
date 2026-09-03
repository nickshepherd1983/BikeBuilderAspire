using System.Net.Http.Headers;
using System.Text;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Azure SDK messaging tracing (the ServiceBusProcessor spans that continue each publisher's
// trace into this app) is still behind this experimental switch. Must be set before the
// Service Bus trigger builds its client.
AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

// Same convention as the ratings worker: the Functions host already emits the invocation
// span, so adding AspNetCore instrumentation here would double-report every request.
builder.AddServiceDefaults(includeAspNetCoreTracing: false);
builder.Services.AddOpenTelemetry().UseFunctionsWorkerDefaults();

// Which provider delivers the order receipts - see EmailOptions for the selection rules.
var email = EmailOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(email);
if (email.Mailjet is not null)
{
#pragma warning disable S1075 // Mailjet's fixed API origin.
  builder.Services.AddHttpClient<IEmailSender, MailjetEmailSender>(client =>
  {
    client.BaseAddress = new Uri("https://api.mailjet.com/v3.1/");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
        Convert.ToBase64String(Encoding.ASCII.GetBytes($"{email.Mailjet.ApiKey}:{email.Mailjet.SecretKey}")));
  });
#pragma warning restore S1075
}
else if (email.Smtp is not null)
{
  builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
}
else
{
  builder.Services.AddSingleton<IEmailSender, NullEmailSender>();
}

var app = builder.Build();

await app.RunAsync();
