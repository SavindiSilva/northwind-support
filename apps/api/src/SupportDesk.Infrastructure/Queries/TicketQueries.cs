using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Common;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using Microsoft.Extensions.Options;
using SupportDesk.Application.Features.Tickets;

namespace SupportDesk.Infrastructure.Queries;

/// <summary>
/// Ticket reads. Projected in the database and never tracked. Aggregates hold only each
/// other's ids, so the customer, category and agent labels are joined in here, on the read side.
/// </summary>
public sealed class TicketQueries(
    SupportDbContext db,
    IClock clock,
    IOptions<SlaOptions> slaOptions) : ITicketQueries
{
    private readonly SlaOptions _sla = slaOptions.Value;
    /// <remarks>
    /// Applies the requested filters in the database before counting, sorting and paging
    /// the matching tickets.
    /// </remarks>
    
    public async Task<PagedResult<TicketListItemDto>> GetPagedAsync(
        TicketQuery query,
        CancellationToken ct)
    {
        var tickets = TicketsWithLabels();

        // Apply exact-match filters in the database.
        if (query.Status is not null)
        {
            tickets = tickets.Where(x => x.Ticket.Status == query.Status.Value);
        }

        if (query.Priority is not null)
        {
            tickets = tickets.Where(x => x.Ticket.Priority == query.Priority.Value);
        }

        if (query.CategoryId is not null)
        {
            tickets = tickets.Where(x => x.Ticket.CategoryId == query.CategoryId.Value);
        }

        if (query.CustomerId is not null)
        {
            tickets = tickets.Where(x => x.Ticket.CustomerId == query.CustomerId.Value);
        }

        if (query.AssignedAgentId is not null)
        {
            tickets = tickets.Where(
                x => x.Ticket.AssignedAgentId == query.AssignedAgentId.Value);
        }

        if (query.UnassignedOnly)
        {
            tickets = tickets.Where(x => x.Ticket.AssignedAgentId == null);
        }

        var now = clock.UtcNow;
        if (query.SlaStatus is not null)
        {
            var slaFilterNow = now;
            var lowHours = _sla.BaseWindowHours[nameof(TicketPriority.Low)];
            var mediumHours = _sla.BaseWindowHours[nameof(TicketPriority.Medium)];
            var highHours = _sla.BaseWindowHours[nameof(TicketPriority.High)];
            var criticalHours = _sla.BaseWindowHours[nameof(TicketPriority.Critical)];
            var premiumMultiplier = _sla.PremiumCustomerMultiplier;
            var minimumHours = _sla.MinimumWindowHours;
            var atRiskThreshold = _sla.AtRiskThresholdPercent / 100.0;

            tickets = query.SlaStatus.Value switch
            {
                SlaStatus.NotApplicable =>
                    tickets.Where(x => x.Ticket.DueAtUtc == null),

                SlaStatus.Met =>
                    tickets.Where(x =>
                        x.Ticket.DueAtUtc != null &&
                        x.Ticket.ResolvedAtUtc != null &&
                        x.Ticket.ResolvedAtUtc <= x.Ticket.DueAtUtc),

                SlaStatus.Breached =>
                    tickets.Where(x =>
                        x.Ticket.DueAtUtc != null &&
                        (x.Ticket.ResolvedAtUtc > x.Ticket.DueAtUtc ||
                        (x.Ticket.ResolvedAtUtc == null && x.Ticket.DueAtUtc < slaFilterNow))),

                SlaStatus.AtRisk =>
                    tickets.Where(x =>
                        x.Ticket.DueAtUtc != null &&
                        x.Ticket.ResolvedAtUtc == null &&
                        x.Ticket.DueAtUtc >= slaFilterNow &&
                        x.Ticket.DueAtUtc <= slaFilterNow.AddHours(
                            Math.Max(
                                minimumHours,
                                (x.Ticket.Priority == TicketPriority.Low ? lowHours :
                                x.Ticket.Priority == TicketPriority.Medium ? mediumHours :
                                x.Ticket.Priority == TicketPriority.High ? highHours :
                                criticalHours) *
                                (x.Customer.Tier == CustomerTier.Premium ? premiumMultiplier : 1.0)
                            ) * atRiskThreshold)),

                SlaStatus.WithinSla =>
                    tickets.Where(x =>
                        x.Ticket.DueAtUtc != null &&
                        x.Ticket.ResolvedAtUtc == null &&
                        x.Ticket.DueAtUtc >= slaFilterNow &&
                        x.Ticket.DueAtUtc > slaFilterNow.AddHours(
                            Math.Max(
                                minimumHours,
                                (x.Ticket.Priority == TicketPriority.Low ? lowHours :
                                x.Ticket.Priority == TicketPriority.Medium ? mediumHours :
                                x.Ticket.Priority == TicketPriority.High ? highHours :
                                criticalHours) *
                                (x.Customer.Tier == CustomerTier.Premium ? premiumMultiplier : 1.0)
                            ) * atRiskThreshold)),

                _ => tickets
            };
        }


        // Ignore blank searches and trim spaces around the search text.
        var search = query.Search?.Trim();

        if (!string.IsNullOrWhiteSpace(search))
        {
            tickets = tickets.Where(x =>
                x.Ticket.Title.Contains(search) ||
                x.Ticket.Reference.Contains(search) ||
                x.Customer.Name.Contains(search));
        }

        // Count only matching tickets, before sorting and pagination.
        var totalCount = await tickets.CountAsync(ct);

        var items = await ApplySort(tickets, query)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new TicketListItemDto
            {
                Id = x.Ticket.Id,
                Reference = x.Ticket.Reference,
                Title = x.Ticket.Title,
                Status = x.Ticket.Status,
                Priority = x.Ticket.Priority,
                Customer = new CustomerSummaryDto(
                    x.Customer.Id, x.Customer.Name, x.Customer.Tier),
                Category = new CategorySummaryDto(
                    x.Category.Id, x.Category.Name),
                AssignedAgent = x.Agent == null
                    ? null
                    : new AgentSummaryDto(x.Agent.Id, x.Agent.FullName),
                CreatedAtUtc = x.Ticket.CreatedAtUtc,
                UpdatedAtUtc = x.Ticket.UpdatedAtUtc,
                DueAtUtc = x.Ticket.DueAtUtc,
                ResolvedAtUtc = x.Ticket.ResolvedAtUtc,
                SlaStatus = SlaEvaluator.Evaluate(
                    x.Ticket.DueAtUtc,
                    x.Ticket.ResolvedAtUtc,
                    now,
                    CalculateSlaWindowHours(x.Customer.Tier, x.Ticket.Priority, _sla),
                    _sla.AtRiskThresholdPercent)
            })
            .ToListAsync(ct);

        return new PagedResult<TicketListItemDto>(
            items, query.Page, query.PageSize, totalCount);
    }


    public Task<TicketDetailDto?> GetDetailAsync(int id, CancellationToken ct)
    {
        var now = clock.UtcNow;

        return TicketsWithLabels()
            .Where(x => x.Ticket.Id == id)
            .Select(x => new TicketDetailDto
            {
                Id = x.Ticket.Id,
                Reference = x.Ticket.Reference,
                Title = x.Ticket.Title,
                Description = x.Ticket.Description,
                Status = x.Ticket.Status,
                Priority = x.Ticket.Priority,
                Customer = new CustomerContactDto(
                    x.Customer.Id, x.Customer.Name, x.Customer.Email, x.Customer.Phone, x.Customer.Tier),
                Category = new CategoryDto(x.Category.Id, x.Category.Name, x.Category.RequiresSpecialist),
                AssignedAgent = x.Agent == null ? null : new AgentSummaryDto(x.Agent.Id, x.Agent.FullName),
                CreatedAtUtc = x.Ticket.CreatedAtUtc,
                UpdatedAtUtc = x.Ticket.UpdatedAtUtc,
                DueAtUtc = x.Ticket.DueAtUtc,
                ResolvedAtUtc = x.Ticket.ResolvedAtUtc,
                SlaStatus = SlaEvaluator.Evaluate(
                    x.Ticket.DueAtUtc,
                    x.Ticket.ResolvedAtUtc,
                    now,
                    CalculateSlaWindowHours(x.Customer.Tier, x.Ticket.Priority, _sla),
                    _sla.AtRiskThresholdPercent)
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<TicketListItemDto>> GetForCustomerAsync(int customerId, CancellationToken ct)
    {
        var now = clock.UtcNow;

        return await TicketsWithLabels()
            .Where(x => x.Ticket.CustomerId == customerId)
            .OrderByDescending(x => x.Ticket.CreatedAtUtc)
            .ThenByDescending(x => x.Ticket.Id)
            .Select(x => new TicketListItemDto
            {
                Id = x.Ticket.Id,
                Reference = x.Ticket.Reference,
                Title = x.Ticket.Title,
                Status = x.Ticket.Status,
                Priority = x.Ticket.Priority,
                Customer = new CustomerSummaryDto(x.Customer.Id, x.Customer.Name, x.Customer.Tier),
                Category = new CategorySummaryDto(x.Category.Id, x.Category.Name),
                AssignedAgent = x.Agent == null ? null : new AgentSummaryDto(x.Agent.Id, x.Agent.FullName),
                CreatedAtUtc = x.Ticket.CreatedAtUtc,
                UpdatedAtUtc = x.Ticket.UpdatedAtUtc,
                DueAtUtc = x.Ticket.DueAtUtc,
                ResolvedAtUtc = x.Ticket.ResolvedAtUtc,
                SlaStatus = SlaEvaluator.Evaluate(
                    x.Ticket.DueAtUtc,
                    x.Ticket.ResolvedAtUtc,
                    now,
                    CalculateSlaWindowHours(x.Customer.Tier, x.Ticket.Priority, _sla),
                    _sla.AtRiskThresholdPercent)
            })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TicketEscalationDto>> GetEscalationsAsync(
        int ticketId,
        CancellationToken ct)
    {
        var query =
            from escalation in db.Set<TicketEscalation>().AsNoTracking()
            where escalation.TicketId == ticketId
            join fromAgentRow in db.Set<Agent>()
                on escalation.FromAgentId equals (int?)fromAgentRow.Id into fromAgents
            from fromAgent in fromAgents.DefaultIfEmpty()
            join toAgentRow in db.Set<Agent>()
                on escalation.ToAgentId equals (int?)toAgentRow.Id into toAgents
            from toAgent in toAgents.DefaultIfEmpty()
            orderby escalation.EscalatedAtUtc descending, escalation.Id descending
            select new TicketEscalationDto(
                escalation.Id,
                escalation.TicketId,
                escalation.FromPriority,
                escalation.ToPriority,
                fromAgent == null
                    ? null
                    : new AgentSummaryDto(fromAgent.Id, fromAgent.FullName),
                toAgent == null
                    ? null
                    : new AgentSummaryDto(toAgent.Id, toAgent.FullName),
                escalation.FromDueAtUtc,
                escalation.ToDueAtUtc,
                escalation.Reason,
                escalation.EscalatedBy,
                escalation.EscalatedAtUtc);

        return await query.ToListAsync(ct);
    }
    private static double CalculateSlaWindowHours(
        CustomerTier tier,
        TicketPriority priority,
        SlaOptions settings)
    {
        if (!settings.BaseWindowHours.TryGetValue(priority.ToString(), out var baseHours))
            throw new InvalidOperationException(
                $"No SLA window is configured for priority '{priority}'.");

        var multiplier = tier == CustomerTier.Premium
            ? settings.PremiumCustomerMultiplier
            : 1.0;

        return Math.Max(settings.MinimumWindowHours, baseHours * multiplier);
    }


    /// <summary>
    /// Every ticket with the rows that label it. Filter and sort this, then project.
    /// </summary>
    private IQueryable<TicketWithLabels> TicketsWithLabels() =>
        from ticket in db.Set<Ticket>().AsNoTracking()
        join customer in db.Set<Customer>() on ticket.CustomerId equals customer.Id
        join category in db.Set<Category>() on ticket.CategoryId equals category.Id
        from agent in db.Set<Agent>().Where(a => a.Id == ticket.AssignedAgentId).DefaultIfEmpty()
        select new TicketWithLabels { Ticket = ticket, Customer = customer, Category = category, Agent = agent };

    private static IQueryable<TicketWithLabels> ApplySort(IQueryable<TicketWithLabels> source, TicketQuery query)
    {
        var descending = !string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);

        var sorted = query.SortBy?.ToLowerInvariant() switch
        {
            "updatedatutc" => descending
                ? source.OrderByDescending(x => x.Ticket.UpdatedAtUtc)
                : source.OrderBy(x => x.Ticket.UpdatedAtUtc),
            "dueatutc" => descending
                ? source.OrderByDescending(x => x.Ticket.DueAtUtc)
                : source.OrderBy(x => x.Ticket.DueAtUtc),
            "priority" => descending
                ? source.OrderByDescending(x => x.Ticket.Priority)
                : source.OrderBy(x => x.Ticket.Priority),
            "status" => descending
                ? source.OrderByDescending(x => x.Ticket.Status)
                : source.OrderBy(x => x.Ticket.Status),
            _ => descending
                ? source.OrderByDescending(x => x.Ticket.CreatedAtUtc)
                : source.OrderBy(x => x.Ticket.CreatedAtUtc)
        };

        // Keeps paging stable when the sort column has duplicates.
        return sorted.ThenByDescending(x => x.Ticket.Id);
    }

    private sealed class TicketWithLabels
    {
        public required Ticket Ticket { get; init; }

        public required Customer Customer { get; init; }

        public required Category Category { get; init; }

        public Agent? Agent { get; init; }
    }
}
