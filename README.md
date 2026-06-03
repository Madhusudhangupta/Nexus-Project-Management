# NexusPM — Enterprise Project Management Platform

[![CI/CD](https://github.com/madhusudhangupta/nexuspm/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/madhusudhangupta/nexuspm/actions)

A production-grade, multi-tenant SaaS project management API built with **.NET 10**, **Clean Architecture**, and **CQRS**. Comparable in scope to Jira/Asana/Monday.com.

## What is NexusPM?

NexusPM is a highly scalable, real-time Project Management SaaS backend designed for enterprise teams. It provides the foundational API to build a fully functional Agile tracking tool.

**Key Product Features:**

- **Multi-Tenant Workspaces:** Organizations can create their own isolated workspaces. Data is strictly siloed per tenant using JWT claims, Entity Framework query filters, and Postgres Row-Level Security.
- **Projects & Sprints:** Organize work into customizable projects and time-boxed sprints.
- **Advanced Task Management:** Support for Kanban-style task items, story point estimations, priority tagging, and sub-task hierarchies (up to 3 levels deep).
- **Real-Time Collaboration:** Powered by **SignalR** and a **Redis Backplane**, task updates and board movements are pushed to all connected clients instantly across multiple server instances.
- **Smart Dependencies:** Built-in logic to link tasks (e.g., Task A blocks Task B) with circular dependency prevention.
- **Asynchronous Background Processing:** Uses **RabbitMQ** for event-driven, out-of-band workloads like triggering email notifications upon task assignment without blocking the main API thread.

---

## Quick Start (5 minutes)

**Prerequisites:** Docker Desktop 4.x+, .NET 10 SDK

```bash
# 1. Clone
git clone https://github.com/madhusudhangupta/nexuspm
cd nexuspm

# 2. Start infrastructure (PostgreSQL, Redis, RabbitMQ, Seq)
docker compose up -d postgres redis rabbitmq seq

# 3. Run the API
dotnet run --project src/NexusPM.API

# 4. Open Swagger UI
open http://localhost:5000/swagger

# 5. Run all tests
dotnet test
```

---

## How to Use NexusPM (User Workflows)

Once the application is running (see Quick Start), you can interact with the API using the Swagger UI at `http://localhost:5000/swagger` or via tools like Postman and cURL.

Here is a detailed, step-by-step flow to test the core features of the system:

### 1. Register & Authenticate
Before you can do anything, you need an identity. Registration automatically provisions your first Workspace!
- **Register:** `POST /api/v1/auth/register`
  ```json
  {
    "email": "user@company.com",
    "password": "SecurePassword123!",
    "displayName": "John Doe",
    "workspaceName": "Engineering Department"
  }
  ```
- **Login:** `POST /api/v1/auth/login` with the same credentials to receive your JWT (`accessToken`) and `refreshToken`.
- **Authenticate in Swagger:** Click the green **"Authorize"** button at the top of the Swagger page. Enter `Bearer <your_access_token>` and click Authorize. 

### 2. Prepare Your Workspace
Your first Workspace was created automatically during registration!
- Look at the JSON response from your Login or Registration request. Keep track of the `workspaceId` returned (e.g., `wid = 1`), as you need it in the URL for all future API calls.
- *(Optional)* **Create Additional Workspaces:** `POST /api/v1/workspaces`

### 3. Build Your Project & Invite Team
- **Create Project:** `POST /api/v1/workspaces/{wid}/projects`
  ```json
  {
    "name": "Q3 Roadmap",
    "key": "Q3R"
  }
  ```
- **Invite Member (Optional):** `POST /api/v1/workspaces/{wid}/members/invite`
  ```json
  {
    "email": "teammate@company.com",
    "role": "Member"
  }
  ```
  *Note: Thanks to RabbitMQ, this triggers an asynchronous background email to the user.*

### 4. Manage Tasks (Kanban & Agile)
Tasks are the core aggregate in NexusPM. Let's create a hierarchical task structure.
- **Create an Epic/Parent Task:** `POST /api/v1/workspaces/{wid}/projects/{pid}/tasks`
  ```json
  {
    "title": "Migrate Database to PostgreSQL 16",
    "description": "Upgrade the production cluster.",
    "priority": "High",
    "storyPoints": 8
  }
  ```
- **Create a Sub-task:** Send the same payload, but add `"parentTaskId": <id_of_epic>` to nest it. NexusPM enforces a maximum depth of 3 levels.
- **Move Task (Kanban flow):** `PATCH /api/v1/workspaces/{wid}/tasks/{tid}/status`
  ```json
  {
    "status": "In Progress"
  }
  ```
  *Note: Thanks to SignalR and the Redis Backplane, if anyone else is viewing the board via WebSockets, they will see this task move instantly!*

### 5. Advanced Collaboration
- **Add Comments:** `POST /api/v1/workspaces/{wid}/tasks/{tid}/comments`
  ```json
  {
    "text": "I started working on the migration scripts today."
  }
  ```
- **Assign Task:** `PATCH /api/v1/workspaces/{wid}/tasks/{tid}/assign`
  ```json
  {
    "assigneeId": "<user_id_of_teammate>"
  }
  ```
- **Set Dependencies:** You can link tasks (e.g., Task A *blocks* Task B). The Domain model will automatically validate and reject circular dependencies (e.g., A blocks B, B blocks A).

---

## Technology Stack

| Concern       | Technology                                      |
| ------------- | ----------------------------------------------- |
| API Framework | ASP.NET Core 10 Web API                         |
| Architecture  | Clean Architecture + CQRS                       |
| ORM           | Entity Framework Core 10 + Npgsql               |
| Database      | PostgreSQL 16                                   |
| Cache         | Redis 7.2                                       |
| Messaging     | RabbitMQ 3.13                                   |
| Real-time     | SignalR + Redis backplane                       |
| Auth          | JWT (15min) + Refresh Tokens (7d)               |
| Validation    | FluentValidation                                |
| Mediator      | MediatR 12                                      |
| Logging       | Serilog → Console (JSON) + Seq                  |
| Testing       | xUnit + Moq + FluentAssertions + Testcontainers |
| CI/CD         | GitHub Actions                                  |
| Secrets       | Environment Variables                           |
| Containers    | Docker + Docker Compose                         |

---

## Architecture

```
NexusPM.sln
├── src/
│   ├── NexusPM.Domain          # Aggregates, value objects, domain events, repository interfaces
│   ├── NexusPM.Application     # CQRS commands/queries, handlers, validators, pipeline behaviors
│   ├── NexusPM.Infrastructure  # EF Core, PostgreSQL, Redis, RabbitMQ, JWT, Blob Storage
│   ├── NexusPM.API             # ASP.NET Core controllers, SignalR hubs, middleware, DI
│   └── NexusPM.Worker          # Background consumers (RabbitMQ → email/notifications)
└── tests/
    ├── NexusPM.Domain.Tests          # Unit tests — aggregates and value objects
    ├── NexusPM.Application.Tests     # Unit tests — handlers and validators (Moq)
    ├── NexusPM.Infrastructure.Tests  # Integration tests (Testcontainers)
    └── NexusPM.API.Tests             # API integration tests (WebApplicationFactory)
```

### The Dependency Rule

_Source code dependencies must point inward, toward the Domain._

- **`NexusPM.Domain`**: Core of the system. Has **zero dependencies** on external frameworks.
- **`NexusPM.Application`**: Implements business use cases using CQRS. Depends only on the Domain.
- **`NexusPM.Infrastructure`**: Implementation details (DBs, Auth, Third-party APIs). Implements Domain interfaces.
- **`NexusPM.API`**: Thin presentation layer routing HTTP/WebSocket requests to the Application via MediatR.
- **`NexusPM.Worker`**: Background service consuming RabbitMQ messages for out-of-band processing.

---

## Multi-Tenancy & Security

NexusPM is a true multi-tenant application using a **Shared Database, Shared Schema** approach. Every tenant's data lives in the same tables, distinguished by a `workspace_id`. Data isolation is enforced at three layers:

1. **Authentication Layer (JWT):** The `workspace_id` claim is extracted from the JWT and injected into the scoped `ICurrentTenant` service.
2. **Application / ORM Layer (EF Core):** `AppDbContext` dynamically applies a Global Query Filter (`WHERE workspace_id = @tid`) to all tenant entities. Developers don't manually filter by workspace ID.
3. **Database Layer (Row-Level Security):** PostgreSQL Row-Level Security (RLS) policies act as the ultimate backstop.

---

## Core Domain Models (DDD)

We treat the Domain model as a rich, encapsulated object graph. Mutations happen through explicit methods on **Aggregate Roots**.

- **Key Aggregates**: `Workspace`, `Project`, `TaskItem`, `User`.
- **Value Objects**: Immutable concepts without identity (`StoryPoints`, `Priority`). Stored directly in tables using custom `ValueConverter` classes.
- **Domain Events**: When an aggregate mutates (e.g., `TaskItem.ChangeStatus()`), it raises a Domain Event dispatched automatically by `AppDbContext` before saving.

---

## Application Layer Patterns (CQRS)

All workflows are mediated through **MediatR**.

- **Commands (Writes):** Flow through `Controller -> MediatR -> ValidationBehavior -> Command Handler`. Handlers load Aggregates, invoke mutations, and save.
- **Queries (Reads):** Bypass the Domain Model entirely. Queries often use EF Core `.AsNoTracking()` to project straight into flat DTOs for massive performance gains.
- **Pipeline Behaviors:** Used for cross-cutting concerns (e.g., `ValidationBehavior` with FluentValidation, `LoggingBehavior`).

---

## Infrastructure & Integrations

- **Soft Deletion**: Entities implementing `ISoftDeletable` are hidden via EF Core global query filters rather than `DELETE`d.
- **Real-time (SignalR + Redis)**: `TaskHub` and `NotificationHub` use **Redis as a SignalR Backplane** to sync WebSocket connections horizontally across API instances.
- **Messaging (RabbitMQ)**: Integration Events are published to a Topic Exchange. The `NexusPM.Worker` subscribes to these to handle heavy lifting (like sending SendGrid emails) out-of-band.

---

## API Overview

```
POST   /api/v1/auth/register
POST   /api/v1/auth/login
POST   /api/v1/auth/refresh
POST   /api/v1/auth/logout

GET    /api/v1/workspaces
POST   /api/v1/workspaces
GET    /api/v1/workspaces/{id}
...
GET    /api/v1/workspaces/{wid}/tasks/{tid}
PATCH  /api/v1/workspaces/{wid}/tasks/{tid}/status
POST   /api/v1/workspaces/{wid}/tasks/{tid}/comments

GET    /health/live
GET    /health/ready
```

---

## Local Services

| Service       | URL                           | Credentials            |
| ------------- | ----------------------------- | ---------------------- |
| API + Swagger | http://localhost:5000/swagger | —                      |
| RabbitMQ UI   | http://localhost:15672        | nexuspm / dev_password |
| Seq Logs      | http://localhost:8081         | —                      |
| PostgreSQL    | localhost:5432                | nexuspm / dev_password |
| Redis         | localhost:6379                | dev_redis_password     |

---

## Running Tests

```bash
# All tests
dotnet test

# Domain layer only (fast, no infrastructure)
dotnet test tests/NexusPM.Domain.Tests

# Application layer (Moq, no DB)
dotnet test tests/NexusPM.Application.Tests

# Infrastructure integration (requires Docker — uses Testcontainers)
dotnet test tests/NexusPM.Infrastructure.Tests

# API integration (requires Docker — uses Testcontainers + WebApplicationFactory)
dotnet test tests/NexusPM.API.Tests
```

---

## Environment Variables (Production)

Secrets are injected via secure **Environment Variables** at runtime in production.

| Key                                   | Purpose                      |
| ------------------------------------- | ---------------------------- |
| `nexuspm--jwt--signingkey`            | JWT HMAC-SHA256 signing key  |
| `nexuspm--db--connectionstring`       | PostgreSQL connection string |
| `nexuspm--redis--connectionstring`    | Redis connection string      |
| `nexuspm--rabbitmq--connectionstring` | RabbitMQ connection URI      |
| `nexuspm--sendgrid--apikey`           | Email delivery               |

---

## Development Guidelines

1. **Never use `public set` on Aggregate properties.** Use `private set` or `init` and create an explicit method (e.g., `UpdateTitle()`) to mutate state.
2. **Never inject `AppDbContext` into a Controller.** Use MediatR to dispatch a Command or Query.
3. **Always validate incoming data at the boundary.** Create a `FluentValidation.AbstractValidator<TCommand>` for every command.
4. **Think in Events.** If an action requires side effects, don't put them in the Command Handler. Mutate the aggregate, raise a Domain Event, and let Event Handlers process the side effects.

---

## Contributing

1. Branch from `develop`: `git checkout -b feature/your-feature`
2. Write tests first (or alongside)
3. Ensure `dotnet test` passes with no failures
4. Submit a PR against `develop` — CI must be green before merge

---

## License

MIT
