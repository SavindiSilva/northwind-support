using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

public sealed class EscalateTicketCommandHandler(
    ITicketRepository tickets,
    ITicketEscalationRepository escalations,
    ITicketQueries ticketQueries,
    IUnitOfWork unitOfWork,
    IClock clock,
    TicketTriageService triage,
    GetTicketQueryHandler getTicket)
{
    public async Task<EscalateTicketResultDto> HandleAsync(
        int id,
        EscalateTicketRequest request,
        string escalatedBy,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        var reason = request.Reason?.Trim() ?? string.Empty;
        escalatedBy = escalatedBy?.Trim() ?? string.Empty;

        if (reason.Length < 5 || reason.Length > 500)
        {
            errors[nameof(request.Reason)] =
                ["Reason must contain between 5 and 500 characters."];
        }

        if (escalatedBy.Length is < 1 or > 100)
        {
            errors["EscalatedBy"] =
                ["Authenticated agent name is required and must not exceed 100 characters."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var ticket = await tickets.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Ticket", id);

        if (ticket.Priority == TicketPriority.Critical)
        {
            throw new ConflictException(
                $"Ticket {ticket.Reference} is already Critical and cannot be escalated.");
        }

        if (ticket.Status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            throw new ConflictException(
                $"Ticket {ticket.Reference} is resolved or closed and cannot be escalated.");
        }

        var fromPriority = ticket.Priority;
        var fromAgentId = ticket.AssignedAgentId;
        var fromDueAtUtc = ticket.DueAtUtc;
        var escalatedAtUtc = clock.UtcNow;
        var newPriority = (TicketPriority)((int)fromPriority + 1);

        var triageResult = await triage.ReevaluateForEscalationAsync(
            ticket.CustomerId,
            ticket.CategoryId,
            newPriority,
            fromAgentId,
            escalatedAtUtc,
            ct);

        ticket.Escalate(
            triageResult.Priority,
            triageResult.AssignedAgentId,
            triageResult.DueAtUtc,
            escalatedAtUtc);

        var escalation = new TicketEscalation(
            ticket.Id,
            fromPriority,
            ticket.Priority,
            fromAgentId,
            ticket.AssignedAgentId,
            fromDueAtUtc,
            ticket.DueAtUtc,
            reason,
            escalatedBy,
            escalatedAtUtc);

        await escalations.AddAsync(escalation, ct);
        await unitOfWork.SaveChangesAsync(ct);

        var updatedTicket = await getTicket.HandleAsync(id, ct);
        var history = await ticketQueries.GetEscalationsAsync(id, ct);
        var recordedEscalation = history.FirstOrDefault()
            ?? throw new InvalidOperationException(
                "The saved escalation could not be retrieved.");

        return new EscalateTicketResultDto(
            updatedTicket,
            recordedEscalation);
    }
}