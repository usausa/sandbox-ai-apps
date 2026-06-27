namespace AutoOrder.Agent;

using System.ClientModel;

using Azure.AI.OpenAI;
using Azure.Identity;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

// 発注エージェントの DI 登録。Foundry の IChatClient と Agent Framework のエージェントを構成する。
// ツール・プロンプト・承認フローの本実装は Phase 4 で行う（ここでは最小の配線のみ）。
public static class AgentServiceCollectionExtensions
{
    public static IServiceCollection AddOrderAgent(this IServiceCollection services, FoundrySettings settings)
    {
        services.AddSingleton(settings);

        services.AddSingleton(_ =>
        {
            var endpoint = new Uri(settings.Endpoint);
            return string.IsNullOrWhiteSpace(settings.ApiKey)
                ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential())
                : new AzureOpenAIClient(endpoint, new ApiKeyCredential(settings.ApiKey));
        });

        services.AddSingleton<IChatClient>(static provider =>
        {
            var client = provider.GetRequiredService<AzureOpenAIClient>();
            var foundry = provider.GetRequiredService<FoundrySettings>();
            return client.GetChatClient(foundry.ChatDeployment).AsIChatClient();
        });

        services.AddSingleton<AIAgent>(static provider =>
        {
            var chatClient = provider.GetRequiredService<IChatClient>();
            return chatClient.AsAIAgent(
                instructions: "あなたは食品スーパーの発注業務を支援するエージェントです。発注に関係しない話題には応じません。",
                name: "AutoOrderAgent");
        });

        return services;
    }
}
