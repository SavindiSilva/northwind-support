using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Repositories;

public sealed class TicketEscalationRepository(SupportDbContext db)
    : ITicketEscalationRepository
{
    public async Task AddAsync(
        TicketEscalation escalation,
        CancellationToken ct)
    {
        await db.Set<TicketEscalation>().AddAsync(escalation, ct);
    }
}