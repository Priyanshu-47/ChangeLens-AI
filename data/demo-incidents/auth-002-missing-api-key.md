# INC-002: Integration goes down because a client stopped sending X-Api-Key

> **SYNTHETIC EVALUATION DATA** — this incident is fictional and written for ChangeLens evaluation only.

- **Severity:** SEV-3
- **Service:** acmepay-api
- **Archetype:** Authentication / authorization
- **Difficulty:** Easy

## Symptom

A billing integration partner reported 100% `401 api_key_missing` responses from
`POST /api/v1/payments`. No code was deployed on either side. The partner's traffic
was the only consumer affected; all other partners continued to work.

## Timeline

- 12:00 — Partner ticket opened: "All requests failing with 401 api_key_missing".
- 12:20 — AcmePay on-call checks `ApiKeyAuthMiddleware` logs; the requests are arriving without an `X-Api-Key` header.
- 12:45 — Partner confirms their gateway proxy was reconfigured during a load-balancer migration and the header injection rule was lost.
- 13:00 — Partner re-adds the header injection rule; traffic recovers immediately.

## Root Cause

`ApiKeyAuthMiddleware` requires the `X-Api-Key` header for every payment request
and returns `401 api_key_missing` when it is absent. The partner's infrastructure
was responsible for injecting that header at the edge; a load-balancer migration
dropped the injection rule. No AcmePay-side change was involved.

## Resolution

- Partner restored the header injection rule.
- AcmePay added a request-log field `auth.header_present` so missing headers are visible in dashboards before partners escalate.
- Documented the header contract in the partner onboarding guide.

## Lessons Learned

- A 401 with a precise error code (`api_key_missing` vs `api_key_invalid`) let the partner self-diagnose in under an hour.
- Edge-proxy header injection is a shared responsibility — verify it in integration tests that run through the real edge.
