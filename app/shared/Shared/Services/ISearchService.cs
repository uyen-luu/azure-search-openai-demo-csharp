// Copyright (c) Microsoft. All rights reserved.

using Shared.Models;

namespace Shared.Services;

public interface ISearchService
{
    Task<SupportingContentRecord[]> QueryDocumentsAsync(
               string? query = null,
               float[]? embedding = null,
               RequestOverrides? overrides = null,
               CancellationToken cancellationToken = default);

    Task<SupportingImageRecord[]> QueryImagesAsync(
               string? query = null,
               float[]? embedding = null,
               RequestOverrides? overrides = null,
               CancellationToken cancellationToken = default);
}
