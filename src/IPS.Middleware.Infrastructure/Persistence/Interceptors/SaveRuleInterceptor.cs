using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace IPS.Middleware.Infrastructure.Persistence.Interceptors;

/// <summary>Runs a synchronous rule against the shared context before every save.</summary>
internal abstract class SaveRuleInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Run(eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Run(eventData);
        return ValueTask.FromResult(result);
    }

    protected abstract void Apply(TransactionDbContext db);

    private void Run(DbContextEventData eventData)
    {
        if (eventData.Context is TransactionDbContext db) Apply(db);
    }
}
