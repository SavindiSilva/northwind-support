using Microsoft.Extensions.Options;
using Moq;
using SupportDesk.Application.Contracts.Agents;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Features.Tickets;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;
using SupportDesk.Application.Abstractions;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public sealed class TicketTriageServiceTests
{
    private static readonly DateTime Now =
        new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<ICustomerRepository> _customers = new();
    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly Mock<IAgentQueries> _agents = new();

    private TicketTriageService CreateService(
        CustomerTier tier = CustomerTier.Standard,
        bool forcesCritical = false,
        bool requiresSpecialist = false,
        IReadOnlyList<AgentDto>? agents = null,
        SlaOptions? settings = null)
    {
        _customers
            .Setup(x => x.GetByIdAsync(
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new Customer(
                "Contoso Ltd",
                "support@contoso.example",
                null,
                tier,
                Now));

        _categories
            .Setup(x => x.GetByIdAsync(
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new Category(
                "General",
		requiresSpecialist,
                forcesCritical));

        _agents
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(agents ?? Array.Empty<AgentDto>());

        return new TicketTriageService(
            _customers.Object,
            _categories.Object,
            _agents.Object,
            Options.Create(settings ?? new SlaOptions()));
    }

    private static AgentDto Agent(
        int id,
        int openTickets,
        int maxOpenTickets = 10,
        bool active = true,
        params int[] specializationIds) =>
        new(
            id,
            $"Agent {id}",
            $"agent{id}@example.com",
            active,
            maxOpenTickets,
            openTickets,
            specializationIds
                .Select(id => new CategorySummaryDto(id, $"Category {id}"))
                .ToArray());

    [Fact]
    public async Task TriageAsync_ForcedCriticalCategory_OverridesRequestedPriority()
    {
        var service = CreateService(forcesCritical: true);

        var result = await service.TriageAsync(
            1, 1, TicketPriority.Low, Now, CancellationToken.None);

        Assert.Equal(TicketPriority.Critical, result.Priority);
        Assert.Contains("requires Critical priority", result.PriorityReason);
    }

    [Theory]
    [InlineData(TicketPriority.Critical, 4)]
    [InlineData(TicketPriority.High, 8)]
    [InlineData(TicketPriority.Medium, 24)]
    [InlineData(TicketPriority.Low, 72)]
    public async Task TriageAsync_StandardCustomer_UsesConfiguredSlaWindow(
        TicketPriority priority,
        double expectedHours)
    {
        var service = CreateService();

        var result = await service.TriageAsync(
            1, 1, priority, Now, CancellationToken.None);

        Assert.Equal(expectedHours, result.SlaWindowHours);
        Assert.Equal(Now.AddHours(expectedHours), result.DueAtUtc);
    }

    [Theory]
    [InlineData(TicketPriority.Critical, 2)]
    [InlineData(TicketPriority.High, 4)]
    [InlineData(TicketPriority.Medium, 12)]
    [InlineData(TicketPriority.Low, 36)]
    public async Task TriageAsync_PremiumCustomer_HalvesSlaWindow(
        TicketPriority priority,
        double expectedHours)
    {
        var service = CreateService(tier: CustomerTier.Premium);

        var result = await service.TriageAsync(
            1, 1, priority, Now, CancellationToken.None);

        Assert.Equal(expectedHours, result.SlaWindowHours);
        Assert.Equal(Now.AddHours(expectedHours), result.DueAtUtc);
    }

    [Fact]
    public async Task TriageAsync_PremiumCustomer_RespectsMinimumSlaWindow()
    {
        var settings = new SlaOptions
        {
            MinimumWindowHours = 1
        };
        settings.BaseWindowHours["Critical"] = 1;

        var service = CreateService(
            tier: CustomerTier.Premium,
            settings: settings);

        var result = await service.TriageAsync(
            1, 1, TicketPriority.Critical, Now, CancellationToken.None);

        Assert.Equal(1, result.SlaWindowHours);
        Assert.Equal(Now.AddHours(1), result.DueAtUtc);
    }

    [Fact]
    public async Task TriageAsync_SelectsEligibleAgentWithFewestOpenTickets()
    {
        var service = CreateService(agents:
        [
            Agent(1, 4),
            Agent(2, 1),
            Agent(3, 3)
        ]);

        var result = await service.TriageAsync(
            1, 1, TicketPriority.Medium, Now, CancellationToken.None);

        Assert.Equal(2, result.AssignedAgentId);
    }

    [Fact]
    public async Task TriageAsync_TiedWorkload_SelectsLowestAgentId()
    {
        var service = CreateService(agents:
        [
            Agent(3, 2),
            Agent(1, 2),
            Agent(2, 2)
        ]);

        var result = await service.TriageAsync(
            1, 1, TicketPriority.Medium, Now, CancellationToken.None);

        Assert.Equal(1, result.AssignedAgentId);
    }

    [Fact]
    public async Task TriageAsync_SpecialistCategory_SelectsOnlyQualifiedAgent()
    {
        var service = CreateService(
            requiresSpecialist: true,
            agents:
            [
                Agent(1, 0, specializationIds: [99]),
                Agent(2, 3, specializationIds: [0])
            ]);

        var result = await service.TriageAsync(
            1, 1, TicketPriority.Medium, Now, CancellationToken.None);

        Assert.Equal(2, result.AssignedAgentId);
    }

    [Fact]
    public async Task TriageAsync_InactiveOrFullAgents_AreNotAssigned()
    {
        var service = CreateService(agents:
        [
            Agent(1, 0, active: false),
            Agent(2, 5, maxOpenTickets: 5)
        ]);

        var result = await service.TriageAsync(
            1, 1, TicketPriority.Medium, Now, CancellationToken.None);

        Assert.Null(result.AssignedAgentId);
        Assert.Contains("left unassigned", result.AssignmentReason);
    }

    [Fact]
    public async Task TriageAsync_NoEligibleAgent_ReturnsUnassignedReason()
    {
        var service = CreateService(requiresSpecialist: true);

        var result = await service.TriageAsync(
            1, 1, TicketPriority.Medium, Now, CancellationToken.None);

        Assert.Null(result.AssignedAgentId);
        Assert.Contains("No active specialist", result.AssignmentReason);
    }
}
