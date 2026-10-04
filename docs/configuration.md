# Runtime settings

The Api reads the `Payments` sections in `src/IPS.Middleware.Api/appsettings.json` into the existing immutable typed options. Constructor validation runs after host configuration is finalized and before requests are served. Application remains independent of configuration/hosting packages. Settings are fixed for the host lifetime: restart after changes. Normal ASP.NET configuration precedence applies (environment-specific JSON, environment variables and command-line overrides).

| Section | Typed settings | Controls |
|---|---|---|
| Payments:Outgoing:Pacs008 | Pacs008Options | Submission window, ownership, preparation retry delay |
| Payments:Incoming:Processing | IncomingProcessingOptions | Payment window, inline status/reply budgets, ownership, response persistence budget, first follow-up delay |
| Payments:Incoming:Reconciliation | IncomingReconciliationOptions | CBS call timeout, response persistence budget, ownership, reconciliation window, discovery batch, retry delays, repeat interval |
| Payments:Incoming:Replies | IncomingReplyOptions | Total delivery attempts, retry delay, call/persistence/ownership budgets, preparation retry delay |
| Payments:Incoming:Scheduling | InboundSchedulingOptions | Channel capacity, discovery batch/interval, receipt claim duration, registration conflict retry limit |
| Payments:Incoming:Transport | IncomingTransportSettings | Opt-in URLs, routes, pools, timeouts, breakers and separate certificate sources |
| Payments:Signing | Pacs008SigningPolicy | AllowUnsignedInDevelopment, rejected outside Development |

Durations use .NET TimeSpan notation, for example `00:00:20` (20 seconds), `00:15:00` (15 minutes) and `1.00:00:00` (one day). All implemented defaults are listed in appsettings.json. Unit tests and direct library consumers retain the same constructor defaults.

For example, set `Payments__Incoming__Reconciliation__CallTimeout=00:00:15` and `Payments__Incoming__Scheduling__RegistrationMaxAttempts=4` in the environment. Array overrides use indexes, such as `Payments__Incoming__Reconciliation__RetryDelays__0=00:00:45`. Configuration arrays merge by index across providers: overriding index 0 does not remove later default entries. Edit the base JSON when shortening the sequence; an empty base RetryDelays array uses RepeatInterval for every retry.

The reconciliation RetryDelays array selects the delay after the first, second, and subsequent persisted query attempts. RepeatInterval applies after that array is exhausted. Processing.FollowUpDelay controls the first follow-up. RegistrationMaxAttempts applies only to fresh-scope SQL uniqueness/concurrency retries during receipt/payment registration; it never retries a remote payment or reversal call.

Validation rejects nonpositive budgets/counts, queue discovery batches larger than capacity, processing reserves that exhaust the payment window, ownership that cannot cover processing/call plus persistence, invalid retry delays, malformed duration values and unknown keys inside these typed sections. No switch enables unsafe automatic submission/reversal retries or treats reversal acceptance as completion.

Incoming HTTP adapters and their validated settings are implemented below. No polling worker or payment endpoint is enabled by binding settings. Signing remains explicitly opt-in for unsigned Development messages.

Frozen intake deadlines, request data, stored notifications and IPS decisions do not change when configuration changes. The reconciliation cutoff is frozen atomically with the first IPS decision and follow-up obligation. A changed Window applies only to new obligations; it never extends or shortens existing work. Pending due times remain persisted, and subsequent scheduling uses the new retry policy. Keep the default business time limits unless an operational/protocol change is intended.

Reply delivery defaults to two total sends, 200 ms apart after an unresolved response, with a 20 s call timeout, 2 s response-persistence budget, 45 s ownership and 5 s preparation retry. MaxAttempts is frozen with each reply and cannot be replenished by restart or configuration changes. Certificate deferral spends no send attempt. The live adapter uses Microsoft timeouts and circuit breakers without introducing sends outside the SQL attempt budget.

## Incoming transport (Review 004c.1)

`Payments:Incoming:Transport:Enabled` defaults to false. This increment registers adapters only; it starts no receive/processing workers even when enabled. Workflow composition and activation follow in Reviews 2 and 3. Startup does not connect to SQL, migrate the database, or call IPS/CBS.

Settings are bound from finalized host configuration and validated once. Restart after changing settings or certificates. Enabling requires an 8/11-character `ParticipantBic`, a `ConnectionStrings:Middleware` SQL Server connection string with server/database, usable IPS/CBS URLs, IPS signature trust, and XML signing material unless explicit Development unsigned mode is enabled. Production additionally requires an IPS mutual-TLS client certificate. HTTPS is required except loopback HTTP in Development.

Transport paths: `MessagePath` = `Message`; `SubmissionPath` = `/api/ips/pacs008/receive`; `StatusPath` = `/api/ips/payments/status`; `ReversalPath` = `/api/ips/transactions/status/receive`. Leading slashes are removed before resolving against the base URL, preserving its application prefix, as in the source. `IpsVersion` defaults to `1`. `ReceiveTimeout` defaults to 10 seconds.

Both `Ips` and `Cbs` sections expose `BaseUrl`, `ConnectionLimit` (100), `ConnectTimeout` (2 seconds), `RequestTimeout` (20 seconds), `PooledConnectionLifetime` (5 minutes), `PooledConnectionIdleTimeout` (1 minute), and `CircuitBreaker` (`FailureRatio` 0.5, `MinimumThroughput` 10, `SamplingDuration` 30 seconds, `BreakDuration` 5 seconds). IPS uses one receive connection and the remaining limit for replies. CBS shares its pool between submission/status/reversal; dispatcher admission and the two-slot follow-up reservation arrive in Review 3. Settings are per instance. The shorter caller workflow budget still wins.

Microsoft resilience wraps one HTTP attempt with a circuit breaker and timeout. Response buffering occurs inside that pipeline so slow/failing body reads count. There are no HTTP retries, hedging, redirects, cookies or fallback business responses. SQL retains responsibility for retries. Non-success status, content and headers reach the interpreters unchanged; transport/cancellation/circuit-open failures stay exceptions.

Certificate settings have exactly one of `Path` or `Thumbprint`. File sources support PFX/P12 (`Password` optional), PEM certificate/key (`KeyPath` optional, same file by default; `Password` for an encrypted PEM key), and public PEM/DER trust files. Store sources specify `StoreLocation` (default LocalMachine) and `StoreName` (default My). Store loading is read-only; private-key access must be granted to the service identity. No certificates are installed by the host. Windows TLS file keys use temporary named key storage for Schannel, removed on certificate disposal; XML signing uses ephemeral file keys.

Configure sources independently at `SigningCertificate`, `IpsSignatureTrust` (array of pinned IPS signing certificates), `Ips:ClientCertificate`, `Cbs:ClientCertificate`, and `Ips:ServerTrust`/`Cbs:ServerTrust` (CA arrays). Empty server trust uses normal OS trust; configured CAs use custom chain trust without bypassing hostname, validity, EKU or revocation checks. `CheckCertificateRevocation` defaults true; disposable offline simulators explicitly disable it. This switch does not disable chain/hostname validation. Signing requires ECDSA and digital-signature usage; IPS signature trust requires ECDSA public keys and digital-signature usage; TLS client certificates require private keys and client-auth usage when EKU is present. All configured certificates must be currently valid at startup.

Put passwords and SQL credentials in environment/user-secret/deployment secret providers, never tracked appsettings. For example the password key is `Payments__Incoming__Transport__SigningCertificate__Password`. The existing `Payments:Signing:AllowUnsignedInDevelopment` remains the only unsigned switch and is forbidden outside Development.
