namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// Derives SLA status from ticket dates, the SLA window, and the current time.
/// SLA status is never stored.
/// </summary>
public static class SlaEvaluator
{
    public static SlaStatus Evaluate(
        DateTime? dueAtUtc,
        DateTime? resolvedAtUtc,
        DateTime nowUtc,
        double? slaWindowHours = null,
        double atRiskThresholdPercent = 25)
    {
        if (dueAtUtc is null)
            return SlaStatus.NotApplicable;

        if (resolvedAtUtc is not null)
            return resolvedAtUtc <= dueAtUtc
                ? SlaStatus.Met
                : SlaStatus.Breached;

        if (nowUtc > dueAtUtc.Value)
            return SlaStatus.Breached;

        if (slaWindowHours is > 0)
        {
            var remainingHours = (dueAtUtc.Value - nowUtc).TotalHours;
            var atRiskHours = slaWindowHours.Value * atRiskThresholdPercent / 100.0;

            if (remainingHours <= atRiskHours)
                return SlaStatus.AtRisk;
        }

        return SlaStatus.WithinSla;
    }
}
