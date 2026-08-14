# AcmePay — ChangeLens demo repository

> ⚠️ **SYNTHETIC EVALUATION DATA.** This is a small, intentionally written demo
> application for ChangeLens development and evaluation. It is not a real product;
> it has never run in production; any resemblance to a real codebase is accidental.
> It exists to give the RAG pipeline realistic source code to ingest: service
> dependencies, external API calls, authentication, database operations,
> retry/timeout logic, and API contracts.

The demo is a payment-orchestration service ("AcmePay") with five projects:

```
AcmePay.sln
└── src/
    ├── AcmePay.Api/            ASP.NET Core API: payments + refunds controllers,
    │                           API-key auth middleware, error handling
    ├── AcmePay.Application/    Command handlers (process/refund), token service,
    │                           API-key validator
    ├── AcmePay.Domain/         Payment / Refund / status / PaymentGatewayException
    ├── AcmePay.External/       Third-party integrations: Stripe-style gateway client
    │                           (retry/timeout/backoff) + payouts client
    └── AcmePay.Infrastructure/ EF Core PaymentDbContext (PostgreSQL), payments
                                repository, resilient executor, resilience config
```

The repository deliberately contains the kinds of changes the golden dataset
(`data/golden-dataset/cases.json`) references: signing-key rotation in the token
service, retry/timeout policy in the gateway client, an API-key auth middleware,
and environment-specific configuration (for example, `appsettings.Staging.json`
tightens resilience settings — classic configuration drift).
