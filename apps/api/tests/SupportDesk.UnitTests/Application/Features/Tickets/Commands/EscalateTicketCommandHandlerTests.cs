using Moq;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;
using Xunit;
using SupportDesk.Application.Contracts.Agents;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public sealed class EscalateTicketCommandHandlerTests
{
    private static readonly DateTime Now =
        new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_EscalatesTicketAndRecordsHistory()
    {
        var context = new TicketCommandTestContext();
        context.Clock.Advance(Now - context.Clock.UtcNow);

        var ticket = CreateTicket(TicketPriority.Medium);
        SetTicketId(ticket, 41);
        ticket.ApplyTriage(
            TicketPriority.Medium,
            null,
            Now.AddHours(24),
            Now.AddHours(-1));

        context.ExistingTicket(ticket);

        TicketEscalation? savedEscalation = null;

        var escalationRepository =
            new Mock<SupportDesk.Domain.Repositories.ITicketEscalationRepository>();

        escalationRepository
            .Setup(r => r.AddAsync(
                It.IsAny<TicketEscalation>(),
                It.IsAny<CancellationToken>()))
            .Callback<TicketEscalation, CancellationToken>(
                (e, _) => savedEscalation = e)
            .Returns(Task.CompletedTask);

        var updatedDto = new TicketDetailDto
        {
            Id = 41,
            Reference = ticket.Reference,
            Priority = TicketPriority.High,
            DueAtUtc = Now.AddHours(4),
            Customer = new CustomerContactDto(
                1, "Contoso Ltd", "support@contoso.example", null,
                SupportDesk.Domain.Aggregates.Customers.CustomerTier.Premium),
            Category = new CategoryDto(1, "General", false)
        };

        context.TicketQueries
            .Setup(q => q.GetDetailAsync(41, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedDto);

        context.TicketQueries
            .Setup(q => q.GetEscalationsAsync(41, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => savedEscalation is null
                ? Array.Empty<TicketEscalationDto>()
                : new[]
                {
                    new TicketEscalationDto(
                        1,
                        savedEscalation.TicketId,
                        savedEscalation.FromPriority,
                        savedEscalation.ToPriority,
                        null,
                        null,
                        savedEscalation.FromDueAtUtc,
                        savedEscalation.ToDueAtUtc,
                        savedEscalation.Reason,
                        savedEscalation.EscalatedBy,
                        savedEscalation.EscalatedAtUtc)
                });

        var handler = new EscalateTicketCommandHandler(
            context.Tickets.Object,
            escalationRepository.Object,
            context.TicketQueries.Object,
            context.UnitOfWork.Object,
            context.Clock,
            context.TriageService,
            new SupportDesk.Application.Features.Tickets.Queries.GetTicket.GetTicketQueryHandler(
                context.TicketQueries.Object));

        var result = await handler.HandleAsync(
            41,
            new EscalateTicketRequest("Customer impact increased", "Test user"),
            CancellationToken.None);

        Assert.Equal(TicketPriority.High, ticket.Priority);
        Assert.Equal(Now.AddHours(4), ticket.DueAtUtc);
        Assert.Equal(TicketPriority.High, result.Ticket.Priority);
        Assert.NotNull(result.Escalation);
        Assert.Equal(TicketPriority.Medium, result.Escalation.FromPriority);
        Assert.Equal(TicketPriority.High, result.Escalation.ToPriority);
        Assert.Equal("Customer impact increased", result.Escalation.Reason);
        Assert.Equal("Test user", result.Escalation.EscalatedBy);
        Assert.Equal(Now, result.Escalation.EscalatedAtUtc);
        context.UnitOfWork.Verify(
            u => u.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("abc", "Test user")]
    [InlineData("This is a valid reason", " ")]
    public async Task HandleAsync_InvalidInput_ThrowsValidationException(
        string reason,
        string escalatedBy)
    {
        var context = new TicketCommandTestContext();
        var handler = CreateHandler(context);

        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.HandleAsync(
                41,
                new EscalateTicketRequest(reason, escalatedBy),
                CancellationToken.None));

        context.UnitOfWork.Verify(
            u => u.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_UnknownTicket_ThrowsNotFoundException()
    {
        var context = new TicketCommandTestContext();
        var handler = CreateHandler(context);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(
                999,
                new EscalateTicketRequest("Customer impact increased", "Test user"),
                CancellationToken.None));

        context.UnitOfWork.Verify(
            u => u.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(TicketPriority.Critical, TicketStatus.New)]
    [InlineData(TicketPriority.Medium, TicketStatus.Resolved)]
    [InlineData(TicketPriority.Medium, TicketStatus.Closed)]
    public async Task HandleAsync_CriticalOrCompletedTicket_ThrowsConflictException(
        TicketPriority priority,
        TicketStatus status)
    {
        var context = new TicketCommandTestContext();
        var ticket = CreateTicket(priority, status);
        SetTicketId(ticket, 41);
        context.ExistingTicket(ticket);

        var handler = CreateHandler(context);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.HandleAsync(
                41,
                new EscalateTicketRequest("Customer impact increased", "Test user"),
                CancellationToken.None));

        context.UnitOfWork.Verify(
            u => u.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static EscalateTicketCommandHandler CreateHandler(
        TicketCommandTestContext context)
    {
        var escalations =
            new Mock<SupportDesk.Domain.Repositories.ITicketEscalationRepository>();

        return new EscalateTicketCommandHandler(
            context.Tickets.Object,
            escalations.Object,
            context.TicketQueries.Object,
            context.UnitOfWork.Object,
            context.Clock,
            context.TriageService,
            new SupportDesk.Application.Features.Tickets.Queries.GetTicket.GetTicketQueryHandler(
                context.TicketQueries.Object));
    }

    private static Ticket CreateTicket(
        TicketPriority priority,
        TicketStatus status = TicketStatus.New)
    {
        var ticket = Ticket.Raise(
            "TCK-0041",
            "Something is not working",
            "A description long enough to be realistic.",
            1,
            1,
            priority,
            Now.AddHours(-1));

        if (status != TicketStatus.New)
        {
            ticket.ChangeStatus(status, Now.AddMinutes(-30));
        }

        return ticket;
    }

    private static void SetTicketId(Ticket ticket, int id)
    {
        typeof(SupportDesk.Domain.Common.Entity)
            .GetProperty(nameof(SupportDesk.Domain.Common.Entity.Id))!
            .SetValue(ticket, id);
    }

    [Fact]
    public async Task HandleAsync_KeepsCurrentAgentWhenStillEligible()
    {
        var context = new TicketCommandTestContext();
        var ticket = CreateTicket(TicketPriority.Medium);
        SetTicketId(ticket, 41);
        ticket.ApplyTriage(
            TicketPriority.Medium,
            1,
            Now.AddHours(12),
            Now.AddHours(-1));
        context.ExistingTicket(ticket);

        context.AgentQueries
            .Setup(a => a.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AgentDto>)
            [
                new AgentDto(1, "Alex Turner", "alex@example.com",
                    true, 10, 2, Array.Empty<CategorySummaryDto>())
            ]);

        var escalationRepository =
            new Mock<SupportDesk.Domain.Repositories.ITicketEscalationRepository>();

        var handler = new EscalateTicketCommandHandler(
            context.Tickets.Object,
            escalationRepository.Object,
            context.TicketQueries.Object,
            context.UnitOfWork.Object,
            context.Clock,
            context.TriageService,
            new SupportDesk.Application.Features.Tickets.Queries.GetTicket.GetTicketQueryHandler(
                context.TicketQueries.Object));

        // The handler reads the escalation back after saving.
        escalationRepository
            .Setup(r => r.AddAsync(
                It.IsAny<TicketEscalation>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        context.TicketQueries
            .Setup(q => q.GetEscalationsAsync(41, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new TicketEscalationDto(
                    1, 41, TicketPriority.Medium, TicketPriority.High,
                    null, null, Now.AddHours(12), Now.AddHours(4),
                    "Customer impact increased", "Test user", Now)
            });

        await handler.HandleAsync(
            41,
            new EscalateTicketRequest("Customer impact increased", "Test user"),
            CancellationToken.None);

        Assert.Equal(1, ticket.AssignedAgentId);
    }

    [Fact]
    public async Task HandleAsync_ReassignsInactiveAgentAndRecordsAssignmentChange()
    {
        var context = new TicketCommandTestContext();
        var ticket = CreateTicket(TicketPriority.Medium);
        SetTicketId(ticket, 41);
        ticket.ApplyTriage(
            TicketPriority.Medium,
            1,
            Now.AddHours(12),
            Now.AddHours(-1));
        context.ExistingTicket(ticket);

        context.AgentQueries
            .Setup(a => a.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AgentDto>)
            [
                new AgentDto(1, "Inactive Agent", "inactive@example.com",
                    false, 10, 1, Array.Empty<CategorySummaryDto>()),
                new AgentDto(2, "Available Agent", "available@example.com",
                    true, 10, 2, Array.Empty<CategorySummaryDto>())
            ]);

        TicketEscalation? savedEscalation = null;
        var escalationRepository =
            new Mock<SupportDesk.Domain.Repositories.ITicketEscalationRepository>();

        escalationRepository
            .Setup(r => r.AddAsync(
                It.IsAny<TicketEscalation>(),
                It.IsAny<CancellationToken>()))
            .Callback<TicketEscalation, CancellationToken>(
                (escalation, _) => savedEscalation = escalation)
            .Returns(Task.CompletedTask);

        context.TicketQueries
            .Setup(q => q.GetEscalationsAsync(41, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => savedEscalation is null
                ? Array.Empty<TicketEscalationDto>()
                : new[]
                {
                    new TicketEscalationDto(
                        1, 41,
                        savedEscalation.FromPriority,
                        savedEscalation.ToPriority,
                        null, null,
                        savedEscalation.FromDueAtUtc,
                        savedEscalation.ToDueAtUtc,
                        savedEscalation.Reason,
                        savedEscalation.EscalatedBy,
                        savedEscalation.EscalatedAtUtc)
                });

        var handler = new EscalateTicketCommandHandler(
            context.Tickets.Object,
            escalationRepository.Object,
            context.TicketQueries.Object,
            context.UnitOfWork.Object,
            context.Clock,
            context.TriageService,
            new SupportDesk.Application.Features.Tickets.Queries.GetTicket.GetTicketQueryHandler(
                context.TicketQueries.Object));

        await handler.HandleAsync(
            41,
            new EscalateTicketRequest("Customer impact increased", "Test user"),
            CancellationToken.None);

        Assert.Equal(2, ticket.AssignedAgentId);
        Assert.NotNull(savedEscalation);
        Assert.Equal(1, savedEscalation.FromAgentId);
        Assert.Equal(2, savedEscalation.ToAgentId);
    }
}