# INC-018: Refund requests pile up because cancellation is swallowed

> **SYNTHETIC EVALUATION DATA** — this incident is fictional and written for ChangeLens evaluation only.

- **Severity:** SEV-3
- **Service:** acmepay-api
- **Archetype:** Retry / timeout / resilience
- **Difficulty:** Medium

## Symptom

When the API shut down for a deploy, in-flight refund requests did not abort.
`dotnet` reported "hosting shutdown took longer than expected" and the deploy hung
for 8 minutes. Refund requests issued during the drain window continued calling the
gateway instead of cancelling.

## Timeline

- 22:00 — Deploy starts; graceful shutdown begins.
- 22:01 — Kestrel stops accepting new connections but in-flight refunds keep running.
- 22:05 — Deploy times out waiting for shutdown; process is force-killed.
- 22:06 — A support agent finds refunds that were "still processing" from 22:00 that never completed or failed.
- 22:30 — Root cause: `RefundPaymentHandler` calls the gateway with a `CancellationToken` created inside the handler that is never linked to the request's token.

## Resolution

- Threaded the request `CancellationToken` through `RefundPaymentHandler.HandleAsync` into `StripeGatewayClient.RefundAsync` and `SaveChangesAsync`.
- Verified shutdown drain now completes in under 30 seconds.
- Added a test that cancels the token mid-request and asserts the gateway call is aborted.

## Lessons Learned

- A swallowed `CancellationToken` turns a routine deploy into a hung process and orphaned work.
- Cancellation is part of the API contract, not an implementation detail — it must flow from the controller to the last I/O call.
