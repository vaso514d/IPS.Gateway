# IPS transactions

The middleware records transactions exchanged between the core banking system and IPS.

## Language

**Transaction**: One message of a particular kind and direction, with its current outcome and history. A payment and its return are separate transactions.

**Outgoing transaction**: A transaction started by the core system and sent to IPS. Its client reference is the core system's identifier.

**Incoming transaction**: A transaction received from IPS and passed to the core system. Its core reference is the identifier used when communicating with the core system.

**Status change**: A recorded lifecycle state, its origin, observation time, and optional explanation. A repeated state is still a separate observation.

**Processing step**: An observed technical milestone under the transaction's current state. It does not replace the current outcome.

**Final status**: Accepted, Rejected, NotSent, or ManuallyResolved. This classification describes an outcome; whether later evidence may replace it depends on the workflow.

**Manual review**: An unresolved outcome that requires operator attention. It is not a final status.
