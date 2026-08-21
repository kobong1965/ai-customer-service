using AgentDesk.Core;

namespace AgentDesk.AI;

public sealed class RuleBasedReplyGenerator : IReplyGenerator
{
    private static readonly string[] HighRiskKeywords =
    [
        "退款", "退货", "赔偿", "投诉", "差评", "改价", "优惠", "修改地址", "改地址", "修改订单"
    ];

    public Task<ReplyDecision> GenerateAsync(
        IncomingMessage incoming,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var text = incoming.Text.Trim();

        if (HighRiskKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(HumanRequired("命中高风险业务规则"));
        }

        if (text.Contains("保证", StringComparison.OrdinalIgnoreCase)
            || text.Contains("永远", StringComparison.OrdinalIgnoreCase)
            || text.Contains("绝对", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(HumanRequired("客户要求绝对承诺，规则禁止自动回复"));
        }

        if (ContainsAny(text, "有货", "库存", "现货"))
        {
            return Task.FromResult(new ReplyDecision(
                RiskLevel.Low,
                false,
                "亲，这款目前显示有库存，可以正常下单。库存会随订单变化，请以客服平台当前页面为准。",
                ["kb:simulation:stock-policy-v1"],
                []));
        }

        if (ContainsAny(text, "发货", "多久发", "什么时候发"))
        {
            return Task.FromResult(new ReplyDecision(
                RiskLevel.Low,
                false,
                "亲，现货订单通常会按店铺页面标注的时效安排发出，您下单后可以在订单详情查看最新进度。",
                ["kb:simulation:shipping-policy-v1"],
                []));
        }

        if (ContainsAny(text, "尺码", "偏大", "偏小", "身高", "体重"))
        {
            return Task.FromResult(new ReplyDecision(
                RiskLevel.Low,
                false,
                "亲，可以把您的身高、体重和通常穿着尺码告诉我，我会结合商品尺码表帮您参考；最终请以实际试穿感受为准。",
                ["kb:simulation:size-policy-v1"],
                []));
        }

        return Task.FromResult(HumanRequired("未找到足够可靠的知识依据"));
    }

    private static bool ContainsAny(string value, params string[] keywords) =>
        keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static ReplyDecision HumanRequired(string warning) =>
        new(
            RiskLevel.High,
            true,
            string.Empty,
            [],
            [warning]);
}
