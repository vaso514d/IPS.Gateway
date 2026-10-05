using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Transactions;

public sealed record TransactionIntakeResult(OutgoingPayment Payment, bool Created);
