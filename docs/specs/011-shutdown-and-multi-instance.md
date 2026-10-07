# Specification 011: graceful shutdown and multi-instance proofs

Status: specification on codex/payment-initiation, stacked on 010 (bcd19fc) and the earlier unmerged slices 005a-005d, 007a-007c and 008a-008c, 009 (merge approval pending for all of them). The owner approved the specification on 2026-10-07; implemented, review pending.

## Scope

State plainly, with tests, what the service guarantees when an instance is stopped and when several instances share one database, and fix any defect those tests find. This is a proof slice: no new feature and no configuration change unless a test shows a defect. Measured performance is 012.

The design already relies on SQL claims for ownership (a work item is owned until its claim expires), a committed marker before every IPS or core call, and supervised services that stop admitting, drain within a shutdown budget, then cancel and persist what they observed. Existing tests cover the building blocks: claim races at the repository, process kills at each checkpoint, a shutdown that cancels after the budget and leaves the marker, two incoming workers over one journal, and the host shutdown timeout for incoming workers. What is not yet shown end to end is below.

## What is proved today, and what is not

Proved: crash recovery at every checkpoint; a cancelled in-flight send leaves its marker; two incoming workers register one payment, submit once and reply per duplicate; the host deadline covers the incoming drain.

Not proved:
1. Two outgoing instances running recovery at the same time over the same database send each payment to IPS exactly once and deliver each callback exactly once.
2. A graceful stop with work in flight: the in-flight send finishes within the budget, its outcome and callback are committed, no claim is left, and new work is refused; and a stop that outlasts the budget leaves a claim another instance takes over without a second send of an unknown-outcome message other than the flagged possible-duplicate resend.
3. An instance that stops while another keeps running hands its due work over, with no payment stuck until a restart.
4. The host shutdown deadline for the outgoing runtime covers its drain and evidence persistence like the incoming one.
5. Readiness turns Unhealthy as soon as a real runtime starts stopping, and the intake of a payment during a drain stays durable (it answers with the current status, as for any unfinished payment, and another instance or the restarted one completes it).

## Design of the proof

- A test fixture runs two `OutgoingRuntime` instances over one SQL database and one simulated IPS and CBS that record every message, with a controllable pause in the IPS reply.
- Scenarios, each asserted on the IPS simulator's received messages, the CBS callbacks, the payment rows and their claims:
  - **Race:** N payments are accepted but not started; both runtimes' discovery sweeps run; every payment is sent once (one message per payment, not flagged), reaches its final status once, one callback each, no claim left.
  - **Drain:** a send is paused at IPS, the instance stops; admission is refused at once; releasing the pause inside the budget commits the outcome and callback; releasing it after the budget cancels, leaves the marker, and a second instance completes the payment with exactly one flagged resend of the same bytes.
  - **Hand-over:** instance A stops while instance B runs; payments accepted afterwards through A's stored work (or already due) are finished by B without a restart.
  - **Intake during drain:** the HTTP send accepted while the instance drains is stored, answers 504 with its status after the wait, and ends final once.
  - **Timeout:** the configured host shutdown timeout for the outgoing runtime is at least its shutdown budget plus the persistence budgets, as the incoming one is tested.
  - **Readiness:** `/health/ready` is 503 while the runtime drains, through the real host.
- If a scenario fails because of a defect (a double send, a stuck claim, a lost callback, a timeout shorter than the drain), the defect is fixed in this slice with a regression test and recorded in the review; a design change beyond a fix is brought to the owner first.
- No test removes or weakens an existing one. Incoming scenarios are not repeated; the existing two-worker test stays the proof for the incoming side, and a short test is added only if the review finds an incoming gap.

## Acceptance

All scenarios above pass repeatedly (each run three times in a row without a flake), on real SQL with the existing simulators; build, full tests, format, EF model unchanged; independent Standards and Spec reviews; a short statement in `docs/architecture.md` of the shutdown and multi-instance guarantees and their limits.

## Limits

Process-level SIGTERM handling by a hosting platform, load balancer draining and Kubernetes probes are not exercised; the in-process host stop is the unit under test. Ownership relies on the SQL clock agreement of the instances (claims use the instance clock), so clock skew between instances can shorten or extend an ownership window; the existing ownership margin is documented, not changed.

## Implementation notes

- The scenarios run as `OutgoingMultiInstanceTests` with two (or one) real hosts over one database and the fixture's simulated IPS and CBS; instances are separate `WebApplicationFactory` hosts, so each has its own runtime, clock and connection pools. Work that "no instance has started" is seeded through the real intake (a pain.002, which has no pre-send deadline), as an instance that stopped or crashed after intake would leave it.
- No defect was found: every scenario passed, and three further consecutive runs passed. Early failures were the tests' own: with a short IPS request timeout, or 16 sends at once, the simulator (which signs each reply with Java) answered after the timeout, so outcomes were unknown and the next sweep sent the flagged possible-duplicate resend, as designed. The scenarios use budgets inside the validated ordering that leave it room.
- Incoming multi-instance behaviour is not repeated; the existing two-worker test remains its proof.
- After review the scenarios were made to prove more than the first version: the race runs 40 payments through two instances that identify themselves by their protocol version header and requires both to have sent part of the work; the over-budget stop asserts the claim and marker remain; the in-budget stop asserts the callback row is pending and undelivered until the next instance; the intake during a drain asserts which payment is unfinished, that nothing was sent by the draining instance and that the in-flight request completed; readiness is read healthy before the stop and unhealthy after it; the timeout test derives its bound from the configured budgets.
- Limits recorded: the scenarios use pain.002 (the pacs.008 investigation takeover uses the same claim and is proved by the workflow tests); "nothing more arrives" is observed for 750 ms around durable-state checks; the hand-over scenario shows the running instance finishes work accepted after the other stopped, while work the stopped instance held is shown by the over-budget scenario.
