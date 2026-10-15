using System.Diagnostics;
using AdoHookReceiver.Options;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace AdoHookReceiver.Hooks;

/// <summary><c>POST /ado/hooks</c>: auth filter, then body, envelope, allow-list, sink.</summary>
public static class HooksEndpoint
{
    private const string LoggerCategory = "AdoHookReceiver.Hooks.HooksEndpoint";

    public static RouteHandlerBuilder MapAdoHooks(this IEndpointRouteBuilder endpoints) =>
        endpoints
            .MapPost("/ado/hooks", HandleAsync)
            .AddEndpointFilter<BasicAuthFilter>();

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        AllowList allowList,
        IHookSink sink,
        TimeProvider time,
        IOptions<AdoHookOptions> options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(LoggerCategory);

        var body = await ReadBodyAsync(http, options.Value.BodyLimitBytes, cancellationToken);
        if (body is null)
        {
            HookLog.Rejected(logger, $"Body is over the {options.Value.BodyLimitBytes} byte limit.");
            return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        if (!Envelope.TryParse(body, out var envelope, out var error))
        {
            HookLog.Rejected(logger, error!);
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: error);
        }

        if (allowList.Refuse(envelope!) is { } refusedBy)
        {
            HookLog.Forbidden(logger, envelope!.EventType, refusedBy, envelope.AccountId, envelope.OrgUrl, envelope.ProjectId, envelope.SubscriptionId);
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, detail: $"Not on the {refusedBy} allow-list.");
        }

        var hook = new ReceivedHook(
            ReceiptId: Guid.CreateVersion7().ToString(),
            ReceivedAt: time.GetUtcNow(),
            Source: "ado",
            EventId: envelope!.EventId,
            EventType: envelope.EventType,
            ResourceVersion: envelope.ResourceVersion,
            PublisherId: envelope.PublisherId,
            SubscriptionId: envelope.SubscriptionId,
            NotificationId: envelope.NotificationId,
            CreatedDate: envelope.CreatedDate,
            OrgUrl: envelope.OrgUrl,
            TraceParent: Activity.Current?.Id,
            Raw: envelope.Raw);

        try
        {
            await sink.HandleAsync(hook, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            HookLog.SinkFailed(logger, ex, hook.EventType, hook.EventId, hook.ReceiptId);
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Accepted(value: new { receiptId = hook.ReceiptId });
    }

    /// <summary>Reads the whole body, or returns null when it is over the limit.</summary>
    private static async Task<byte[]?> ReadBodyAsync(HttpContext http, long limit, CancellationToken cancellationToken)
    {
        if (http.Request.ContentLength > limit)
        {
            return null;
        }

        // Make the server's own limit match ours, so a larger configured limit isn't capped by
        // Kestrel's default.
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
        {
            sizeFeature.MaxRequestBodySize = limit;
        }

        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await http.Request.Body.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > limit)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return null;
        }
    }
}
