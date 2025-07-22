// Copyright (c) Microsoft. All rights reserved.

using Azure.Core;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Embeddings;
using static System.ArgumentException;

namespace Copilot.Service.Extensions;
#pragma warning disable SKEXP0011 // Mark members as static
#pragma warning disable SKEXP0001 // Mark members as static
#pragma warning disable SKEXP0010 // Mark members as static
internal class RagFactory
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chat;
    private readonly ITextEmbeddingGenerationService _embedding;

    #region Constructors
    private RagFactory(Kernel kernel)
    {
        _kernel = kernel;
        _chat = _kernel.GetRequiredService<IChatCompletionService>();
        _embedding = _kernel.GetRequiredService<ITextEmbeddingGenerationService>();
    }

    public static RagFactory FromOpenAi(IConfiguration configuration, OpenAIClient client)
    {
        var kernelBuilder = Kernel.CreateBuilder();
        var (languageModel, embeddingModel) = (configuration["OpenAiChatGptDeployment"], configuration["OpenAiEmbeddingDeployment"]);
        ThrowIfNullOrWhiteSpace(languageModel);
        ThrowIfNullOrWhiteSpace(embeddingModel);
        //
        kernelBuilder = kernelBuilder.AddOpenAIChatCompletion(languageModel, client);
        kernelBuilder = kernelBuilder.AddOpenAITextEmbeddingGeneration(embeddingModel, client);
        var kernel = kernelBuilder.Build();
        return new(kernel);
    }

    public static RagFactory FromAzureOpenAi(IConfiguration configuration, TokenCredential? tokenCredential)
    {
        var kernelBuilder = Kernel.CreateBuilder();
        var (languageModel, embeddingModel, endpoint)
                = (configuration["AzureOpenAiChatGptDeployment"], configuration["AzureOpenAiEmbeddingDeployment"], configuration["AzureOpenAiServiceEndpoint"]);
        ThrowIfNullOrWhiteSpace(languageModel);
        ThrowIfNullOrWhiteSpace(embeddingModel);
        ThrowIfNullOrWhiteSpace(endpoint);
        //
        tokenCredential ??= new DefaultAzureCredential();
        kernelBuilder = kernelBuilder.AddAzureOpenAITextEmbeddingGeneration(embeddingModel, endpoint, tokenCredential);
        kernelBuilder = kernelBuilder.AddAzureOpenAIChatCompletion(languageModel, endpoint, tokenCredential);
        var kernel = kernelBuilder.Build();
        return new(kernel);
    }
    #endregion

    public async Task<(string? Query, float[]? Vector)> GenerateQueryAsync(string question, RetrievalMode? retrievalMode, CancellationToken ct = default)
    {
        float[]? embeddings = null;
        if (retrievalMode != RetrievalMode.Text)
        {
            embeddings = (await _embedding.GenerateEmbeddingAsync(question, cancellationToken: ct)).ToArray();
        }

        string? query = null;
        if (retrievalMode != RetrievalMode.Vector)
        {
            var chatHistory = ChatQueryBuilder.CreateQueryRequest(question);
            var result = await _chat.GetChatMessageContentAsync(chatHistory, cancellationToken: ct);
            query = result.Content ?? throw new InvalidOperationException("Failed to get search query");
        }

        return (query ?? question, embeddings);
    }

    public async Task<(string Answer, string Thoughts)> GenerateAnswerAsync(ChatHistory history,
                                                                            OpenAIPromptExecutionSettings promptSetting,
                                                                            CancellationToken ct = default)
    {
        var dataObject = await GenerateChatMessageAsync(history, promptSetting, ct);
        var ans = dataObject.GetProperty("answer").GetString() ?? throw new InvalidOperationException("Failed to get answer");
        var thoughts = dataObject.GetProperty("thoughts").GetString() ?? throw new InvalidOperationException("Failed to get thoughts");

        return (ans, thoughts);
    }

    public async Task<(string Answer, string[] FollowUps)> GenerateFollowupAsync(ChatHistory history,
                                                                                 OpenAIPromptExecutionSettings promptSetting,
                                                                                 CancellationToken ct = default)
    {
        var dataObject = await GenerateChatMessageAsync(history, promptSetting, ct);
        var followUps = dataObject.EnumerateArray().Select(x => x.GetString()!).ToList();
        var ans = string.Empty;
        followUps.ForEach(item => ans += $" <<{item}>> ");
        //
        return (ans, followUps.ToArray());
    }

    /// <summary>
    /// Use a language model to generate a grounded response.
    /// </summary>
    /// <param name="history"></param>
    /// <param name="promptSetting"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    private async Task<JsonElement> GenerateChatMessageAsync(ChatHistory history,
                                                             OpenAIPromptExecutionSettings promptSetting,
                                                             CancellationToken ct = default)
    {
        var answer = await _chat.GetChatMessageContentAsync(history,
                                                            promptSetting,
                                                            cancellationToken: ct);

        var answerJson = answer.Content ?? throw new InvalidOperationException("Failed to get search query");
        return JsonSerializer.Deserialize<JsonElement>(answerJson);
    }
}
