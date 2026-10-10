using SupportDesk.Domain.Aggregates.Categories;

namespace SupportDesk.Domain.Repositories;

public interface ICategoryRepository
{
    Task<bool> ExistsAsync(int id, CancellationToken ct);

    Task<Category?> GetByIdAsync(int id, CancellationToken ct);
}