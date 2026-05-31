using System.ClientModel;
using OpenAI;
using OpenAI.Chat;

namespace HealthyGuidance.Core.AzureOpenAI;

#pragma warning disable OPENAI001

public class GptVisionClient
{
    private readonly ChatClient _client;

    public GptVisionClient(string endpoint, string apiKey, string deploymentName)
    {
        _client = new ChatClient(
            credential: new ApiKeyCredential(apiKey),
            model: deploymentName,
            options: new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
    }

    public async Task<string> ParseScreenshotAsync(
        byte[] imageBytes,
        string imageMimeType,
        string systemPrompt,
        string parseResultSchemaJson,
        CancellationToken cancellationToken = default)
    {
        var messages = new ChatMessage[]
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(
                ChatMessageContentPart.CreateTextPart("请解析这张截图，按照给定的 JSON Schema 输出。"),
                ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(imageBytes), imageMimeType))
        };

        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "parse_result",
                jsonSchema: BinaryData.FromString(parseResultSchemaJson),
                jsonSchemaIsStrict: true)
        };

        var completion = await _client.CompleteChatAsync(messages, options, cancellationToken);

        if (completion.Value.Content.Count == 0)
            throw new InvalidOperationException("Model returned empty content.");

        return completion.Value.Content[0].Text;
    }
}
