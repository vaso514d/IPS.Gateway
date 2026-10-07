# Runtime settings

The Api reads the `Payments` sections in `src/IPS.Middleware.Api/appsettings.json` into the existing immutable typed options. Constructor validation runs after host configuration is finalized and before requests are served. Application remains independent of configuration/hosting packages. Settings are fixed for the host lifetime: restart after changes. Normal ASP.NET configuration precedence applies (environment-specific JSON, environment variables and command-line overrides).

| Section | Typed settings | Controls |
|---|---|---|
| Payments:Outgoing:Pacs008 | Pacs008Options | Submission window, ownership, preparation retry delay, post-exchange persistence budget |
| Payments:Outgoing:Execution | OutgoingExecutionOptions | Explicit endpoint/worker activation, payment/callback admission, HTTP wait, attempt/shutdown budgets, recovery and observation cadence |
| Payments:Outgoing:Policy | Pacs008Policy | Required enabled currency list, amount bounds, Treasury and indirect participants |
| Payments:Outgoing:Protocol | Pacs008ProtocolProfile | Required IPS BIC and outgoing XML mapping profile |
| Proxy | ProxySettings | Opt-in Proxy Solution management (register, update, remove): participant and Proxy BICs, protocol version, endpoint URL, pool, timeouts, breaker, TLS client certificate and server trust, optional signing certificate |
| Diagnostics | DiagnosticsSettings | Backlog snapshot on/off and interval, certificate expiry warning window, worker stall factor, readiness database timeout |
| Payments:Outgoing:Transport | OutgoingTransportSettings | Opt-in IPS initial-send and CBS callback URLs, paths, pools, timeouts, breakers and separate certificate sources |
| Payments:Outgoing:StatusDelivery | StatusDeliveryOptions | Callback attempts/rounds, retry delays, call/persistence/ownership budgets, discovery |
| Payments:Incoming:Processing | IncomingProcessingOptions | Payment window, inline status/reply budgets, ownership, response persistence budget, first follow-up delay |
| Payments:Incoming:Reconciliation | IncomingReconciliationOptions | CBS call timeout, response persistence budget, ownership, reconciliation window, discovery batch, retry delays, repeat interval |
| Payments:Incoming:Replies | IncomingReplyOptions | Total delivery attempts, retry delay, call/persistence/ownership budgets, preparation retry delay |
| Payments:Incoming:Composition | IncomingCompositionOptions | Receipt continuation delay while awaiting a payment decision (default 1 second) |
| Payments:Incoming:Scheduling | InboundSchedulingOptions | Channel capacity, discovery batch/interval, receipt claim duration, registration conflict retry limit |
| Payments:Incoming:Transport | IncomingTransportSettings | Opt-in URLs, routes, pools, timeouts, breakers and separate certificate sources |
| Payments:Incoming:Workers | IncomingWorkerOptions | Explicit activation, polling delays, CBS reservation, shutdown drain budget |
| Payments:Incoming:Protocol | Pacs008ProtocolProfile | Required IPS BIC and reply mapping profile when workers are enabled |
| Payments:Signing | Pacs008SigningPolicy | AllowUnsignedInDevelopment, rejected outside Development |

Durations use .NET TimeSpan notation, for example `00:00:20` (20 seconds), `00:15:00` (15 minutes) and `1.00:00:00` (one day). All implemented defaults are listed in appsettings.json. Unit tests and direct library consumers retain the same constructor defaults.

For example, set `Payments__Incoming__Reconciliation__CallTimeout=00:00:15` and `Payments__Incoming__Scheduling__RegistrationMaxAttempts=4` in the environment. Array overrides use indexes, such as `Payments__Incoming__Reconciliation__RetryDelays__0=00:00:45`. Configuration arrays merge by index across providers: overriding index 0 does not remove later default entries. Edit the base JSON when shortening the sequence; an empty base RetryDelays array uses RepeatInterval for every retry.

The reconciliation RetryDelays array selects the delay after the first, second, and subsequent persisted query attempts. RepeatInterval applies after that array is exhausted. Processing.FollowUpDelay controls the first follow-up. RegistrationMaxAttempts applies only to fresh-scope SQL uniqueness/concurrency retries during receipt/payment registration; it never retries a remote payment or reversal call.

Validation rejects nonpositive budgets/counts, queue discovery batches larger than capacity, processing reserves that exhaust the payment window, ownership that cannot cover processing/call plus persistence, invalid retry delays, malformed duration values and unknown keys inside these typed sections. No switch enables unsafe automatic submission/reversal retries or treats reversal acceptance as completion.

Incoming HTTP adapters and their validated settings are implemented below. Workers are disabled by default and require their own explicit activation switch; no payment endpoint is exposed. Signing remains explicitly opt-in for unsigned Development messages.

Frozen intake deadlines, request data, stored notifications and IPS decisions do not change when configuration changes. The reconciliation cutoff is frozen atomically with the first IPS decision and follow-up obligation. A changed Window applies only to new obligations; it never extends or shortens existing work. Pending due times remain persisted, and subsequent scheduling uses the new retry policy. Keep the default business time limits unless an operational/protocol change is intended.

Reply delivery defaults to two total sends, 200 ms apart after an unresolved response, with a 20 s call timeout, 2 s response-persistence budget, 45 s ownership and 5 s preparation retry. MaxAttempts is frozen with each reply and cannot be replenished by restart or configuration changes. Certificate deferral spends no send attempt. The live adapter uses Microsoft timeouts and circuit breakers without introducing sends outside the SQL attempt budget.

## Incoming transport (Review 004c.1)

`Payments:Incoming:Transport:Enabled` defaults to false. This increment registers adapters only; it starts no receive/processing workers even when enabled. Workflow composition and activation follow in Reviews 2 and 3. Startup does not connect to SQL, migrate the database, or call IPS/CBS.

Settings are bound from finalized host configuration and validated once. Restart after changing settings or certificates. Enabling requires an 8/11-character `ParticipantBic`, a `ConnectionStrings:Middleware` SQL Server connection string with server/database, usable IPS/CBS URLs, IPS signature trust, and XML signing material unless explicit Development unsigned mode is enabled. Production additionally requires an IPS mutual-TLS client certificate. HTTPS is required except loopback HTTP in Development.

Transport paths: `MessagePath` = `Message`; `AckPath` = `MessageAck` (a `POST` with the `X-MONTRAN-IPS-MessageSeq` header acknowledges a stored unsolicited pacs.002 status report incoming pacs.009 incoming pacs.004 or incoming pain.001); `SubmissionPath` = `/api/ips/pacs008/receive`; `Pacs009SubmissionPath` = `/api/ips/pacs009/receive`; `Pacs004SubmissionPath` = `/api/ips/pacs004/receive`; `Pain001SubmissionPath` = `/api/ips/pain001/receive`; `StatusPath` = `/api/ips/payments/status`; `ReversalPath` = `/api/ips/transactions/status/receive`. Leading slashes are removed before resolving against the base URL, preserving its application prefix, as in the source. `IpsVersion` defaults to `1`. `ReceiveTimeout` defaults to 10 seconds.

Both `Ips` and `Cbs` sections expose `BaseUrl`, `ConnectionLimit` (100), `ConnectTimeout` (2 seconds), `RequestTimeout` (20 seconds), `PooledConnectionLifetime` (5 minutes), `PooledConnectionIdleTimeout` (1 minute), and `CircuitBreaker` (`FailureRatio` 0.5, `MinimumThroughput` 10, `SamplingDuration` 30 seconds, `BreakDuration` 5 seconds). IPS uses one receive connection and the remaining limit for replies. CBS shares its pool between submission/status/reversal; dispatcher admission reserves two CBS slots for follow-up by default. Settings are per instance. The shorter caller workflow budget still wins.

Microsoft resilience wraps one HTTP attempt with a circuit breaker and timeout. Response buffering occurs inside that pipeline so slow/failing body reads count. There are no HTTP retries, hedging, redirects, cookies or fallback business responses. SQL retains responsibility for retries. Non-success status, content and headers reach the interpreters unchanged; transport/cancellation/circuit-open failures stay exceptions.

Certificate settings have exactly one of `Path` or `Thumbprint`. File sources support PFX/P12 (`Password` optional), PEM certificate/key (`KeyPath` optional, same file by default; `Password` for an encrypted PEM key), and public PEM/DER trust files. Store sources specify `StoreLocation` (default LocalMachine) and `StoreName` (default My). Store loading is read-only; private-key access must be granted to the service identity. No certificates are installed by the host. Windows TLS file keys use temporary named key storage for Schannel, removed on certificate disposal; XML signing uses ephemeral file keys.

Configure sources independently at `SigningCertificate`, `IpsSignatureTrust` (array of pinned IPS signing certificates), `Ips:ClientCertificate`, `Cbs:ClientCertificate`, and `Ips:ServerTrust`/`Cbs:ServerTrust` (CA arrays). Empty server trust uses normal OS trust; configured CAs use custom chain trust without bypassing hostname, validity, EKU or revocation checks. `CheckCertificateRevocation` defaults true; disposable offline simulators explicitly disable it. This switch does not disable chain/hostname validation. Signing requires ECDSA and digital-signature usage; IPS signature trust requires ECDSA public keys and digital-signature usage; TLS client certificates require private keys and client-auth usage when EKU is present. All configured certificates must be currently valid at startup.

Put passwords and SQL credentials in environment/user-secret/deployment secret providers, never tracked appsettings. For example the password key is `Payments__Incoming__Transport__SigningCertificate__Password`. The existing `Payments:Signing:AllowUnsignedInDevelopment` remains the only unsigned switch and is forbidden outside Development.

Review 004c.2 binds `Payments:Incoming:Composition:ContinuationDelay` as a positive duration, default `00:00:01`. Callable composition releases receipt ownership and persists this due time before entering CBS processing; another owner or unresolved payment does not cause an inline loop. Once a committed decision exists, an eligible first reply may be made immediately due without shortening an existing reply retry/preparation schedule. Review 004c.3 connects composition to the opt-in hosted workers.

## Incoming workers

Set `Payments:Incoming:Workers:Enabled=true` only alongside complete enabled transport configuration, `ConnectionStrings:Middleware`, and `Payments:Incoming:Protocol:IpsBic`. Protocol settings also accept `ServiceLevelCode` (INST) and `RemittanceMethod` (Uri). Settings/certificates are loaded at startup and require restart to change. The host never creates or migrates its database. Disabled workers do not resolve persistence or remote clients; ordinary liveness still needs no external services.

Every instance runs one receive worker, one concurrent processing dispatcher, one reply/retry dispatcher, and one CBS follow-up scheduler. Each instance may poll independently. SQL claims and rowversion, not dequeue order, decide ownership. Two bounded ID-only channels provide notifications; SQL discovery rebuilds their contents at startup and every Scheduling:DiscoveryInterval (default one second). Processing discovery excludes stored replies; reply discovery includes unsent envelopes. A retry never runs before its stored due time; actual dispatch can be later according to discovery cadence and available capacity.

Worker defaults: MessageDelay=0, EmptyDelay=250ms, ErrorDelay=1s, ShutdownBudget=30s, CbsFollowUpCapacity=2. IPS reserves one connection for receive. Concurrent processing handlers are limited to min(IPS ConnectionLimit - 1, CBS ConnectionLimit - CbsFollowUpCapacity). Initial replies and retries share the IPS send admission pool before claiming receipts. Follow-up uses its reserved CBS capacity. These limits apply per instance; operator deployment limits must accommodate the total.

Shutdown stops receive/admission, drains tracked handlers for ShutdownBudget, then cancels remaining work and awaits bounded response persistence. The host stops roles concurrently and allows the drain plus the largest configured workflow persistence budget. Abandoned SQL claims remain recoverable after expiry. Raw nonempty response bodies, including whitespace, are preserved; unsuccessful HTTP responses use ErrorDelay. A failed receipt commit is retried before another receive is issued. No pacs.008 MessageAck is sent.

## Outgoing status delivery (Review 2c.1)

Payments:Outgoing:StatusDelivery defaults to AttemptsPerRound=3, MaxRounds=0 (unlimited), DelayBetweenAttempts=00:00:05, PauseBetweenRounds=00:10:00, CallTimeout=00:00:20, PersistenceBudget=00:00:02, Ownership=00:00:45, DiscoveryBatch=50 and DiscoveryInterval=00:00:05. Ownership must exceed call plus persistence budgets. Positive durations/counts and nonnegative MaxRounds are validated at startup. Committed attempts and due times survive restart; changing retry settings affects subsequent scheduling without rewriting frozen payloads or resetting attempts. An exhausted record is not rearmed by a status query. These settings register callable workflows only; this review adds no callback HTTP client or outgoing worker.

## Outgoing transport (Review 2c.2a)

Payments:Outgoing:Transport:Enabled defaults false. Enabling validates configuration and loads certificates without starting outgoing endpoints or workers. It requires a participant BIC, usable ConnectionStrings:Middleware (server and database), IPS and CBS base URLs, signature trust and signing credentials or explicit Development unsigned policy. Production requires IPS mutual TLS. Startup does not contact SQL/remote services or migrate a database.

MessagePath defaults to Message; CallbackPath defaults to /api/ips/transactions/status/receive; IpsVersion defaults to 1. The initial IPS send uses UTF-8 XML, participant/version/keep-alive headers, and no possible-duplicate header. Non-success responses reach the journal interpreter unchanged. The callback uses the frozen existing DTO and idempotency key; no transport retry is enabled.

IPS RequestTimeout defaults to 25 seconds and CBS to 20 seconds; each pool defaults to 100 connections, ConnectTimeout 2 seconds, connection lifetime 5 minutes and idle timeout 1 minute. Breaker defaults match incoming settings. Full response buffering stays inside timeout/circuit breaker. Certificate source fields and TLS rules are the same shared validated types described above, but outgoing certificates and pools have independent lifetimes. Enabling both transports allows up to 200 IPS and 200 CBS connections per instance by default (incoming receive/reply pools sum to 100); these are transport ceilings, not throughput claims. Handler admission and callback concurrency are bounded separately in 2c.2b.

Payments:Outgoing:Pacs008:PersistenceBudget defaults to 2 seconds and must be positive and below ownership. One timer starts only after remote completion/failure evidence exists, covering response storage and interpretation saves; a canceled remote call without evidence remains marker-based uncertainty for recovery. Later 2c.2b adds attempt/shutdown ordering validation around this allowance.

## Outgoing execution (Review 2c.2b)

Payments:Outgoing:Execution:Enabled defaults false. Explicit enablement adds POST /api/ips/pacs008/send and GET /api/ips/transactions/status and starts supervised processing, SQL recovery and callback discovery. Transport must also be enabled. Configure Policy:Currencies explicitly (Code, Enabled, optional Minimum/Maximum), optional TreasuryBic and IndirectParticipants, and Protocol:IpsBic. No default currency policy is guessed. Startup validates configuration and certificate sources; it never applies migrations. Enabled discovery naturally queries SQL after startup.

Defaults: Concurrency8, CallbackConcurrency8, ChannelCapacity256, DiscoveryBatch100, DiscoveryInterval1second, StatusPollInterval100ms, HttpWait30seconds, AttemptBudget35seconds and ShutdownBudget30seconds. Positive limits/durations and batch <= channel capacity are required. Admission must fit the independent outgoing IPS/CBS connection limits; IPS request timeout < HTTP wait < attempt budget; attempt + persistence budget < payment ownership; CBS request timeout <= callback workflow call timeout. General host shutdown timeout includes the larger outgoing persistence allowance without reducing incoming requirements.

A new request starts directly when a slot is available. Full admission leaves durable pending work for recovery without unbounded waiting tasks. The HTTP wait includes admission delay. Final outcomes return200; still unresolved at the deadline returns504 with the latest committed snapshot observed (or the committed intake snapshot when observation itself expires). Duplicate reference returns200 immediately with its stored status, without replacing/validating the original request again or starting another attempt. Neither POST response acknowledges callbacks. GET status retains exact-outcome acknowledgement. Caller disconnect stops waiting, not the service-owned payment scope.

SQL is authoritative across restarts and instances. An abandoned submission marker without response becomes Uncertain, with no original resend. pacs.028 investigation is the next capability. Channels are process-local hints; lost/full queues do not lose SQL work. Connection budgets are per instance and additive across incoming and outgoing pools; size total deployment limits deliberately. Defaults are not measured throughput claims.


## Callable outgoing investigation

`Payments:Outgoing:Investigation` binds validated options at startup, without starting a worker. Defaults: first delay 9 seconds; retry delays 30 seconds, 1 minute and 5 minutes, then every 15 minutes; window 24 hours; MaxCycles 0 (window-limited); call timeout 25 seconds; attempt budget 35 seconds; ownership 45 seconds; evidence persistence 2 seconds; preparation retry 1 second; discovery batch 50 and interval 5 seconds. Attempt plus persistence must be less than ownership; call timeout must be less than attempt budget. First delay must be at least 9 seconds and the window at most 24 hours. Changes require restart.

The first investigation identity freezes its absolute deadline from the accepted payment time. Later configuration changes cannot extend it. Recovered abandoned work has no extra first delay. Ordinary uncertainty becomes eligible after the configured first delay. The explicit `AddOutgoingInvestigation` registration provides a callable workflow and fresh recovery/processing scopes; callers supply the protocol trust/certificate adapter, existing transport, persistence and clock. No production invocation or automatic resend is added by this registration.

## Proxy management (009)

`Proxy:Enabled` defaults false; while false the `/api/proxy/*` routes do not exist. Enabling validates the settings and loads the certificates at startup without contacting the Proxy Solution. It requires an 8 or 11 character `ParticipantBic` and `ProxyBic`, an ASCII `ProtocolVersion`, and `Proxy:Endpoint` (HTTPS base URL; loopback HTTP only in Development; production requires a TLS client certificate). `Proxy:SigningCertificate` is optional: without it the acmt.022 is sent unsigned, as in the source. The operations are stateless and use no database. A timeout is returned as HTTP 504 and any other failure to get an answer as 502; the outcome of the operation is then unknown.

## Readiness and diagnostics (010)

`GET /health/ready` returns only `Healthy`, `Degraded` (HTTP 200) or `Unhealthy` (HTTP 503); what failed is logged. It checks the database (reachable and every migration applied, only when an enabled feature uses it), the supervised workers (running, progressing within `WorkerStallFactor` times their period, not draining) and the loaded certificates (Degraded within `CertificateWarning` of expiry, Unhealthy once expired). `/health/live` is unchanged. The meter `IPS.Middleware` carries counters, one HTTP duration histogram and backlog gauges refreshed from SQL every `SnapshotInterval`; attach OpenTelemetry or another listener in the host to export them. `Diagnostics:BacklogSnapshot` turns the SQL snapshot off.
