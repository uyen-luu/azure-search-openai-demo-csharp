// Copyright (c) Microsoft. All rights reserved.

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
namespace Shared.Services;
public class AzureComputerVisionService(HttpClient client, string endPoint, TokenCredential tokenCredential) : IComputerVisionService
{
    public int Dimension => 1024;

    // add virtual keyword to make it mockable
    public async Task<ImageEmbeddingResponse> VectorizeImageAsync(string imagePathOrUrl, CancellationToken ct = default)
    {
        var api = $"{endPoint}/computervision/retrieval:vectorizeImage?api-version=2023-04-01-preview&modelVersion=latest";
        return await VectorizeAsync(api, async request =>
        {
            if (File.Exists(imagePathOrUrl))
            {
                // set body
                var bytes = await File.ReadAllBytesAsync(imagePathOrUrl, ct);
                request.Content = new ByteArrayContent(bytes);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/*");
            }
            else
            {
                // set content type to application/json
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                // set body
                var body = new { url = imagePathOrUrl };
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            }
        }, ct);
    }

    public virtual async Task<ImageEmbeddingResponse> VectorizeTextAsync(string text, CancellationToken ct = default)
    {
        var api = $"{endPoint}/computervision/retrieval:vectorizeText?api-version=2023-04-01-preview&modelVersion=latest";
        return await VectorizeAsync(api, async request =>
        {
            // set content type to application/json
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json")); // set body
            var body = new { text };
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }, ct);
    }

    private async Task<ImageEmbeddingResponse> VectorizeAsync(string api, Func<HttpRequestMessage, Task> configure, CancellationToken ct = default)
    {
        var token = await tokenCredential.GetTokenAsync(new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]), ct); ;
        // first try to read as local file
        using var request = new HttpRequestMessage(HttpMethod.Post, api);
        // set authorization header
        request.Headers.Add("Authorization", $"Bearer {token.Token}");
        //
        await configure(request);
        // send request
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        // read response
        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<ImageEmbeddingResponse>(json);

        return result ?? throw new InvalidOperationException("Failed to deserialize response");
    }
}
