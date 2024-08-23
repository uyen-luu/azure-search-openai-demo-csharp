// Copyright (c) Microsoft. All rights reserved.

using Microsoft.AspNetCore.Diagnostics;

namespace MinimalApi.Extensions;

public class CustomExceptionHandler(ILogger<CustomExceptionHandler> logger) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var exceptionMessage = exception.Message;
        logger.LogError(
            "Error Message: {exceptionMessage}, Time of occurrence {time}",
            exceptionMessage, DateTime.UtcNow);
        // Return false to continue with the default behavior
        // - or - return true to signal that this exception is handled
        return ValueTask.FromResult(false);
    }
}
