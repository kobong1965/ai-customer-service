using AgentDesk.Core;
using AgentDesk.Infrastructure;

namespace AgentDesk.Automation;

public sealed class LivePlatformAdapter(
    SimulationApprovalService approvalService,
    string configurationFingerprint,
    WindowsPlatformAutomation automation,
    IScreenObserver screenObserver,
    PlatformCalibrationSettings calibration,
    bool requireLiveApproval = true) : ISupportPlatformAdapter
{
    private string? _lastObservedHash;
    private string? _lastProcessedHash;
    private ScreenObservation? _pendingObservation;
    private CapturedPlatformWindow? _pendingCapture;

    public string Name => "Windows 客服平台视觉适配器";

    public async ValueTask<IncomingMessage?> ReceiveNextAsync(CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken);
        var capture = automation.Capture(calibration.WindowTitleContains);
        WindowsPlatformAutomation.EnsureStableSize(capture.Window, calibration);

        if (_lastObservedHash is null)
        {
            _lastObservedHash = capture.ContentHash;
            return null;
        }

        if (string.Equals(_lastObservedHash, capture.ContentHash, StringComparison.Ordinal)
            || string.Equals(_lastProcessedHash, capture.ContentHash, StringComparison.Ordinal))
        {
            return null;
        }

        _lastObservedHash = capture.ContentHash;
        var observation = await screenObserver.ObserveAsync(capture.DataUrl, cancellationToken);
        if (observation.Confidence < calibration.MinimumObserverConfidence)
        {
            return null;
        }

        switch (observation.Action)
        {
            case ScreenAction.SwitchAccount:
                EnsureClickWithin(observation, 0, 0, 1, 0.28, "账号标签区域");
                WindowsPlatformAutomation.EnsureStableSize(capture.Window, calibration);
                await automation.ClickRelativeAsync(
                    capture.Window,
                    observation.ClickX,
                    observation.ClickY,
                    cancellationToken);
                _lastObservedHash = null;
                return null;

            case ScreenAction.OpenConversation:
                EnsureClickWithin(observation, 0, 0.12, 0.36, 0.96, "左侧会话列表区域");
                WindowsPlatformAutomation.EnsureStableSize(capture.Window, calibration);
                await automation.ClickRelativeAsync(
                    capture.Window,
                    observation.ClickX,
                    observation.ClickY,
                    cancellationToken);
                await Task.Delay(800, cancellationToken);
                capture = automation.Capture(calibration.WindowTitleContains);
                _lastObservedHash = capture.ContentHash;
                break;

            case ScreenAction.ProcessActiveConversation:
                break;

            default:
                return null;
        }

        _lastProcessedHash = capture.ContentHash;
        _pendingObservation = observation;
        _pendingCapture = capture;
        return new IncomingMessage(
            Guid.NewGuid().ToString("N"),
            string.IsNullOrWhiteSpace(observation.AccountLabel)
                ? "未识别账号"
                : observation.AccountLabel.Trim(),
            string.IsNullOrWhiteSpace(observation.CustomerAlias) ? "当前客户" : observation.CustomerAlias,
            string.IsNullOrWhiteSpace(observation.LatestCustomerMessage)
                ? "请从截图读取当前会话最新一条客户消息"
                : observation.LatestCustomerMessage,
            DateTimeOffset.Now,
            capture.DataUrl,
            observation.ProductKey?.Trim() ?? string.Empty);
    }

    public async Task<SendReceipt> SendReplyAsync(
        IncomingMessage incoming,
        string reply,
        CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken);
        if (_pendingCapture is null || _pendingObservation is null)
        {
            throw new InvalidOperationException("没有与当前回复匹配的已验证客服窗口截图。");
        }

        var current = automation.Capture(calibration.WindowTitleContains);
        WindowsPlatformAutomation.EnsureStableSize(current.Window, calibration);
        var verification = await screenObserver.ObserveAsync(current.DataUrl, cancellationToken);
        if (verification.Action is not ScreenAction.ProcessActiveConversation
            || verification.Confidence < calibration.MinimumObserverConfidence)
        {
            throw new InvalidOperationException("发送前复核未确认当前会话，已取消真实发送。");
        }

        if (string.IsNullOrWhiteSpace(_pendingObservation.CustomerAlias)
            || string.IsNullOrWhiteSpace(verification.CustomerAlias))
        {
            throw new InvalidOperationException("发送前无法确认客户标识，已取消真实发送。");
        }

        if (!string.Equals(
            _pendingObservation.CustomerAlias.Trim(),
            verification.CustomerAlias.Trim(),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("发送前客户标识发生变化，已取消真实发送。");
        }

        if (!MessagesMatch(
            _pendingObservation.LatestCustomerMessage,
            verification.LatestCustomerMessage))
        {
            throw new InvalidOperationException("发送前最新客户消息发生变化，已取消真实发送。");
        }

        if (string.IsNullOrWhiteSpace(_pendingObservation.AccountLabel)
            || string.IsNullOrWhiteSpace(verification.AccountLabel)
            || !string.Equals(
                _pendingObservation.AccountLabel.Trim(),
                verification.AccountLabel.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("发送前无法确认同一客服账号标签，已取消真实发送。");
        }

        if (!string.IsNullOrWhiteSpace(_pendingObservation.ProductKey)
            && (string.IsNullOrWhiteSpace(verification.ProductKey)
                || !ProductKeysMatch(_pendingObservation.ProductKey, verification.ProductKey)))
        {
            throw new InvalidOperationException("发送前商品或版本标识发生变化，已取消真实发送。");
        }

        await automation.TypeAndSendAsync(current.Window, calibration, reply, cancellationToken);
        _pendingCapture = null;
        _pendingObservation = null;
        _lastObservedHash = null;
        return new SendReceipt(incoming.Id, current.Window.Title, DateTimeOffset.Now);
    }

    private async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (requireLiveApproval
            && !await approvalService.IsLiveUnlockedAsync(configurationFingerprint, cancellationToken))
        {
            throw new InvalidOperationException("正式模式未解锁：请先完成模拟测试并由人工批准。");
        }

        if (!calibration.IsValid)
        {
            throw new InvalidOperationException("真实客服平台尚未完成有效校准。");
        }
    }

    private static void EnsureClickWithin(
        ScreenObservation observation,
        double left,
        double top,
        double right,
        double bottom,
        string regionName)
    {
        if (observation.ClickX < left || observation.ClickX > right
            || observation.ClickY < top || observation.ClickY > bottom)
        {
            throw new InvalidOperationException($"模型给出的点击位置不在{regionName}内，已拒绝执行。");
        }
    }

    private static bool MessagesMatch(string pending, string current)
    {
        if (string.IsNullOrWhiteSpace(pending) || string.IsNullOrWhiteSpace(current))
        {
            return false;
        }

        var left = string.Concat(pending.Where(character => !char.IsWhiteSpace(character))).Trim();
        var right = string.Concat(current.Where(character => !char.IsWhiteSpace(character))).Trim();
        return left.Length >= 2
            && right.Length >= 2
            && (left.Contains(right, StringComparison.OrdinalIgnoreCase)
                || right.Contains(left, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ProductKeysMatch(string pending, string current)
    {
        var left = string.Concat(pending.Where(character => !char.IsWhiteSpace(character))).Trim();
        var right = string.Concat(current.Where(character => !char.IsWhiteSpace(character))).Trim();
        return left.Length >= 2
            && right.Length >= 2
            && left.Equals(right, StringComparison.OrdinalIgnoreCase);
    }
}
