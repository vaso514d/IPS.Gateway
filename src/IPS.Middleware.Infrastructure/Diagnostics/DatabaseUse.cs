namespace IPS.Middleware.Infrastructure.Diagnostics;

// Whether any enabled feature reads or writes the database; readiness and the backlog snapshot need it only then.
public sealed record DatabaseUse(bool Enabled);
