using System.ClientModel;
using OpenAI;
using OpenAI.Chat;

namespace HealthyGuidance.Core.AzureOpenAI;

#pragma warning disable OPENAI001

public class AdviceClient
{
    private readonly ChatClient _client;

    public AdviceClient(string endpoint, string apiKey, string deploymentName)
    {
        _client = new ChatClient(
            credential: new ApiKeyCredential(apiKey),
            model: deploymentName,
            options: new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
    }

    public async Task<string> GenerateAsync(
        string systemPrompt,
        string adviceResultSchemaJson,
        CancellationToken cancellationToken = default)
    {
        var messages = new ChatMessage[]
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage("请基于以上数据生成报告，严格按 JSON Schema 输出。")
        };

        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "advice_result",
                jsonSchema: BinaryData.FromString(adviceResultSchemaJson),
                jsonSchemaIsStrict: true)
        };

        var completion = await _client.CompleteChatAsync(messages, options, cancellationToken);

        if (completion.Value.Content.Count == 0)
            throw new InvalidOperationException("Model returned empty content.");

        return completion.Value.Content[0].Text;
    }
}
