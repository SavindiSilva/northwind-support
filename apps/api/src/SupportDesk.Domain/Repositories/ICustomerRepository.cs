using SupportDesk.Domain.Aggregates.Customers;

namespace SupportDesk.Domain.Repositories;

public interface ICustomerRepository
{
    Task<bool> ExistsAsync(int id, CancellationToken ct);

    Task<Customer?> GetByIdAsync(int id, CancellationToken ct);
}