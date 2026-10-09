
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Queries;

public sealed class TicketQueriesTests
{
    [Fact]
    public async Task GetPagedAsync_FiltersByExactPriority()
    {
        await using var fixture = await CreateFixtureAsync();

        fixture.Db.AddRange(
            new TicketBuilder()
                .WithReference("TCK-LOW")
                .WithPriority(TicketPriority.Low)
                .Build(),
            new TicketBuilder()
                .WithReference("TCK-HIGH")
                .WithPriority(TicketPriority.High)
                .Build());

        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Queries.GetPagedAsync(
            new TicketQuery { Priority = TicketPriority.High },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("TCK-HIGH", result.Items[0].Reference);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task GetPagedAsync_CombinesStatusAndPriorityFilters()
    {
        await using var fixture = await CreateFixtureAsync();

        fixture.Db.AddRange(
            new TicketBuilder()
                .WithReference("TCK-MATCH")
                .WithStatus(TicketStatus.InProgress)
                .WithPriority(TicketPriority.High)
                .Build(),
            new TicketBuilder()
                .WithReference("TCK-WRONG-STATUS")
                .WithStatus(TicketStatus.New)
                .WithPriority(TicketPriority.High)
                .Build(),
            new TicketBuilder()
                .WithReference("TCK-WRONG-PRIORITY")
                .WithStatus(TicketStatus.InProgress)
                .WithPriority(TicketPriority.Low)
                .Build());

        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Queries.GetPagedAsync(
            new TicketQuery
            {
                Status = TicketStatus.InProgress,
                Priority = TicketPriority.High
            },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("TCK-MATCH", result.Items[0].Reference);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task GetPagedAsync_SearchesCustomerName()
    {
        await using var fixture = await CreateFixtureAsync();

        fixture.Db.Add(
            new TicketBuilder()
                .WithReference("TCK-CUSTOMER")
                .Build());

        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Queries.GetPagedAsync(
            new TicketQuery { Search = "  Acme Customer  " },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("TCK-CUSTOMER", result.Items[0].Reference);
        Assert.Equal("Acme Customer", result.Items[0].Customer.Name);
    }

    private static async Task<TestFixture> CreateFixtureAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<SupportDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new SupportDbContext(options);

        try
        {
            await db.Database.EnsureCreatedAsync();

            db.Add(new Customer(
                "Acme Customer",
                "acme@example.com",
                null,
                Enum.GetValues<CustomerTier>()[0],
                FixedClock.DefaultNow));

            db.Add(new Category("General", false, false));

            await db.SaveChangesAsync();

            var clock = new FixedClock();
            var queries = new TicketQueries(db, clock);

            return new TestFixture(connection, db, queries);
        }
        catch
        {
            await db.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class TestFixture(
        SqliteConnection connection,
        SupportDbContext db,
        TicketQueries queries) : IAsyncDisposable
    {
        public SupportDbContext Db { get; } = db;

        public TicketQueries Queries { get; } = queries;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
