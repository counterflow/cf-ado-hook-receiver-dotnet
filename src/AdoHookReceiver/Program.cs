using System.Text.Encodings.Web;
using System.Text.Json;
using AdoHookReceiver;
using AdoHookReceiver.Hooks;
using AdoHookReceiver.Options;
using Microsoft.Extensions.Options;

if (args.Contains(HealthCheckCommand.Argument))
{
    return await HealthCheckCommand.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);

// JSON console logs. Relaxed escaping writes quotes in the raw payload as \" rather than as a
// \u escape sequence, roughly halving its size; the output is a log stream, never HTML.
builder.Logging.AddJsonConsole(options =>
    options.JsonWriterOptions = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

builder.Services
    .AddOptions<AdoHookOptions>()
    .Bind(builder.Configuration.GetSection(AdoHookOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<AdoHookOptions>, AdoHookOptionsValidator>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AllowList>();

// The one seam: later layers replace this registration and change nothing else.
builder.Services.AddSingleton<IHookSink, LogHookSink>();

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapHealthChecks("/health");
app.MapAdoHooks();

await app.RunAsync();
return 0;
