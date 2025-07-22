// Copyright (c) Microsoft. All rights reserved.

using Shared.Extensions;

namespace EmbedFunctions.Services;

public sealed class EmbeddingAggregateService(
    EmbedServiceFactory embedServiceFactory,
    BlobServiceClient blobServiceClient,
    BlobContainerClient corpusClient,
    ILogger<EmbeddingAggregateService> logger)
{
    internal async Task EmbedBlobAsync(Stream blobStream, string blobName)
    {
        var embeddingType = GetEmbeddingType();
        var contentContainer = blobServiceClient.GetBlobContainerClient("content");
        var blobClient = contentContainer.GetBlobClient(blobName);
        var uri = blobClient.Uri.AbsoluteUri ?? throw new InvalidOperationException("Blob URI is null.");
        var props = await blobClient.GetPropertiesAsync().ConfigureAwait(false);
        var metadata = props.Value.Metadata;
        var currentStatus = BlobExtension.GetMetadataEnumOrDefault<DocumentProcessingStatus>(
                   metadata, nameof(DocumentProcessingStatus), DocumentProcessingStatus.Default);

        if (currentStatus == DocumentProcessingStatus.Succeeded)
        {
            return;
        }
        var processingStatus = DocumentProcessingStatus.Processing;
        string? errorMessage = null;
        try
        {
            var embedService = embedServiceFactory.GetEmbedService(embeddingType);

            if (Path.GetExtension(blobName) is ".png" or ".jpg" or ".jpeg" or ".gif")
            {
                logger.LogInformation("Embedding image: {Name}", blobName);

                var result = await embedService.EmbedImageBlobAsync(blobStream, uri, blobName);
                processingStatus = result switch
                {
                    true => DocumentProcessingStatus.Succeeded,
                    _ => DocumentProcessingStatus.Failed
                };
            }

            else if (Path.GetExtension(blobName) is ".pdf")
            {
                logger.LogInformation("Embedding pdf: {Name}", blobName);
                var result = await embedService.EmbedPdfBlobAsync(blobStream, blobName);

                processingStatus = result switch
                {
                    true => DocumentProcessingStatus.Succeeded,
                    _ => DocumentProcessingStatus.Failed
                };
            }
            else
            {
                throw new NotSupportedException("Unsupported file type.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to embed: {Name}, error: {Message}", blobName, ex.Message);
            processingStatus = DocumentProcessingStatus.Failed;
            errorMessage = ex.Message;
            throw;
        }
        finally
        {
            if (currentStatus != processingStatus)
            {
                metadata = new Dictionary<string, string>
                {
                    [nameof(DocumentProcessingStatus)] = processingStatus.ToString(),
                    [nameof(EmbeddingType)] = embeddingType.ToString()
                };
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    metadata.Add("ErrorMessage", errorMessage);
                }
                await blobClient.SetMetadataAsync(metadata);
            }
        }
    }

    private static EmbeddingType GetEmbeddingType() => Environment.GetEnvironmentVariable("EMBEDDING_TYPE") is string type &&
            Enum.TryParse<EmbeddingType>(type, out EmbeddingType embeddingType)
            ? embeddingType
            : EmbeddingType.AzureSearch;
}
