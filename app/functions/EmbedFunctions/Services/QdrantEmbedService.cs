﻿// Copyright (c) Microsoft. All rights reserved.


using Shared.Services;

namespace EmbedFunctions.Services;

internal sealed class QdrantEmbedService : IEmbedService
{
    public Task CreateSearchIndexAsync(string searchIndexName, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> EmbedImageBlobAsync(Stream imageStream, string imageUrl, string imageName, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> EmbedPdfBlobAsync(Stream blobStream, string blobName) => throw new NotImplementedException();

    public Task EnsureSearchIndexAsync(string searchIndexName, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}
