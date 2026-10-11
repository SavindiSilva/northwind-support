# Implementation Approach

## Overview

I approached the assignment in the required order: fix the existing filtering defect, implement ticket triage and escalation, add authentication, and then Dockerise the API. I kept the existing .NET layered architecture and React/TypeScript frontend rather than introducing a new framework or redesigning the application.

The main principles were to fix defects at their source, keep business rules on the server, make security-sensitive decisions server-controlled, and add regression coverage for the behaviours changed.

## Task 1 — Fix the defect and implement server-side filtering

### Defect root cause

The ticket-list effect in `apps/web/src/features/tickets/hooks/useTicketList.ts` omitted some filter values from its dependencies. Consequently, changing a filter such as status alone did not reliably trigger a new API request, leaving the displayed results out of sync with the selected filters.

I corrected the effect dependencies to include the relevant filters, pagination, and sorting values. Search remains debounced by 300 ms to avoid issuing a request for every keystroke.

### Filtering design

I implemented the filtering in the backend query rather than fetching all tickets and filtering them in the browser.

- Apply the supplied filters together before counting and pagination.
- Support status, priority, category, customer, assigned agent, unassigned-only, and search filters.
- Trim search input and search the supported ticket/customer fields.
- Calculate the total count from the filtered query before applying pagination.
- Preserve sorting and pagination behaviour.
- Keep the query extensible for SLA-status filtering introduced with Task 2.

This keeps the returned page and total count consistent with the active filters and avoids transferring unrelated records to the client.

### Verification

Added a regression test proving that changing status triggers a request, while preserving the search debounce behaviour. Also tested the ticket-list filtering behaviour and the table rendering.

## Task 2 — Ticket triage, SLA tracking, and escalation

### Triage and SLA decisions

I centralised ticket triage and assignment rules in the backend rather than relying on the frontend to determine business outcomes.

- Configure base SLA windows by priority.
- Apply the configured premium-customer multiplier and minimum SLA window.
- Respect category rules for specialist assignment and categories that force critical priority.
- Select an eligible specialist when required; otherwise use the assignment strategy based on the least-open workload.
- Derive SLA status from the ticket's deadline and the configured at-risk threshold.

The SLA values are configuration-driven so they can be adjusted without changing the business logic.

### Escalation and history

Escalation raises a ticket by one priority level and re-runs the relevant triage logic. Each successful escalation records a history entry so previous escalation events can be retrieved separately from the current ticket state.

I used a dedicated `TicketEscalations` table with a composite index on `(TicketId, EscalatedAtUtc)` to support retrieving a ticket's history in chronological order, newest first. The endpoint validates missing tickets and invalid or conflicting operations rather than silently accepting them.

The escalation actor is derived from the authenticated user's identity on the server, not trusted from a client-supplied actor field.

### Error handling

I retained the API's Problem Details error format and used appropriate responses for validation errors, missing resources, and conflicting or disallowed business operations.

## Task 3 — Authentication

### Authentication design

I implemented agent login using password hashing and JWT bearer authentication.

- Store password hashes rather than plaintext passwords.
- Verify credentials through the ASP.NET Core password hasher.
- Reject invalid credentials and inactive agents without revealing which condition failed.
- Issue a signed JWT with an expiry and the authenticated agent's identity.
- Validate the token's signature, issuer, audience, and lifetime.
- Apply a fallback authorization policy so API endpoints require authentication unless explicitly allowed anonymously.
- Expose an authenticated endpoint for retrieving the current user's details.
- Integrate login, token handling, and logout into the React frontend.

I used JWT bearer authentication instead of introducing a separate session service or refresh-token system because the assignment requires a straightforward authenticated API and explicitly excludes refresh tokens and more advanced identity features.

The signing key and development password are configuration values, not credentials baked into application source. The development seed process supplies or backfills password hashes for the demo agents.

### Verification

Automated controller tests cover successful login, incorrect passwords, unknown email addresses, inactive agents, and missing credentials. Manually verified that a valid demo login returns a token, the token grants access to protected endpoints, unauthenticated requests return `401 Unauthorized`, and an inactive agent cannot log in. The frontend authentication tests and production build also passed.

## Task 4 — Dockerise the API

I added a multi-stage Dockerfile using the .NET 10 SDK for restore and publish and the smaller ASP.NET runtime image for execution. This keeps build tooling out of the runtime image.

The Docker Compose setup runs SQL Server and the API as separate services. It waits for SQL Server's health check before starting the API and uses the Compose service name for the container-to-container database connection.

The API runs in Development mode in this local setup so that existing startup logic applies migrations and seeds demo data. Database credentials, the development password, and the JWT signing key can be overridden through environment configuration.

I excluded local environment files, build output, dependencies, and other unnecessary files from the Docker build context.

### Verification

Successfully built the API image, started both containers, confirmed SQL Server was healthy, logged in through the containerised API, and retrieved category data using the returned bearer token.

- Confirmed the API runs as the non-root app user.

- Confirmed /health reports database connectivity without authentication.

- Verified migrations and demo-data seeding after removing the database volume.

- Compared the approximately 455 MB runtime image with the 1.3 GB SDK image.

- Temporarily changed a C# source file and rebuilt the image; the dotnet restore layer remained cached while the source-copy and publish layers reran.

## Cross-cutting decisions

- **Preserve the existing architecture:** extend the existing domain, application, infrastructure, and presentation layers rather than bypassing them.
- **Keep filtering and business rules server-side:** the API remains responsible for query semantics, triage, SLA calculations, escalation, and authentication.
- **Make behaviour testable:** add focused regression and unit tests around changed behaviour, then run the backend and frontend test suites.
- **Keep configuration external:** use configuration for SLA values, database connection details, and authentication secrets.
- **Prefer explicit errors:** return meaningful HTTP status codes and Problem Details responses instead of silently ignoring invalid operations.

## Testing evidence

The following checks passed during the final verification run:

- Backend solution tests: 69 passed, 0 failed.
- Frontend tests: 15 passed, 0 failed.
- Frontend production build: passed.
- Lint: 0 errors and 1 non-blocking React Fast Refresh warning.
- Docker image build, Compose startup, authentication, and protected-endpoint checks: passed.

## Known limitations

- Authentication supports basic login and JWT expiry; registration, password reset, refresh tokens, MFA, OAuth/SSO, and role-based authorization are not implemented.
- The Docker Compose configuration is intended for local development. Its default credentials and signing key must not be used in production.
- The Docker setup exposes SQL Server on the host for local development and uses a persistent named volume. Removing that volume deletes the local database data.
- The development API applies migrations and seeds data at startup. A production deployment would need a deliberate migration and secret-management strategy.
- The frontend lint command still reports one non-blocking Fast Refresh warning.
- Further production hardening, deployment automation, and broader authentication-specific automated regression coverage remain possible follow-up work.

## Use of AI tools

I used ChatGPT as a development aid to discuss implementation approaches, investigate errors, review design decisions, and improve documentation. I checked suggestions against the existing codebase, ran automated tests and builds, and manually exercised important API and authentication flows. AI assistance did not replace the need to understand the submitted implementation or verify its behaviour.
