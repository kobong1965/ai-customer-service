using AgentDesk.Core;

namespace AgentDesk.Automation;

public sealed class LockedLivePlatformAdapter(
    SimulationApprovalService approvalService,
    string configurationFingerprint) : ISupportPlatformAdapter
{
    public string Name => "真实客服平台（尚未校准）";

    public async ValueTask<IncomingMessage?> ReceiveNextAsync(CancellationToken cancellationToken)
    {
        await EnsureUnlockedAsync(cancellationToken);
        throw new InvalidOperationException("真实客服平台尚未完成窗口校准和账号绑定。");
    }

    public async Task<SendReceipt> SendReplyAsync(
        IncomingMessage incoming,
        string reply,
        CancellationToken cancellationToken)
    {
        await EnsureUnlockedAsync(cancellationToken);
        throw new InvalidOperationException("真实客服平台尚未完成发送按钮校准，拒绝执行真实发送。");
    }

    private async Task EnsureUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!await approvalService.IsLiveUnlockedAsync(configurationFingerprint, cancellationToken))
        {
            throw new InvalidOperationException("正式模式未解锁：请先完成模拟测试并由人工批准。");
        }
    }
}
