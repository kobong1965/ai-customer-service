namespace AgentDesk.Core;

public sealed class AgentRuntimeService : IAsyncDisposable
{
    private readonly AgentOrchestrator _orchestrator;
    private readonly TimeSpan _pollInterval;
    private readonly AgentExecutionMode _executionMode;
    private readonly RuntimeSafetyLimits _limits;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Queue<DateTimeOffset> _minuteSends = new();
    private int _dailySentCount;
    private DateOnly _countDate;
    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    public AgentRuntimeService(
        AgentOrchestrator orchestrator,
        TimeSpan pollInterval,
        AgentExecutionMode executionMode = AgentExecutionMode.AutoSend,
        RuntimeSafetyLimits? limits = null,
        int initialDailySentCount = 0,
        Func<DateTimeOffset>? clock = null)
    {
        _orchestrator = orchestrator;
        _pollInterval = pollInterval;
        _executionMode = executionMode;
        _limits = limits ?? RuntimeSafetyLimits.Default;
        if (!_limits.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "自动发送限额无效。");
        }

        _clock = clock ?? (() => DateTimeOffset.Now);
        _dailySentCount = Math.Max(0, initialDailySentCount);
        _countDate = DateOnly.FromDateTime(_clock().LocalDateTime);
    }

    public event EventHandler<RunEvent>? EventRecorded;

    public bool IsRunning => _loop is { IsCompleted: false };
    public AgentExecutionMode ExecutionMode => _executionMode;
    public int DailySentCount => _dailySentCount;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _orchestrator.EventRecorded += ForwardEvent;
        _loop = RunLoopAsync(_cancellation.Token);
        var modeText = _executionMode is AgentExecutionMode.Shadow ? "影子观察" : "自动发送";
        Record(AgentStage.Monitoring, $"智能客服已启动：{modeText}模式，正在观察客服平台");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cancellation is null)
        {
            return;
        }

        await _cancellation.CancelAsync();
        if (_loop is not null)
        {
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _orchestrator.EventRecorded -= ForwardEvent;
        _cancellation.Dispose();
        _cancellation = null;
        _loop = null;
        Record(AgentStage.Stopped, "正式智能客服已停止，后续自动化动作已取消");
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_executionMode is AgentExecutionMode.AutoSend
                    && !CanSendNow(out var limitReason))
                {
                    Record(AgentStage.RateLimited, limitReason, true);
                    Record(AgentStage.Stopped, "已触发自动发送限额，服务安全停止", true);
                    break;
                }

                var result = await _orchestrator.ProcessNextAsync(
                    _executionMode is AgentExecutionMode.AutoSend,
                    cancellationToken);
                if (result?.WasSent is true)
                {
                    RegisterSend();
                }

                consecutiveFailures = 0;
                await Task.Delay(_pollInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                consecutiveFailures++;
                Record(
                    AgentStage.Failed,
                    $"运行故障（连续 {consecutiveFailures} 次）：{exception.Message}",
                    true);

                if (consecutiveFailures >= 5)
                {
                    Record(AgentStage.Stopped, "连续故障达到上限，已安全停止自动发送", true);
                    break;
                }

                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, consecutiveFailures)));
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private void ForwardEvent(object? sender, RunEvent runEvent) =>
        EventRecorded?.Invoke(this, runEvent);

    private bool CanSendNow(out string reason)
    {
        var now = _clock();
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        if (today != _countDate)
        {
            _countDate = today;
            _dailySentCount = 0;
            _minuteSends.Clear();
        }

        while (_minuteSends.TryPeek(out var timestamp)
               && now - timestamp >= TimeSpan.FromMinutes(1))
        {
            _minuteSends.Dequeue();
        }

        if (_dailySentCount >= _limits.DailySendLimit)
        {
            reason = $"今日自动发送已达到上限 {_limits.DailySendLimit} 条";
            return false;
        }

        if (_minuteSends.Count >= _limits.PerMinuteSendLimit)
        {
            reason = $"一分钟自动发送已达到上限 {_limits.PerMinuteSendLimit} 条";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private void RegisterSend()
    {
        var now = _clock();
        _dailySentCount++;
        _minuteSends.Enqueue(now);
    }

    private void Record(AgentStage stage, string summary, bool isError = false) =>
        EventRecorded?.Invoke(
            this,
            new RunEvent(DateTimeOffset.Now, "live", stage, summary, isError));
}
