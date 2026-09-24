using Microsoft.EntityFrameworkCore.Storage;

namespace UZLLM.Persistence;

public interface ITransactionCoordinator
{
    Task<ITransactionScope> BeginAsync(CancellationToken cancellationToken = default);
}

public interface ITransactionScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}

internal sealed class TransactionCoordinator(FoundationDbContext dbContext) : ITransactionCoordinator
{
    public async Task<ITransactionScope> BeginAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        return new TransactionScope(transaction);
    }
}

internal sealed class TransactionScope(IDbContextTransaction transaction) : ITransactionScope
{
    private bool completed;

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (completed)
        {
            throw new InvalidOperationException("This transaction has already completed.");
        }

        await transaction.CommitAsync(cancellationToken);
        completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!completed)
        {
            await transaction.RollbackAsync();
        }

        await transaction.DisposeAsync();
    }
}
