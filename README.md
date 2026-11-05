# Emergency Services – City Management Enterprise System

The **Emergency Services** service manages fire, medical, rescue and hazmat incidents for the city:
it receives 911 calls, dispatches response units, tracks incidents until they close, records outcomes, and
publishes weather warnings. It is one of several services in the **City Management Enterprise
System**, and it exchanges data with the other services only through REST APIs behind a shared API gateway.

---

## Table of Contents

1. [Features](#features)
2. [Tech Stack](#tech-stack)
3. [Architecture](#architecture)
4. [Repository Structure](#repository-structure)
5. [Getting Started](#getting-started)
6. [Configuration](#configuration)
7. [Database](#database)
8. [API Reference](#api-reference)
9. [Authentication & Roles](#authentication--roles)
10. [Integrations with Other Services](#integrations-with-other-services)
11. [Frontend](#frontend)
12. [Testing](#testing)
13. [Git Workflow](#git-workflow)
14. [Documentation & Diagrams](#documentation--diagrams)
15. [Out of Scope](#out-of-scope)
16. [Team](#team)

---

## Features

| Area | What it does | Requirements |
|---|---|---|
| **Incidents** | Takes in incidents forwarded from Security's 911 dispatch or created by a Dispatcher; stores type, severity, location, caller details | ES-FR-001, 002 |
| **Status tracking** | Reported → Dispatched → En Route → On Scene → Resolved → Closed, with a timestamped history entry for each change | ES-FR-003 |
| **Live board & map** | Open incidents sorted by severity, then age; shown on an interactive city map; new incidents appear within 15 s | ES-FR-004, 005 |
| **Dispatch** | Assign one or more available units; suggest the nearest unit of the right type; warn about road closures within 1 km | ES-FR-008, 009, 010 |
| **Units & stations** | Manage Engines, Ladders and Ambulances, and the fire/EMS stations they belong to | ES-FR-007, 011 |
| **Hospitals & patients** | ER status and bed counts; suggest the nearest open hospital; record patient transports (no medical history) | ES-FR-012, 013, 014 |
| **After-action reports** | Required before an incident can close: outcome, injuries, deaths, hydrant water used | ES-FR-006 |
| **Weather warnings** | Admins create and expire warnings; alerts are imported from Environment Canada; active warnings are published app-wide | ES-FR-021, 022, 023 |
| **Dashboard & reports** | Incidents by type and status, average response time, death count, budget and staffing; CSV export | ES-FR-024, 025, 026, 027 |

---

## Tech Stack

| Layer | Technology |
|---|---|
| Backend API | C# / ASP.NET Core Web API, **.NET 8 LTS** |
| Data access | Entity Framework Core (code-first, migrations in repo) |
| Database | **SQL Server** (container; one private database for this service) |
| Frontend | **React + TypeScript** (Vite), in the shared city app |
| API docs | Swagger / OpenAPI at `/swagger` |
| Auth | JWT issued by the Registration service |
| Containers | Docker, docker compose |
| Gateway | Shared API gateway (YARP or Ocelot) – the only public entry point |
| Source control / CI | **Git**, with CI running build + tests on every pull request |

---

## Architecture

```
                    ┌────────────────────────────┐
  React frontend ──►│        API Gateway          │◄── Other city services
  (/emergency)      │  /api/emergency/v1/...      │    (Security, Maintenance, Transit,
                    └─────────────┬──────────────┘     Registration, Budget, HR)
                                  │
                    ┌─────────────▼──────────────┐
                    │  Emergency Services API     │
                    │  (ASP.NET Core, container)  │
                    │                             │
                    │  Controllers                │
                    │      ▼                      │
                    │  Services ──► Typed HttpClients ──► gateway ──► other services
                    │      ▼                      │
                    │  EmergencyDbContext (EF)    │
                    └─────────────┬──────────────┘
                                  │
                    ┌─────────────▼──────────────┐
                    │  SQL Server (EmergencyDb)   │  ← only this service connects
                    └────────────────────────────┘
```

Key rules from the shared requirements:

- Each service has its **own container and its own database**; services never read another
  service's database (SH-A01, SH-A02, SH-D01).
- All service-to-service traffic goes over **REST/JSON through the gateway**, using a service account
  token (SH-A02, SH-S07).
- If another service is down, calls **time out after 5 seconds** and fail gracefully instead of crashing (SH-A07).
  Non-critical outbound messages (hydrant usage, hazards, death notices, status updates to
  Security) are queued and **retried in the background** so they never block an incident from closing (ES-FR-016).

Detailed data flow and class diagrams are in [`docs/diagrams/`](docs/diagrams/README.md).

---

## Repository Structure

> Adjust to match your team's repo. Frontend pages live in the shared frontend app (SH-U02).

```
emergency-services/
├── src/
│   └── EmergencyServices.Api/
│       ├── Controllers/          # IncidentsController, UnitsController, ...
│       ├── Services/             # IIncidentService, IDispatchService, ... + implementations
│       ├── Clients/              # Typed HttpClients for other city services
│       ├── BackgroundServices/   # OutboxDispatcher, EnvironmentCanadaImporter
│       ├── Data/
│       │   ├── EmergencyDbContext.cs
│       │   ├── Entities/         # Incident, Unit, Station, Hospital, ...
│       │   ├── Migrations/
│       │   └── Seed/             # Demo seed data
│       ├── Dtos/
│       ├── Program.cs
│       ├── appsettings.json
│       └── Dockerfile
├── tests/
│   ├── EmergencyServices.UnitTests/
│   └── EmergencyServices.IntegrationTests/
├── docs/
│   ├── diagrams/                 # .drawio DFDs + class diagrams, with README
│   └── requirements/             # SRS + shared requirement spreadsheets
├── .env.example
├── .gitignore
├── EmergencyServices.sln
└── README.md

# In the shared frontend repo:
frontend/src/services/emergency/   # Emergency pages and components
```

---

## Getting Started

### Prerequisites

| Tool | Version |
|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 8.0 |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | Latest (with docker compose) |
| [Node.js](https://nodejs.org/) | 20 LTS or newer (frontend only) |
| Git | Any recent version |
| EF Core CLI | `dotnet tool install --global dotnet-ef` |

### Option A – Run the whole city system (recommended)

The main city repo contains a single `docker-compose.yml` that starts the gateway, every service, every
database and the frontend (SH-A04).

```bash
git clone <main-city-repo-url>
cd <main-city-repo>
cp .env.example .env          # fill in the values – see Configuration
docker compose up --build
```

Then check:

- Health: `GET /api/emergency/v1/health` → `200 OK` (SH-A05, ES-NFR-001)
- Swagger: `/api/emergency/swagger`
- Frontend: log in through Registration, then open `/emergency`

### Option B – Run Emergency Services on its own (development)

```bash
# 1. Start only SQL Server
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=<YourStrong!Passw0rd>" \
  -p 1433:1433 --name emergency-sql -d mcr.microsoft.com/mssql/server:2022-latest

# 2. Apply migrations + run the API
cd src/EmergencyServices.Api
dotnet ef database update
dotnet run
```

Swagger opens at `https://localhost:<port>/swagger`. Calls to other services will fail with a
clear timeout error unless those services (or mocks) are running.

---

## Configuration

All configuration comes from **environment variables or `.env`**. Never hard-code or commit secrets
(SH-A06). `.env` is in `.gitignore`; copy `.env.example` and fill it in.

| Variable | Purpose |
|---|---|
| `ConnectionStrings__EmergencyDb` | SQL Server connection string (this service's database only) |
| `Jwt__Issuer`, `Jwt__Audience`, `Jwt__SigningKey` | Validating JWTs issued by Registration |
| `ServiceAccount__ClientId`, `ServiceAccount__Secret` | Service account for service-to-service calls |
| `Services__GatewayBaseUrl` | Base URL of the API gateway |
| `HttpClient__TimeoutSeconds` | Outbound call timeout (default `5`) |
| `EnvironmentCanada__FeedUrl` | Public alert feed for the city |
| `EnvironmentCanada__PollMinutes` | Import interval (default `15`) |

---

## Database

- **SQL Server**, accessed only through **EF Core**. Queries are never built from strings (SH-Q01).
- Migrations are committed to the repo:

  ```bash
  dotnet ef migrations add <MigrationName> -p src/EmergencyServices.Api
  dotnet ef database update            -p src/EmergencyServices.Api
  ```

- **Seed data** loads on startup so the demo works on a fresh `docker compose up` (SH-D03).
- Every table records `CreatedBy`, `CreatedAt`, `UpdatedBy`, `UpdatedAt` (SH-D04).
- Incidents are **never hard-deleted**. Every status change and assignment is kept (ES-NFR-005).
- `Unit` and `Incident` use a `RowVersion` concurrency token, so two dispatchers can't assign the same
  unit at once; the second gets **409 Conflict** (ES-NFR-006).
- Only resident **IDs** are stored, never copies of residents' personal details (SH-D02).

### Main tables

| Table | Purpose |
|---|---|
| `Incident` | Core incident record |
| `IncidentStatusHistory` | Every status change (from, to, by, when) |
| `Unit` / `Station` | Response units and their home stations |
| `IncidentAssignment` | Which units were assigned, when they arrived and when they were released |
| `Hospital` | ER status and available beds |
| `AfterActionReport` | Outcome, injuries, deaths, notes |
| `PatientTransport` | Triage level and destination hospital (no medical history) |
| `HydrantUsage` | Hydrant ID and litres used, sent to Maintenance |
| `WeatherWarning` | Manual and imported warnings |

See [`docs/diagrams/README.md`](docs/diagrams/README.md) for the full model and proposed tables.

---

## API Reference

Base path: **`/api/emergency/v1`** (SH-I01). Full interactive docs are at `/swagger` (SH-I02).

| Method | Path | Purpose | Roles |
|---|---|---|---|
| `GET` | `/incidents?status=&type=` | List incidents (live board) | Dispatcher, Responder, Admin |
| `POST` | `/incidents` | Create incident (manual or from Security) | Dispatcher, Admin, Security service |
| `GET` | `/incidents/{id}` | Incident detail with history and assignments | Dispatcher, Responder, Admin |
| `PATCH` | `/incidents/{id}/status` | Change status | Dispatcher, Responder, Admin |
| `POST` | `/incidents/{id}/assignments` | Assign unit(s) | Dispatcher, Admin |
| `POST` | `/incidents/{id}/report` | File after-action report and close | Responder, Admin |
| `GET` | `/units?status=available&type=&near=` | List units, optionally sorted by distance | Dispatcher, Admin |
| `POST/PUT` | `/units`, `/stations`, `/hospitals` | Manage reference data | Admin |
| `GET` | `/hospitals?near=lat,lng&open=true` | Nearest open hospitals | Dispatcher, Responder, Admin |
| `GET` | `/weather-warnings?active=true` | Active warnings (app-wide banner) | Any logged-in user or service |
| `POST/PUT` | `/weather-warnings` | Manage warnings | Admin |
| `GET` | `/stats?from=&to=` | Dashboard figures | All Emergency roles |
| `GET` | `/health` | Health check | Public |

**Conventions** (SH-I03 – SH-I06)

- Dates are ISO 8601 in UTC, IDs are GUIDs, money is `decimal` in CAD.
- List endpoints support `?page=&pageSize=` (default 25).
- Every error uses one shape, with the correct status code (400, 401, 403, 404, 409, 500):

  ```json
  { "error": "unit_unavailable", "message": "Unit E-12 is already assigned.", "traceId": "..." }
  ```

**Common rules**

| Situation | Response |
|---|---|
| Missing type, severity or location | `400 Bad Request` |
| Wrong role | `403 Forbidden` |
| Invalid status jump (e.g. Reported → Closed) | `409 Conflict` |
| Closing without an after-action report | `409 Conflict` |
| Unit already on an active incident | `409 Conflict` |

---

## Authentication & Roles

Users log in once through the **Registration** service, which issues a signed JWT
(60-minute lifetime) carrying `service:role` claims (SH-S01, SH-S02). This service validates the token on
every request and enforces roles with `[Authorize(Roles = ...)]` (SH-S03).

| Role | Can do |
|---|---|
| `emergency:Admin` | Everything, plus manage units, stations, hospitals, weather warnings, budget and HR requests |
| `emergency:Dispatcher` | Create incidents, assign units, update any incident status, view caller and patient info |
| `emergency:Responder` | View incidents assigned to their unit, update status, file after-action reports |
| `emergency:Viewer` | Read-only dashboard and active weather warnings (e.g. other departments, council) |

Caller and patient details are **only returned to Dispatcher, Responder and Admin**. Viewers and other
services get incidents without those fields (ES-NFR-007).

---

## Integrations with Other Services

| Contract | Direction | What | Endpoint |
|---|---|---|---|
| SH-C01 | Security → Emergency | 911 calls needing Fire/EMS | `POST /api/emergency/v1/incidents` |
| SH-C02 | Emergency → Security | Status updates for forwarded incidents | `PATCH /api/security/v1/calls/{id}/status` |
| SH-C03 | Maintenance → Emergency | Active road closures | `GET /api/maintenance/v1/road-closures?active=true` |
| SH-C04 | Emergency → Maintenance | Hydrant water used | `POST /api/maintenance/v1/hydrant-usage` |
| SH-C05 | Emergency → Maintenance | Utility hazards found on scene | `POST /api/maintenance/v1/utility-reports` |
| SH-C06 | Registration → Emergency | Resident lookup | `GET /api/registration/v1/residents/{id}` |
| SH-C07 | Emergency → Registration | Confirmed death | `POST /api/registration/v1/residents/{id}/deceased` |
| SH-C08 | Budget → Emergency | Budget allocation | `GET /api/budget/v1/allocations/emergency` |
| SH-C09 | Emergency → Budget | Expense statements | `POST /api/budget/v1/expenses` |
| SH-C10 | Emergency → HR | Hire / fire requests | `POST /api/hr/v1/staff-requests` |
| SH-C11 | HR → Emergency | Fire/EMS staff list | `GET /api/hr/v1/staff?department=emergency` |
| SH-C12 | Transit → Emergency | Live bus locations | *Path to be confirmed by Transit* |
| SH-C13 | Emergency → all | Active weather warnings | `GET /api/emergency/v1/weather-warnings?active=true` |
| SH-C14 | Emergency → Transit | Road-restricting incident notices | *Path to be confirmed by Transit* |

---

## Frontend

The Emergency pages live in the shared React + TypeScript app under
`src/services/emergency/` and are served at the `/emergency` route (SH-U01, SH-U02).

- They call **only** the Emergency API, through the shared API client, which attaches the JWT.
- They use the shared component library for a consistent look (SH-U03).
- Navigation only shows pages for roles the user holds (SH-S05).
- Accessibility target is WCAG 2.1 AA: labels on inputs, keyboard navigation, readable contrast (SH-U04).

Main pages: **Incident board**, **Incident map**, **Incident detail** (history, units, nearby
closures and buses), **Units & stations**, **Hospitals**, **Weather warnings**, **Dashboard**.

```bash
cd frontend
npm install
npm run dev
```

---

## Testing

```bash
dotnet test                                    # all tests
dotnet test tests/EmergencyServices.UnitTests  # unit tests only
```

| Type | Covers |
|---|---|
| Unit tests | Status transition rules, nearest unit and hospital sorting, report-required-to-close rule, DTO redaction of caller and patient fields |
| Integration tests | API status codes (200/201, 400, 401/403, 404, 409, 500), concurrent unit assignment, outbound calls go over HTTP only |
| Performance | Creating an incident and assigning a unit each finish in under 2 s for 95% of requests (ES-NFR-008) |

CI runs the build and all tests on **every pull request** (SH-Q03). Test cases are tracked on the
*Test Cases* sheet of `Requirements_SRS.xlsx` (Test ID → Requirement → Result → Implemented By).

---

## Git Workflow

- `main` is always deployable. **Don't commit directly to `main`.**
- Create a branch for each task:

  ```
  feature/ES-FR-008-assign-units
  fix/incident-status-409
  docs/update-dfd
  ```

- Reference requirement IDs in commit messages and pull requests, e.g.
  `Add nearest-hospital endpoint (ES-FR-013)`.
- Open a pull request into `main`. CI must pass and at least one teammate must review before merging.
- Keep `.env`, secrets and `bin/` / `obj/` / `node_modules/` out of Git (see `.gitignore`).

---

## Documentation & Diagrams

| Document | Location |
|---|---|
| Emergency Services SRS | `docs/requirements/Requirements_SRS.xlsx` |
| Shared requirements & integration contracts | `docs/requirements/Shared_Requirement_Spreadsheet.xlsx` |
| Data flow diagrams (Level 0, Level 1) | `docs/diagrams/EmergencyServices_DFDs.drawio` |
| Class diagrams (domain model, API layers) | `docs/diagrams/EmergencyServices_ClassDiagrams.drawio` |
| Diagram guide + open issues | `docs/diagrams/README.md` |
| Live API docs | `/swagger` |

---

## Out of Scope

This service does **not** handle police dispatch, criminal records, fare payments or payroll
(ES-NFR-009). Those belong to Security, Public Transport and Budget/HR.

---

## Team

**Emergency Services Team – City Management Enterprise System**
Conestoga College

