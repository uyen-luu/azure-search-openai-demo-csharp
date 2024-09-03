// Copyright (c) Microsoft. All rights reserved.

using System.Net;
using Microsoft.AspNetCore.Diagnostics;

namespace MinimalApi.Extensions;

public class CustomExceptionHandler(ILogger<CustomExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var exceptionMessage = exception.Message;
        logger.LogError(
            "Error Message: {exceptionMessage}, Time of occurrence {time}",
            exceptionMessage, DateTime.UtcNow);
        if (exception is HttpOperationException operationException && operationException.ResponseContent is not null)
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(operationException.ResponseContent);
            var errorMessage = doc.Get("error")?.Get("message")?.GetString();
            httpContext.Response.StatusCode = (int)(operationException.StatusCode ?? HttpStatusCode.InternalServerError);
            var result = new ProblemDetails
            {
                Status = httpContext.Response.StatusCode,
                Type = exception.GetType().Name,
                Title = "An unexpected error occurred",
                Detail = errorMessage ?? operationException.Message,
                Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}"
            };
            // Return false to continue with the default behavior
            // - or - return true to signal that this exception is handled

            await httpContext.Response.WriteAsJsonAsync(result, cancellationToken: cancellationToken);
            return true;
        }


        return false;
    }
}
public static partial class JsonExtensions
{
    public static JsonElement? Get(this JsonElement element, string name) =>
        element.ValueKind != JsonValueKind.Null && element.ValueKind != JsonValueKind.Undefined && element.TryGetProperty(name, out var value)
            ? value : (JsonElement?)null;

    public static JsonElement? Get(this JsonElement element, int index)
    {
        if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }
        // Throw if index < 0
        return index < element.GetArrayLength() ? element[index] : null;
    }
}
