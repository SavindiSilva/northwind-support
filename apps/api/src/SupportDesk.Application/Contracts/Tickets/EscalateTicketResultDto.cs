namespace SupportDesk.Application.Contracts.Tickets;

public sealed record EscalateTicketResultDto(
    TicketDetailDto Ticket,
    TicketEscalationDto Escalation);