// Copyright (c) Microsoft. All rights reserved.

using Azure.Core;
using Copilot.Service.Extensions;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Shared.Services;

namespace MinimalApi.Services;
#pragma warning disable SKEXP0011 // Mark members as static
#pragma warning disable SKEXP0001 // Mark members as static
public class RagOchestratorService(ISearchService searchService,
                                   OpenAIClient client,
                                   IConfiguration configuration,
                                   IComputerVisionService? visionService = null,
                                   TokenCredential? tokenCredential = null)
{
    private readonly RagFactory _ragFactory = configuration["UseAOAI"] == "false"
        ? RagFactory.FromOpenAi(configuration, client)
        : RagFactory.FromAzureOpenAi(configuration, tokenCredential);
    //
    public async Task<ChatAppResponse> ReplyAsync(
        ChatMessage[] history,
        RequestOverrides? overrides,
        CancellationToken ct = default)
    {
        var context = ResponseContext.Empty();
        #region Generate Answer
        // R - Retrieval - Retrieve grounding data based on the initial user-entered prompt.
        var retrievedData = await RetrieveDataAsync(history, overrides, ct);
        // A - Augmented - Augment the prompt with grounding data.
        var augmentedQuery = await AugmentPromptAsync(history, retrievedData, ct);

        // G - Generation - Use a language model to generate a grounded response.
        var executingSetting = CreateExecutionSettings(overrides);
        var (Answer, Thoughts) = await _ragFactory.GenerateAnswerAsync(augmentedQuery, executingSetting, ct);
        #endregion

        // add follow up questions if requested - extra step
        if (overrides?.SuggestFollowupQuestions is true)
        {
            #region Generate Followup questions
            // R - Retrieval
            // The result of the Generate Answer process is the Retrieval step of generating follow up questions process

            // A - Augmented  
            var augmentedFolowUpQuery = ChatQueryBuilder.CreateFollowUpQuestionsRequest(Answer);
            // G - Generation  
            var (FollowUpsAnswer, FollowUps) = await _ragFactory.GenerateFollowupAsync(augmentedFolowUpQuery, executingSetting, ct);
            Answer += FollowUpsAnswer;
            context = context with
            {
                FollowupQuestions = FollowUps,
            };
            #endregion
        }

        // Your choices, produce response in your format.
        var responseMessage = new ResponseMessage("assistant", Answer);
        context = context with
        {
            DataPointsContent = retrievedData.Docs,
            DataPointsImages = retrievedData.Images,
            Thoughts = [new Thoughts("Thoughts", Thoughts)]
        };

        var choice = new ResponseChoice(
            Index: 0,
            Message: responseMessage,
            Context: context,
            CitationBaseUrl: configuration.ToCitationBaseUrl());

        return new ChatAppResponse([choice]);
    }

    #region Privates

    private async Task<ChatMessageContentItemCollection> ReadImageContentsAsync(string prompt, SupportingImageRecord[] images, CancellationToken ct = default)
    {
        var tokenRequestContext = new TokenRequestContext(["https://storage.azure.com/.default"]);
        var sasToken = await tokenCredential!.GetTokenAsync(tokenRequestContext, ct);
        var sasTokenString = sasToken.Token;
        var imageUrls = images.Select(x => $"{x.Url}?{sasTokenString}").ToArray();
        var collection = new ChatMessageContentItemCollection
        {
            new TextContent(prompt)
        };
        foreach (var imageUrl in imageUrls)
        {
            collection.Add(new ImageContent(new Uri(imageUrl)));
        }

        return collection;
    }

    private static OpenAIPromptExecutionSettings CreateExecutionSettings(RequestOverrides? overrides)
    {
        return new OpenAIPromptExecutionSettings
        {
            MaxTokens = 1024,
            Temperature = overrides?.Temperature ?? 0.7,
            StopSequences = [],
        };
    }

    #endregion

    #region Flows
    /// <summary>
    /// Retrieve grounding data based on the initial user-entered prompt.
    /// </summary>
    /// <param name="history">All history</param>
    /// <param name="overrides"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    private async Task<(SupportingContentRecord[] Docs, SupportingImageRecord[]? Images)> RetrieveDataAsync(ChatMessage[] history,
                                                                                                            RequestOverrides? overrides,
                                                                                                            CancellationToken ct = default)
    {
        // User prompt
        var question = history.LastOrDefault(m => m.IsUser)?.Content is { } userQuestion
          ? userQuestion
          : throw new InvalidOperationException("Use question is null");

        // step 1
        // get query based on User prompt + retrieval mode
        var (Query, Vector) = await _ragFactory.GenerateQueryAsync(question, overrides?.RetrievalMode, ct);
        //
        // step 2
        // use query to search related docs/images(if visiion service is enabled)
        var docs = await searchService.QueryDocumentsAsync(Query, Vector, overrides, ct);
        SupportingImageRecord[]? images = default;
        if (visionService is not null && Query is not null)
        {
            var res = await visionService.VectorizeTextAsync(Query, ct);
            images = await searchService.QueryImagesAsync(Query, res!.vector, overrides, ct);
        }

        return (docs, images);
    }

    private async Task<ChatHistory> AugmentPromptAsync(ChatMessage[] history,
                                                       (SupportingContentRecord[] Docs, SupportingImageRecord[]? Images) relatedData,
                                                       CancellationToken ct = default)
    {
        var (Docs, Images) = relatedData;
        string docsContent = Docs.Length == 0
           ? "no source available."
           : string.Join("\r", Docs.Select(x => $"{x.Title}:{x.Content}"));
        // step 3
        // put together related docs and conversation history to generate answer
        var answerQuery = ChatQueryBuilder.CreateAnswerRequest(history);
        var hasImages = Images != null;
        if (hasImages)
        {
            var prompt = ChatQueryBuilder.CreateImagePrompt(docsContent);
            var collection = await ReadImageContentsAsync(prompt, Images!, ct);
            answerQuery.AddUserMessage(collection);
        }
        else
        {
            var prompt = ChatQueryBuilder.CreateDocPrompt(docsContent);
            answerQuery.AddUserMessage(prompt);
        }

        return answerQuery;
    }

    #endregion
}
