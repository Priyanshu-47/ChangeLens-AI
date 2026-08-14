# INC-014: API reads from the wrong database after a secrets rotation

> **SYNTHETIC EVALUATION DATA** — this incident is fictional and written for ChangeLens evaluation only.

- **Severity:** SEV-1
- **Service:** acmepay-api
- **Archetype:** Configuration / environment drift
- **Difficulty:** Medium

## Symptom

After a scheduled database credential rotation, `acmepay-api` in the staging
environment started writing payments into the *production* database. Staging test
payments appeared in production dashboards within hours. No code changed.

## Timeline

- 00:00 — Credential rotation updates `ConnectionStrings:Payments` in the secrets store.
- 02:00 — Staging deploy picks up the rotated connection string.
- 08:00 — Support notices test payments (amount $0.01, obvious test cards) in the production dashboard.
- 09:30 — Incident declared; staging API is pointed back at the staging database.
- 10:00 — Root cause: the rotation script wrote the new credential into the production secret entry because both environments share one secret key path with per-environment overrides that silently fell back.

## Resolution

- Rotated the leaked staging-test data out of production.
- Split secret paths per environment with explicit validation: staging fails closed if its connection string resolves to the production host.
- Added a startup guard in the API: refuse to start if the connection string database name does not match the environment name.

## Lessons Learned

- Connection strings are the highest-leverage configuration: a wrong value silently moves writes across environments.
- Validate the environment/database pairing at startup — a 5-line check prevents an entire incident class.
