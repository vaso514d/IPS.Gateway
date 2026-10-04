# Runtime settings

The Api reads the `Payments` sections in `src/IPS.Middleware.Api/appsettings.json` into the existing immutable typed options. Constructor validation runs after host configuration is finalized and before requests are served. Application remains independent of configuration/hosting packages. Settings are fixed for the host lifetime: restart after changes. Normal ASP.NET configuration precedence applies (environment-specific JSON, environment variables and command-line overrides).

| Section | Typed settings | Controls |
|---|---|---|
| Payments:Outgoing:Pacs008 | Pacs008Options | Submission window, ownership, preparation retry delay |
| Payments:Incoming:Processing | IncomingProcessingOptions | Payment window, inline status/reply budgets, ownership, response persistence budget, first follow-up delay |
| Payments:Incoming:Reconciliation | IncomingReconciliationOptions | CBS call timeout, response persistence budget, ownership, reconciliation window, discovery batch, retry delays, repeat interval |
| Payments:Incoming:Replies | IncomingReplyOptions | Total delivery attempts, retry delay, call/persistence/ownership budgets, preparation retry delay |
| Payments:Incoming:Scheduling | InboundSchedulingOptions | Channel capacity, discovery batch/interval, receipt claim duration, registration conflict retry limit |
| Payments:Signing | Pacs008SigningPolicy | AllowUnsignedInDevelopment, rejected outside Development |

Durations use .NET TimeSpan notation, for example `00:00:20` (20 seconds), `00:15:00` (15 minutes) and `1.00:00:00` (one day). All implemented defaults are listed in appsettings.json. Unit tests and direct library consumers retain the same constructor defaults.

For example, set `Payments__Incoming__Reconciliation__CallTimeout=00:00:15` and `Payments__Incoming__Scheduling__RegistrationMaxAttempts=4` in the environment. Array overrides use indexes, such as `Payments__Incoming__Reconciliation__RetryDelays__0=00:00:45`. Configuration arrays merge by index across providers: overriding index 0 does not remove later default entries. Edit the base JSON when shortening the sequence; an empty base RetryDelays array uses RepeatInterval for every retry.

The reconciliation RetryDelays array selects the delay after the first, second, and subsequent persisted query attempts. RepeatInterval applies after that array is exhausted. Processing.FollowUpDelay controls the first follow-up. RegistrationMaxAttempts applies only to fresh-scope SQL uniqueness/concurrency retries during receipt/payment registration; it never retries a remote payment or reversal call.

Validation rejects nonpositive budgets/counts, queue discovery batches larger than capacity, processing reserves that exhaust the payment window, ownership that cannot cover processing/call plus persistence, invalid retry delays, malformed duration values and unknown keys inside these typed sections. No switch enables unsafe automatic submission/reversal retries or treats reversal acceptance as completion.

There are currently no live HttpClient adapters, transport base URLs, connection pools or HTTP resilience handlers in the rebuilt host. Its remote-call seams already use the configured Application timeouts; production transport settings will be added when those clients are implemented. No polling worker or payment endpoint is enabled by binding settings. Signing remains explicitly opt-in for unsigned Development messages.

Frozen intake deadlines, request data, stored notifications and IPS decisions do not change when configuration changes. The reconciliation cutoff is frozen atomically with the first IPS decision and follow-up obligation. A changed Window applies only to new obligations; it never extends or shortens existing work. Pending due times remain persisted, and subsequent scheduling uses the new retry policy. Keep the default business time limits unless an operational/protocol change is intended.

Reply delivery defaults to two total sends, 200 ms apart after an unresolved response, with a 20 s call timeout, 2 s response-persistence budget, 45 s ownership and 5 s preparation retry. MaxAttempts is frozen with each reply and cannot be replenished by restart or configuration changes. Certificate deferral spends no send attempt. Microsoft HTTP resilience integration is deferred to the live adapter; it must not introduce sends outside the SQL attempt budget.
