using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Transactions;

public sealed class TransactionIntakeResult
{
    public TransactionIntakeResult(OutgoingPayment payment, bool created)
    {
        Payment = payment;
        Created = created;
    }

    public OutgoingPayment Payment { get; init; }
    public bool Created { get; init; }
}
