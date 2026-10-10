using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Features.Tickets;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.RaiseTicket;

/// <summary>
/// Raises a ticket and applies automated triage.
/// </summary>
public sealed class RaiseTicketCommandHandler(
    ITicketRepository tickets,
    IUnitOfWork unitOfWork,
    IClock clock,
    TicketTriageService triageService,
    GetTicketQueryHandler getTicket,
    ILogger<RaiseTicketCommandHandler> logger)
{
    public async Task<TicketDetailDto> HandleAsync(
        CreateTicketRequest request,
        CancellationToken ct)
    {
        var now = clock.UtcNow;

        var triage = await triageService.TriageAsync(
            request.CustomerId,
            request.CategoryId,
            request.RequestedPriority ?? TicketPriority.Medium,
            now,
            ct);

        var ticket = Ticket.Raise(
            await tickets.NextReferenceAsync(ct),
            request.Title,
            request.Description,
            request.CustomerId,
            request.CategoryId,
            triage.Priority,
            now);

        ticket.ApplyTriage(
            triage.Priority,
            triage.AssignedAgentId,
            triage.DueAtUtc,
            now);

        await tickets.AddAsync(ticket, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Ticket {Reference} raised with priority {Priority}, assigned agent {AgentId}, due {DueAtUtc}.",
            ticket.Reference,
            ticket.Priority,
            ticket.AssignedAgentId,
            ticket.DueAtUtc);

        var result = await getTicket.HandleAsync(ticket.Id, ct);
        result.TriageReasons = triage.Reasons.ToList();
        result.Triage = new TicketTriageDto(
            triage.PriorityReason,
            triage.SlaWindowHours,
            triage.SlaReason,
            triage.AssignmentReason);

        return result;
    }
}