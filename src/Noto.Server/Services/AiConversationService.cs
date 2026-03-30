using System.Runtime.CompilerServices;
using System.Text;

namespace Noto.Server.Services;

public class AiConversationService
{
    private readonly OpenRouterClient _client;

    public AiConversationService(OpenRouterClient client)
    {
        _client = client;
    }

    private const string SystemPrompt = """
        You are a thoughtful conversation partner for personal reflection and synthesis.
        You have access to context from the person's archive — resonant moments, dreams,
        and life observations they have collected over months.

        Guidelines:
        - Respond to what the person brings. Never initiate topics.
        - Name patterns you notice, without insisting on them.
        - Hold material from multiple angles without collapsing it into a single interpretation.
        - Be specific and grounded — use what you see in the context, not generic observations.
        - Tone: warm, clear, reflective. Not clinical, not effusive.
        - When the person is writing/reflecting, your role is to deepen, not redirect.
        - Keep responses concise unless depth is clearly called for.
        """;

    public string DefaultPrompt => SystemPrompt;

    public string BuildSystemPromptWithSeeds(List<(string Title, string Body)> seeds, string? customPrompt = null)
    {
        var basePrompt = customPrompt ?? SystemPrompt;
        if (seeds.Count == 0)
            return basePrompt;

        var sb = new StringBuilder(basePrompt);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Context seeds from the person's archive:");
        sb.AppendLine();

        foreach (var (title, body) in seeds)
        {
            sb.AppendLine($"--- {title} ---");
            sb.AppendLine(body);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public async IAsyncEnumerable<string> StreamResponse(
        string systemPrompt,
        List<OpenRouterClient.ChatMessage> conversationHistory,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = new List<OpenRouterClient.ChatMessage>
        {
            new("system", systemPrompt)
        };
        messages.AddRange(conversationHistory);
        messages.Add(new("user", userMessage));

        await foreach (var token in _client.StreamChat(messages, ct: ct))
        {
            yield return token;
        }
    }
}
