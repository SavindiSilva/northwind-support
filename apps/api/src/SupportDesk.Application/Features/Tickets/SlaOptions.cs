namespace SupportDesk.Application.Features.Tickets;

public sealed class SlaOptions
{
    public const string SectionName = "Sla";

    public Dictionary<string, int> BaseWindowHours { get; set; } = new()
    {
        ["Low"] = 72,
        ["Medium"] = 24,
        ["High"] = 8,
        ["Critical"] = 4
    };

    public double PremiumCustomerMultiplier { get; set; } = 0.5;

    public double MinimumWindowHours { get; set; } = 1;

    public double AtRiskThresholdPercent { get; set; } = 25;
}