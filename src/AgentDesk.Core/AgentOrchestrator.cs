namespace AgentDesk.Core;

public sealed class AgentOrchestrator(
    ISupportPlatformAdapter platform,
    IReplyGenerator replyGenerator,
    IExperienceRecorder? experienceRecorder = null)
{
    public event EventHandler<RunEvent>? EventRecorded;

    public async Task<ProcessResult?> ProcessNextAsync(
        bool allowAutoSend,
        CancellationToken cancellationToken = default)
    {
        var incoming = await platform.ReceiveNextAsync(cancellationToken);
        if (incoming is null)
        {
            return null;
        }

        Record(incoming.AccountId, AgentStage.MessageDetected, "检测到新的平台消息");
        if (incoming.AccountId.Equals("未识别账号", StringComparison.OrdinalIgnoreCase))
        {
            const string accountOutcome = "无法确认客服账号标签，已转人工且未发送";
            var accountDecision = new ReplyDecision(
                RiskLevel.High,
                true,
                string.Empty,
                [],
                ["账号标签缺失"]);
            Record(incoming.AccountId, AgentStage.HumanRequired, accountOutcome);
            return await CompleteAsync(
                new ProcessResult(incoming, accountDecision, false, accountOutcome, null),
                cancellationToken);
        }

        Record(incoming.AccountId, AgentStage.AccountValidated, "账号和客户标识校验通过");
        Record(incoming.AccountId, AgentStage.Drafting, "正在生成回复决策");

        ReplyDecision decision;
        try
        {
            decision = await replyGenerator.GenerateAsync(incoming, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Record(incoming.AccountId, AgentStage.Failed, $"回复生成失败：{exception.Message}", true);
            throw;
        }

        Record(incoming.AccountId, AgentStage.SafetyCheck, $"风险检查：{decision.RiskLevel}");

        if (decision.RequiresHuman
            || decision.RiskLevel is not RiskLevel.Low
            || string.IsNullOrWhiteSpace(decision.DraftReply)
            || decision.FactsUsed.Count == 0)
        {
            const string outcome = "高风险或资料不足，已转人工，未自动发送";
            Record(incoming.AccountId, AgentStage.HumanRequired, outcome);
            return await CompleteAsync(
                new ProcessResult(incoming, decision, false, outcome, null),
                cancellationToken);
        }

        if (!allowAutoSend)
        {
            const string outcome = "影子观察完成：自动发送尚未解锁，低风险回复仅记录、未发送";
            Record(incoming.AccountId, AgentStage.ShadowObserved, outcome);
            return await CompleteAsync(
                new ProcessResult(incoming, decision, false, outcome, null),
                cancellationToken);
        }

        Record(incoming.AccountId, AgentStage.Sending, "低风险校验通过，正在自动发送");
        var receipt = await platform.SendReplyAsync(incoming, decision.DraftReply, cancellationToken);
        const string completedOutcome = "低风险回复已自动发送";
        Record(incoming.AccountId, AgentStage.Completed, completedOutcome);

        return await CompleteAsync(
            new ProcessResult(incoming, decision, true, completedOutcome, receipt),
            cancellationToken);
    }

    private async Task<ProcessResult> CompleteAsync(
        ProcessResult result,
        CancellationToken cancellationToken)
    {
        if (experienceRecorder is null)
        {
            return result;
        }

        try
        {
            await experienceRecorder.RecordAsync(result, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Record(result.Incoming.AccountId, AgentStage.Failed, $"经验候选记录失败：{exception.Message}", true);
        }

        return result;
    }

    private void Record(string accountId, AgentStage stage, string summary, bool isError = false)
    {
        EventRecorded?.Invoke(
            this,
            new RunEvent(DateTimeOffset.Now, accountId, stage, summary, isError));
    }
}
