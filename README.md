# Northwind Support Desk

A support ticket management system: raise tickets, search and filter them, assign them to agents,
and move them through their lifecycle.

**.NET 10 Web API** + **React 19 / TypeScript** + **SQL Server**, in one monorepo.

---

## The assignment

You are joining a team that already has this app working. Complete the tasks **in order**.
Full requirements are in **[`ASSIGNMENT_CANDIDATE.md`](ASSIGNMENT_CANDIDATE.md)**.

| # | Task | Done looks like | Effort |
| --- | --- | --- | --- |
| 1 | **Fix a defect, add server-side filtering** | Root cause fixed with a regression test; every list filter applied in the database with a correct count | ~2.5 h |
| 2 | **Ticket escalation & assignment** | Automatic triage on create, escalation with history, SLA status, UI for all of it | ~7 h |
| 3 | **Login and authentication** | Agents sign in; API rejects unauthenticated calls; login UI | ~4 h |
| 4 | **Dockerise the API** *(optional)* | `docker compose up` gives a migrated, seeded API with no baked-in secret | ~1.5 h |

### Guidance

- **Time: 2 days.** Honestly marking a task "not finished" beats rushing all of them.
- **Fork** this repo to your own GitHub account and work there. **Do not push to the original.**
- **AI tools are allowed.** You must understand and be able to defend every line you submit.
- **Commit as you go** and keep the real history. Commit the Task 1 defect fix and the filtering
  separately from each other and from the feature work. A single squashed commit is not accepted.
- **Where the spec is ambiguous**, make a decision, write it down in the README, and move on.
- **Out of scope:** registration, password reset, roles, OAuth/SSO, refresh tokens, MFA,
  notifications, CI, caching, visual redesign. Mention them under *Known limitations* instead.

### Submission

1. Update this **README** with what you built, your design decisions (chosen, rejected and why),
   the defect's root cause, dev login credentials, how to run and test it, AI usage, and known
   limitations. A reviewer must be able to run it without asking you anything.
2. Add an **APPROACH.md** explaining your approach and thought process for each task.
3. Commit both, then send us the **link to your fork**, the **dev login credentials**, and a
   **rough note of time spent** on what. (If the fork is private, add the reviewers as collaborators.)

---

## Quick start

**Prerequisites:** .NET SDK 10 (`global.json`), Node.js 22 (`.nvmrc`; 20.19+ works), Docker.

```bash
cp .env.example .env          # optional, every value has a working default
npm install                   # installs the web app
npm run db:up                 # SQL Server 2022 in Docker on localhost:1433
npm run api                   # terminal 1: migrates, seeds, then serves the API
npm run web                   # terminal 2: React dev server
```

| What | Where |
| --- | --- |
| Web app | http://localhost:5173 |
| API | https://localhost:7043 (also http://localhost:5043) |
| Swagger | https://localhost:7043/swagger |

In Development the API applies migrations and seeds demo data on startup. **Never commit real
credentials.**

### Run with Docker Compose

Prerequisites: Docker Desktop with Docker Compose.

Start SQL Server and the API:

```bash
docker compose up --build -d
```

The first build may take several minutes. SQL Server must become healthy before the API starts. In the Development environment, the API applies EF Core migrations and seeds demo data on startup.

- **API:** http://localhost:5080
- **Swagger UI:** http://localhost:5080/swagger
- **SQL Server:** localhost, port 1433

The API endpoints require authentication. Sign in using the development demo account below, then send the returned token as `Authorization: Bearer <token>` when calling protected endpoints.

**Development demo account**
- Email: `alex.turner@northwind-support.example`
- Password: `DevOnly_Password1!`

These are local-development credentials only. The password can be overridden using `DEVELOPMENT_AUTH_PASSWORD` in `.env`. Do not use these defaults outside local development.

Check container status and logs:

```bash
docker compose ps
docker compose logs --tail=100 api
```

Stop the services while retaining database data:

```bash
docker compose down
```

To remove the SQL Server volume and its stored database data as well, use `docker compose down -v`. **This permanently deletes that local database data.**

The credentials and signing key are for local development only. Replace them with unique secrets before sharing the environment; do not commit `.env` or production secrets.


**Visual Studio 2026:** run `npm run db:up`, open `SupportDesk.sln`, set
**SupportDesk.Presentation** as the startup project, and press F5 on the **https** profile.

## Tests

```bash
npm test                      # dotnet test, then the web tests (Vitest)
```

Both pass on a clean checkout and neither needs a database.

### Docker verification

The following checks were performed during development:

- **Clean database startup:** `docker compose down -v` followed by `docker compose up --build -d` to verify that migrations and demo-data seeding work with a fresh database volume.
- **Health check:** Verified that `http://localhost:5080/health` reports healthy database connectivity.
- **Seed data:** Confirmed that demo tickets are available after startup. Protected ticket endpoints require a valid bearer token.
- **Restore caching:** Temporarily changed a C# source file and rebuilt the image. The `dotnet restore` layer remained cached.
- **Image size:** The runtime image was approximately 455 MB, compared with approximately 1.3 GB for the .NET SDK image.
- **Non-root execution:** Verified that the API container runs as the `app` user rather than root.
- **Web development proxy:** Run `VITE_API_PROXY_TARGET=http://localhost:5080 npm run web` to start the web app against the Compose API. In PowerShell, use `$env:VITE_API_PROXY_TARGET = "http://localhost:5080"` before running `npm run web`.
- **Production migrations:** Production deployments should apply reviewed migrations through a deliberate deployment or release step, rather than relying on automatic application startup migrations. Production secrets should be supplied through managed configuration or a secrets manager.

## Commands

| Command | What it does |
| --- | --- |
| `npm run db:up` / `db:down` / `db:reset` | Start / stop / wipe SQL Server |
| `npm run api` / `npm run web` | Run the API / the React app |
| `npm run build` | Build both |
| `npm run lint` / `typecheck` / `format` | Web lint and types / `dotnet format` |
| `npm run ef -- migrations add <Name>` | Add an EF Core migration |

## API at a glance

| Endpoint | Description |
| --- | --- |
| `GET /api/tickets` | Paged list with server-side `search`, `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `slaStatus`, `sortBy`, and `sortDirection` filters. |
| `GET /api/tickets/{id}` | One ticket |
| `POST /api/tickets` | Create a ticket |
| `POST /api/tickets/{id}/escalate` | Escalate a ticket by one priority level and record the escalation history. |
| `GET /api/tickets/{id}/escalations` | Retrieve a ticket's escalation history, newest first. |
| `PATCH /api/tickets/{id}/status` · `/assignment` | Change status / assignee |
| `GET /api/customers` · `/api/agents` · `/api/categories` | Reference data |

Errors are `ProblemDetails`: `400` validation, `404` not found, `409` conflict or business rule.

**Seed data:** 5 categories, 6 customers (3 Premium), 6 agents (one inactive, one at their limit)
and 40 tickets across every status, priority and SLA state.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| API cannot reach the database | `npm run db:up`, wait ~20 s, check `docker compose ps` |
| Port 1433 already in use | Change the port in `docker-compose.yml` and `.env` |
| Browser warns about the certificate | `dotnet dev-certs https --trust`, or use the web app, which proxies |
| Web app shows "Unable to load tickets" | The API is not running, or `VITE_API_PROXY_TARGET` points at the wrong port |
| SQL Server slow on Apple Silicon | Enable Rosetta in Docker Desktop → Settings → General |

## Ticket escalation history

The `TicketEscalations` table has a composite index on `(TicketId, EscalatedAtUtc)`. The ticket ID supports retrieving history for a specific ticket, while the escalation timestamp supports ordering those records newest first without scanning unrelated tickets' history.

## DEFECT-117: Root cause and fix

The ticket-filter request flow did not correctly trigger a fresh API request when filter state changed, which could leave the ticket list displaying stale results. I corrected the request-triggering logic and added a regression test to verify that changing filters triggers the expected request. Filtering, counting, and pagination are handled server-side so the result count and displayed page remain consistent with the selected filters.

## Design decisions

- **Server-side filtering:** Chosen over fetching all tickets and filtering in the browser so pagination and total counts remain correct and unrelated records are not transferred to the client.
- **Centralised triage and SLA rules:** Kept business rules in the backend and made SLA settings configurable instead of duplicating logic in the UI or hard-coding every threshold.
- **Separate escalation history:** Stored escalation events in `TicketEscalations` rather than keeping only the latest escalation on the ticket, so earlier events remain auditable.
- **JWT authentication:** Chosen for the protected API and React client. A separate session service and refresh-token flow were not introduced because they are outside the assignment scope.
- **Server-derived escalation actor:** The API derives the actor from the authenticated identity instead of trusting an agent ID supplied by the client.
- **Multi-stage Docker build:** Used separate SDK and runtime images to keep build tooling out of the final API image. Docker Compose waits for SQL Server health before starting the API.
- **Image size and clean-start verification:** The built `northwind-support-api:latest` runtime image was approximately **455 MB**, compared with **1.3 GB** for `mcr.microsoft.com/dotnet/sdk:10.0`. I tested startup with a fresh SQL Server volume and verified that migrations and demo-data seeding completed successfully. The API ran as the non-root `app` user, and `/health` reported database connectivity. I also changed a `.cs` source file temporarily and rebuilt the image; the `dotnet restore` layer remained cached while the source copy and publish layers reran.
- **Development-only startup seeding:** Kept automatic migrations and demo-data seeding in Development. Production deployment should use a deliberate migration process and managed secrets.
- **Credential and token security:** Agent passwords are hashed with ASP.NET Core's `IPasswordHasher<T>`. Successful login issues a signed JWT; protected API endpoints require a valid bearer token, and the escalation actor is derived from the authenticated identity. The signing key is supplied through configuration and validated at startup rather than embedded in application code.

### Authentication and security details

- **Token lifetime:** Access tokens expire after **one hour**. The client handles expired or invalid authentication by clearing the stored session and redirecting to login.
- **Token storage:** The frontend stores the JWT in `localStorage`. This keeps the implementation simple for the assignment, but JavaScript running through an XSS vulnerability could access the token. A production design should evaluate stronger session-storage protections and additional security controls.
- **Agent credentials:** Authentication credentials are stored with the `Agent` record because an agent is the identity that signs in and performs support-desk actions. Passwords are hashed using ASP.NET Core's `IPasswordHasher<Agent>`; plaintext passwords are not stored. The inactive-agent check prevents inactive accounts from signing in.
- **Escalation history index:** The composite index on `(TicketId, EscalatedAtUtc)` supports retrieving a ticket's escalation events in timestamp order without scanning unrelated tickets' history.

## AI usage

I used ChatGPT as a development aid throughout the assignment to investigate defects, compare implementation approaches, review code and tests, troubleshoot build and runtime issues, and improve documentation. I used it to support my decisions rather than treating generated code as automatically correct.

- **Workflow that worked:** I described the requirement or failure, reviewed the proposed approach, implemented or adapted the changes, and ran the relevant automated tests. I also manually checked important behaviours, including authentication, ticket filtering, escalation, and Docker startup.
- **Example accepted with minor changes:** [Describe one specific suggestion you accepted, what it did, and any changes you made before using it.]
- **Example changed or rejected:** [Describe a specific suggestion that you changed or rejected, and explain why the original approach did not suit the requirements or existing architecture.]
- **AI-related bug or issue:** [Describe a real issue introduced or identified while using AI assistance, how you detected it, and how you corrected it. If you did not encounter one, state that honestly rather than inventing an example.]
- **Verification:** I ran the .NET and frontend test suites, checked the production frontend build, and manually tested key application flows. I reviewed the resulting changes and used test results and runtime behaviour to verify the implementation rather than relying solely on AI-generated explanations.

## Known limitations

- Authentication does not include registration, password reset, refresh tokens, MFA, OAuth/SSO, or role-based authorization.
- Docker Compose is intended for local development. Its default credentials and signing key must be replaced for any shared or production environment.
- The API currently applies migrations and seeds demo data automatically in Development; production deployment needs a separate migration and secret-management strategy.
- Automated authentication controller tests cover successful login, incorrect passwords, unknown email addresses, inactive agents, and missing credentials. The JWT-protected API flow was also verified manually.
- The frontend stores JWTs in localStorage for this assignment. This is convenient, but injected JavaScript could access a stored token if an XSS vulnerability exists; production use needs further security hardening.
- The frontend lint command reports one non-blocking React Fast Refresh warning.
- Production deployment automation and additional security hardening are outside the current scope.

## Time spent

These are my approximate hours, including implementation, debugging, testing, and related work.

| Activity | Approximate time |
| --- | ---: |
| Understanding the assignment and setting up prerequisites | 2 hours |
| Task 1 — Defect fix and server-side filtering | 2–2.5 hours |
| Task 2 — Ticket triage, SLA tracking, assignment, and escalation | 10–12 hours |
| Task 3 — Authentication and login UI | 4–5 hours |
| Task 4 — Docker setup and verification | 2 hours |
| **Total** | **20–23.5 hours** |