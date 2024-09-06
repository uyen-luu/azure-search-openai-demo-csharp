// Copyright (c) Microsoft. All rights reserved.

using Microsoft.SemanticKernel.ChatCompletion;

namespace MinimalApi.Extensions;

internal class ChatQueryBuilder
{
    public static ChatHistory CreateQueryRequest(string message)
    {
        var chatHistory = new ChatHistory("""
            You are a helpful AI assistant, generate search query for followup question.
            Make your respond simple and precise. Return the query only, do not return any other text.
            e.g.
            Grass straws Strawlific store.
            The Bamboo G.O.B product and its information.

            """);

        chatHistory.AddUserMessage(message);
        return chatHistory;
    }

    public static ChatHistory CreateAnswerRequest(ChatMessage[] messages)
    {
        var chatHistory = new ChatHistory("You are a system assistant who helps the company employees with their questions. Be brief in your answers");

        foreach (var message in messages)
        {
            if (message.IsUser)
            {
                chatHistory.AddUserMessage(message.Content);
            }
            else
            {
                chatHistory.AddAssistantMessage(message.Content);
            }
        }
        return chatHistory;
    }

    public static string CreateImagePrompt(string docsContent)
    {
        return @$"## Source ##
{docsContent}
## End ##

Answer question based on available source and images.
Your answer needs to be a json object with answer and thoughts field.
Don't put your answer between ```json and ```, return the json string directly. e.g {{""answer"": ""I don't know"", ""thoughts"": ""I don't know""}}";
    }


    public static string CreateDocPrompt(string docsContent)
    {
        return @$" ## Source ##
{docsContent}
## End ##

You answer needs to be a json object with the following format.
{{
    ""answer"": // the answer to the question, add a source reference to the end of each sentence. e.g. Apple is a fruit [reference1.pdf][reference2.pdf]. If no source available, put the answer as I don't know.
    ""thoughts"": // brief thoughts on how you came up with the answer, e.g. what sources you used, what you thought about, etc.
}}";

    }

    public static ChatHistory CreateFollowUpQuestionsRequest(string answer)
    {
        var chatHistory = new ChatHistory(@"You are a helpful AI assistant");
        chatHistory.AddUserMessage($@"Generate three follow-up question based on the answer you just generated.
# Answer
{answer}

# Format of the response
Return the follow-up question as a json string list. Don't put your answer between ```json and ```, return the json string directly.
e.g.
[
    ""What is the deductible?"",
    ""What is the co-pay?"",
    ""What is the out-of-pocket maximum?""
]");

        return chatHistory;
    }
}
