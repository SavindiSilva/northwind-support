using Microsoft.Extensions.Options;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Agents;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets;

public sealed record TicketTriageResult(
    TicketPriority Priority,
    int? AssignedAgentId,
    DateTime DueAtUtc,
    IReadOnlyList<string> Reasons,
    string PriorityReason,
    double SlaWindowHours,
    string SlaReason,
    string AssignmentReason);

public sealed class TicketTriageService(
    ICustomerRepository customers,
    ICategoryRepository categories,
    IAgentQueries agents,
    IOptions<SlaOptions> options)
{
    public async Task<TicketTriageResult> TriageAsync(
        int customerId,
        int categoryId,
        TicketPriority requestedPriority,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var customer = await customers.GetByIdAsync(customerId, ct)
            ?? throw new NotFoundException("Customer", customerId);

        var category = await categories.GetByIdAsync(categoryId, ct)
            ?? throw new NotFoundException("Category", categoryId);

        var priority = category.ForcesCriticalPriority
            ? TicketPriority.Critical
            : requestedPriority;

        var dueAtUtc = CalculateDueAtUtc(customer.Tier, priority, nowUtc);
        var allAgents = await agents.GetAllAsync(ct);

        var eligibleAgent = allAgents
            .Where(agent => IsEligible(agent, category.Id, category.RequiresSpecialist))
            .OrderBy(agent => agent.OpenTicketCount)
            .ThenBy(agent => agent.Id)
            .FirstOrDefault();

        var reasons = new List<string>
        {
            category.ForcesCriticalPriority
                ? $"Category '{category.Name}' requires Critical priority."
                : $"Requested priority '{requestedPriority}' retained.",
            DescribeSla(customer.Tier, dueAtUtc, nowUtc)
        };

        reasons.Add(eligibleAgent is null
            ? DescribeNoEligibleAgent(category.RequiresSpecialist)
            : $"Assigned to {eligibleAgent.FullName}, the eligible agent with the fewest open tickets.");

        return new TicketTriageResult(
            priority,
            eligibleAgent?.Id,
            dueAtUtc,
            reasons,
            category.ForcesCriticalPriority
                ? $"Category '{category.Name}' requires Critical priority."
                : $"Requested priority '{requestedPriority}' retained.",
            CalculateSlaWindowHours(customer.Tier, priority),
            reasons[1],
            reasons[2]);
    }
    public async Task<TicketTriageResult> ReevaluateForEscalationAsync(
        int customerId,
        int categoryId,
        TicketPriority newPriority,
        int? currentAgentId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var customer = await customers.GetByIdAsync(customerId, ct)
            ?? throw new NotFoundException("Customer", customerId);

        var category = await categories.GetByIdAsync(categoryId, ct)
            ?? throw new NotFoundException("Category", categoryId);

        var dueAtUtc = CalculateDueAtUtc(customer.Tier, newPriority, nowUtc);
        var allAgents = await agents.GetAllAsync(ct);

        var currentAgent = currentAgentId is null
            ? null
            : allAgents.FirstOrDefault(agent => agent.Id == currentAgentId.Value);

        // The current ticket is already included in the agent's open-ticket count.
        var canKeepCurrentAgent = currentAgent is not null
            && currentAgent.IsActive
            && currentAgent.OpenTicketCount > 0
            && currentAgent.OpenTicketCount - 1 < currentAgent.MaxOpenTickets
            && (!category.RequiresSpecialist
                || currentAgent.Specializations.Any(s => s.Id == category.Id));

        var selectedAgent = canKeepCurrentAgent
            ? currentAgent
            : allAgents
                .Where(agent => IsEligible(
                    agent,
                    category.Id,
                    category.RequiresSpecialist))
                .OrderBy(agent => agent.OpenTicketCount)
                .ThenBy(agent => agent.Id)
                .FirstOrDefault();

        var reasons = new List<string>
        {
            $"Priority increased to '{newPriority}'.",
            DescribeSla(customer.Tier, dueAtUtc, nowUtc)
        };

        if (canKeepCurrentAgent)
        {
            reasons.Add($"Kept current assignment to {currentAgent!.FullName}.");
        }
        else if (selectedAgent is not null)
        {
            reasons.Add(
                $"Assigned to {selectedAgent.FullName} based on agent eligibility and workload.");
        }
        else
        {
            reasons.Add(DescribeNoEligibleAgent(category.RequiresSpecialist));
        }

        return new TicketTriageResult(
            newPriority,
            selectedAgent?.Id,
            dueAtUtc,
            reasons,
            $"Priority increased to '{newPriority}'.",
            CalculateSlaWindowHours(customer.Tier, newPriority),
            reasons[1],
            reasons[2]);
    }



    private DateTime CalculateDueAtUtc(
        CustomerTier tier,
        TicketPriority priority,
        DateTime now)
    {
        var settings = options.Value;

        if (!settings.BaseWindowHours.TryGetValue(priority.ToString(), out var baseHours)
            || baseHours <= 0)
        {
            throw new InvalidOperationException(
                $"No valid SLA window is configured for priority '{priority}'.");
        }

        var multiplier = tier == CustomerTier.Premium
            ? settings.PremiumCustomerMultiplier
            : 1.0;

        var windowHours = Math.Max(
            settings.MinimumWindowHours,
            baseHours * multiplier);

        return now.AddHours(windowHours);
    }

    private double CalculateSlaWindowHours(
        CustomerTier tier,
        TicketPriority priority)
    {
        var settings = options.Value;
        var baseHours = settings.BaseWindowHours[priority.ToString()];
        var multiplier = tier == CustomerTier.Premium
            ? settings.PremiumCustomerMultiplier
            : 1.0;

        return Math.Max(settings.MinimumWindowHours, baseHours * multiplier);
    }

    private static bool IsEligible(
        AgentDto agent,
        int categoryId,
        bool requiresSpecialist) =>
        agent.IsActive
        && agent.OpenTicketCount < agent.MaxOpenTickets
        && (!requiresSpecialist
            || agent.Specializations.Any(s => s.Id == categoryId));

    private static string DescribeSla(
        CustomerTier tier,
        DateTime dueAtUtc,
        DateTime now)
    {
        var hours = (dueAtUtc - now).TotalHours;
        return tier == CustomerTier.Premium
            ? $"Premium SLA applied: {hours:g} hours."
            : $"Standard SLA applied: {hours:g} hours.";
    }

    private static string DescribeNoEligibleAgent(bool requiresSpecialist) =>
        requiresSpecialist
            ? "No active specialist has available ticket capacity; ticket left unassigned."
            : "No active agent has available ticket capacity; ticket left unassigned.";
}