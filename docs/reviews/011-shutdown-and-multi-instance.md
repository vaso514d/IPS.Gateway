# Review 011: graceful shutdown and multi-instance proofs

Branch codex/payment-initiation, stacked on 010 (bcd19fc) and the unmerged slices 005a-005d, 007a-007c, 008a-008c and 009; no merge is approved. On 2026-10-07 the owner approved the [specification](../specs/011-shutdown-and-multi-instance.md). Commit and merge approval are pending.

## Delivered

A test-only slice (`OutgoingMultiInstanceTests`, plus two additive members of the host fixture: the protocol-version header of each submission and a host with setting overrides). No production code changed because no defect was found.

- **Race:** 40 accepted payments, two instances that sign their sends with different protocol versions; both instances sent part of the work, every payment was sent once (no flagged resend), reported once, no claim left, every callback delivered.
- **Stop inside the budget:** the in-flight send finishes, the outcome is committed, new work is refused, readiness is healthy before and unhealthy during the stop, the callback row stays pending until the next instance delivers it once.
- **Stop past the budget:** the claim and the marker remain, the next instance completes the payment with one flagged resend of the same bytes.
- **Hand-over:** work accepted after one instance stopped is finished by the other.
- **Intake during a drain:** stored, not sent by the draining instance, answered 504 with its status, finished once by another instance; the in-flight request completes.
- **Host timeout:** at least the drain budget plus the longest evidence persistence budget.

## Changed existing tests

None changed. `OutgoingHostFixture` gained `IpsVersions` and `Host(overrides)`.

## Verification

Build 0 warnings; format and imports clean; no pending EF changes; the six scenarios passed in three consecutive runs after the review fixes (and in four before); full suite recorded in the presentation.

## Independent reviews

**Standards.** Fixed: leftover diagnostics removed, readiness read before and after the stop, the drain assertions identify the payment and the in-flight response, the timeout bound derives from the options, the fully qualified names, unused alias, disposal of parsed JSON, a guard on the stop. Recorded: fixed 750 ms observation windows remain for "nothing more arrives", with the durable state checked around them.

**Spec.** Fixed: the race now proves both instances took part, the over-budget case asserts the remaining claim, the intake during a drain asserts nothing was sent and which payment is unfinished, the architecture text no longer claims a flagged resend for pacs.008 and no longer mentions claim renewal. Recorded: pacs.008 takeover is not run through two instances (same claim; proved by the workflow tests); the hand-over scenario does not itself show work held by the stopped instance (the over-budget scenario does).

## Findings about behaviour

No defect. Two test-design lessons are worth keeping: an IPS that answers after the request timeout produces an unknown outcome and the flagged resend by design, so slow simulators need budgets with headroom; and a stopping instance that is still draining refuses new work but still stores and answers intake, leaving completion to another instance.
