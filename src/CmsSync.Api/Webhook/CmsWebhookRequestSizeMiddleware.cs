using System.Buffers;
using System.Security.Cryptography;
using CmsSync.Application.EventIngestion;
using Microsoft.AspNetCore.Http;

namespace CmsSync.Api.Webhook;

public sealed class CmsWebhookRequestSizeMiddleware
{
    private const int ReadBufferSize = 64 * 1024;

    private readonly RequestDelegate _next;
    private readonly int _maximumRequestSizeBytes;

    public CmsWebhookRequestSizeMiddleware(
        RequestDelegate next,
        CmsEventIngestionLimits limits)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        ArgumentNullException.ThrowIfNull(limits);
        _maximumRequestSizeBytes = limits.MaximumRequestSizeBytes;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsCmsWebhookRequest(context.Request))
        {
            await _next(context);
            return;
        }

        if (context.Request.ContentLength > _maximumRequestSizeBytes)
        {
            await WriteRequestTooLargeAsync(context);
            return;
        }

        byte[]? requestBody;

        try
        {
            requestBody = await ReadBoundedBodyAsync(
                context.Request.Body,
                context.RequestAborted);
        }
        catch (BadHttpRequestException exception)
            when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            await WriteRequestTooLargeAsync(context);
            return;
        }

        if (requestBody is null)
        {
            await WriteRequestTooLargeAsync(context);
            return;
        }

        context.Features.Set(new CmsWebhookRequestBody(requestBody));
        await _next(context);
    }

    private static bool IsCmsWebhookRequest(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method))
        {
            return false;
        }

        var path = request.Path.Value;
        return string.Equals(path, CmsEventsEndpoint.Route, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(path, CmsEventsEndpoint.Route + "/", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<byte[]?> ReadBoundedBodyAsync(
        Stream body,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);

        try
        {
            using var bufferedBody = new MemoryStream();

            while (true)
            {
                var remainingWithSentinel = _maximumRequestSizeBytes + 1 - bufferedBody.Length;

                if (remainingWithSentinel <= 0)
                {
                    return null;
                }

                var requestedBytes = (int)Math.Min(buffer.Length, remainingWithSentinel);
                var bytesRead = await body.ReadAsync(
                    buffer.AsMemory(0, requestedBytes),
                    cancellationToken);

                if (bytesRead == 0)
                {
                    return bufferedBody.ToArray();
                }

                if (bufferedBody.Length + bytesRead > _maximumRequestSizeBytes)
                {
                    return null;
                }

                bufferedBody.Write(buffer, 0, bytesRead);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static Task WriteRequestTooLargeAsync(HttpContext context)
    {
        return CmsWebhookProblemResponse.WriteAsync(
            context,
            StatusCodes.Status413PayloadTooLarge,
            "CMS event request is too large",
            "The request body exceeds the 16 MiB limit.",
            CmsEventParsingCodes.RequestTooLarge);
    }
}
