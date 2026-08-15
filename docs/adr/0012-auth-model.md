# ADR-0012: Identity + JWT, RBAC with project-level authorization

- **Status:** Accepted
- **Date:** 2026-08-14

## Context

The MVP needs authentication (users, logins), role-based authorization (Admin/Engineer/Viewer), and project-level isolation (membership decides what a user may see or do). Options ranged from hand-rolled JWT issuance to external IdPs (Cognito/Entra).

## Decision

- **Authentication:** ASP.NET Core Identity (local accounts) issuing JWT bearer tokens. HS256 dev signing key from env; rotation supported; managed secrets on AWS later.
- **Authorization:** role policies (Admin, Engineer, Viewer) + a custom `IAuthorizationHandler` for project membership (`project_members` table, role per project). Enforcement is layered: policy handler **and** project-id filter in every data-layer query.
- **Seam for later:** identity sits behind a boundary (auth endpoints + claims principal only), so Cognito/Entra can replace local Identity at Phase 11 without touching domain code.

## Consequences

- Simple, local, fully testable auth that still demonstrates real patterns (roles, resource-level authz, defense in depth).
- Cost: no SSO/2FA in MVP (documented); JWT revocation is limited (short expiry + rotation acceptable at this scale); password management is on us until the IdP swap.
