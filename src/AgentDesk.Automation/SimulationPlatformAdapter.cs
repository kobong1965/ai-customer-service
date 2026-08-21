using System.Collections.Concurrent;
using AgentDesk.Core;

namespace AgentDesk.Automation;

public sealed record SimulatedSentMessage(
    IncomingMessage Incoming,
    string Reply,
    SendReceipt Receipt);

public sealed class SimulationPlatformAdapter : ISupportPlatformAdapter
{
    private readonly ConcurrentQueue<IncomingMessage> _incoming = new();
    private readonly ConcurrentQueue<SimulatedSentMessage> _sent = new();

    public string Name => "隔离模拟客服平台";

    public IReadOnlyList<SimulatedSentMessage> SentMessages => _sent.ToArray();

    public IncomingMessage InjectCustomerMessage(
        string text,
        string accountId = "simulation-account",
        string customerAlias = "模拟客户")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var incoming = new IncomingMessage(
            Guid.NewGuid().ToString("N"),
            accountId,
            customerAlias,
            text.Trim(),
            DateTimeOffset.Now);

        _incoming.Enqueue(incoming);
        return incoming;
    }

    public ValueTask<IncomingMessage?> ReceiveNextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_incoming.TryDequeue(out var message) ? message : null);
    }

    public Task<SendReceipt> SendReplyAsync(
        IncomingMessage incoming,
        string reply,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(reply);

        var receipt = new SendReceipt(
            incoming.Id,
            $"SIM-{DateTimeOffset.Now:yyyyMMddHHmmssfff}",
            DateTimeOffset.Now);

        _sent.Enqueue(new SimulatedSentMessage(incoming, reply.Trim(), receipt));
        return Task.FromResult(receipt);
    }
}
