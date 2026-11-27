# Parking Reservation System

A system for reserving parking spots. Users find a free parking spot within a parking area, reserve it for a time window, confirm or cancel the reservation, and track the spot's current state (free, reserved, occupied).

---

## 1. Domain

### Resource — what is reserved
The reserved resource is a **parking spot** (`ParkingSpot`), which always belongs to one **parking area** (`ParkingArea`), e.g. a garage floor, a section of an open lot, or a street zone.

### Parking areas (`ParkingArea`)
| Attribute | Description |
|---|---|
| `id`, `name` | identification of the area |
| `address` | address (optional, free text) |
| `type` | `GARAGE`, `OPEN_LOT`, `STREET_ZONE`, `PARK_AND_RIDE` |
| `openingHours` | opening hours as free text (not evaluated yet) |
| `pricingPolicy` | pricing as free text (pricing/payment is out of scope) |

The spots of an area are linked through `ParkingSpot.areaId`. Area availability is always **derived** from its individual spots (`GET /parking-areas/{id}/availability`), never entered manually.

### Parking spots (`ParkingSpot`)
| Attribute | Description |
|---|---|
| `id` | numeric spot identifier (floor/row/number are not modelled yet) |
| `areaId` | link to the parking area |
| `type` | `STANDARD`, `COMPACT`, `DISABLED`, `EV_CHARGING`, `MOTORCYCLE` |
| `state` | stored physical/operational state of the spot (see below) |
| `requiresApproval` | *(v0.2)* reservations must be approved by an Approver (OP-05); currently set only in the database |

**Parking spot states:**

| State | Description |
|---|---|
| `FREE` | the spot is in service and physically free |
| `OCCUPIED` | a vehicle is physically parked on the spot (after check-in of a specific reservation) |
| `OUT_OF_SERVICE` | the spot is administratively blocked (failure, maintenance) |
| `RESERVED` | **not stored**; a derived view: a `CONFIRMED` reservation blocks the spot in the queried interval |

Transitions (BR-06, BR-07): `FREE → OCCUPIED` check-in within the reservation window `[start, end)`, `OCCUPIED → FREE` check-out, `FREE ⇄ OUT_OF_SERVICE` by the Approver. An `OUT_OF_SERVICE` spot is never offered as available and no reservation on it can be confirmed. Confirm, Approve and Cancel do not change the spot state, because availability is computed from reservations over time.

### Reservation
| Attribute | Description |
|---|---|
| `id` | reservation identity |
| `spotId` | reserved parking spot |
| `userId` | user who created the reservation |
| `startTime`, `endTime` | reserved time slot |
| `state` | reservation state (see below) |

**Reservation states:**

| State | Description |
|---|---|
| `DRAFT` | reservation created, not confirmed yet |
| `CONFIRMED` | reservation confirmed, the spot is allocated for the window |
| `CANCELLED` | reservation cancelled by the user or the system |
| `PENDING_APPROVAL` | *(v0.2)* the spot requires approval; the reservation waits for the Approver's decision |
| `REJECTED` | *(v0.2)* the Approver rejected the request (terminal state) |
| `EXPIRED` | *(v0.2)* the approval window elapsed without a decision (terminal state) |
| `COMPLETED` | *(v0.2.1)* after check-out; the reservation no longer blocks the spot (terminal state) |

State operations: `DRAFT → CONFIRMED` (confirmation), for spots with `RequiresApproval` `DRAFT → PENDING_APPROVAL → CONFIRMED / REJECTED / EXPIRED` (OP-05), `→ CANCELLED` (OP-04) and `CONFIRMED → COMPLETED` (check-out, OP-07). The full state diagram is in [docs/specification.md](docs/specification.md) (3b).

### User
A person who creates and manages parking reservations. Roles: **driver** (`Driver`, creates reservations and confirms/cancels only their own) and **parking manager** (`Approver`, approves requests, manages areas and spots, performs check-in/out). Authorization rules are in [docs/specification.md](docs/specification.md), section 0.

---

## 2. Core operations

- **Create reservation** — create a reservation for a spot and a time window.
- **Confirm reservation** — confirm a reservation (`DRAFT → CONFIRMED`).
- **Cancel reservation** — cancel a reservation before it starts (`→ CANCELLED`).
- **Check availability** — availability of one spot or of all spots in an area for a time interval.
- **Manage parking areas/spots** — the Approver creates areas and spots and manages their operational state.
- **Check-in / check-out** — the Approver records the arrival and departure of the vehicle for a confirmed reservation.

---

## 3. Business rules

### Common rule
Two confirmed (`CONFIRMED`) reservations of the same parking spot must not overlap in time.

### Domain-specific rule
A reservation of an `EV_CHARGING` or `DISABLED` spot may be created only by a user whose vehicle actually has the matching type/permit (electric vehicle, disabled parking permit). It is checked when the reservation is created.

---

## 4. External / system boundary

**Notification Service** — an external dependency that informs users about a confirmation, the upcoming end of a reservation, or its cancellation (e-mail / push notification).

---

## 5. CP1 walking skeleton

The minimal end-to-end flow that must actually run:

```
POST /reservations
  → validate (spot exists, interval is valid, user is eligible for the spot type)
  → persist (store the reservation in the database in state DRAFT)
  → return reservation ID
  → automated check (verify that the reservation can be read back from the database with the correct state)
```

---

## 6. Repository structure

```
backend/    ASP.NET Core Web API (.NET 8, EF Core, SQLite)
  Domain/       entities and enums (Reservation, ParkingSpot, ParkingArea, User and their states/types)
  Contracts/    DTOs and operation results (request/response)
  Services/     ReservationService (OP-01..OP-07), IClock, SpotLocks
  Data/         ParkingDbContext
  Migrations/   EF Core migrations
tests/      xUnit tests (ParkingReservation.Api.Tests)
frontend/   empty for now — the frontend is not addressed yet
scripts/    check-architecture-conformance.sh
docs/
  intent-and-change.md          Project Frame, selected future pressure (C01), C08 Change Card
  architecture-and-decisions.md architecture decisions (ADR), C03 and C05 design
  evidence-and-evolution.md     spike, verification, audit and test-case evidence
  specification.md              OP-01..OP-07, BR-01..BR-07, diagrams, baselines v0.1/v0.2/v0.2.1
  REPORT.md                     C06 assessment by another team + our disposition
```

The backend implements the operations of [docs/specification.md](docs/specification.md): Baseline v0.1 (OP-01 Create, OP-02 Check Availability, OP-03 Confirm, OP-04 Cancel), Baseline v0.2 (OP-05 Approve/Reject, states `PENDING_APPROVAL`/`REJECTED`/`EXPIRED` for `ParkingSpot.RequiresApproval = true`) and Baseline v0.2.1 (OP-06 managing areas and spots, OP-07 check-in/check-out).

## 7. Backend — how to run

### Requirements
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- the [`dotnet-ef`](https://learn.microsoft.com/ef/core/cli/dotnet) global tool (for migrations and `dotnet ef database update`): `dotnet tool install --global dotnet-ef`

### Stack
- ASP.NET Core Web API (.NET 8)
- Entity Framework Core + `Microsoft.EntityFrameworkCore.Sqlite` (SQLite database, file `parking.db`)
- Swagger / OpenAPI (`Microsoft.AspNetCore.OpenApi`, `Swashbuckle.AspNetCore`)

### Run

```bash
cd backend
dotnet restore
dotnet run
```

After start-up the API is available at the address printed in the console (e.g. `http://localhost:5xxx`), with Swagger UI at `/swagger` (in the Development environment).

The SQLite connection string is in `backend/appsettings.json` under `ConnectionStrings:DefaultConnection` (`Data Source=parking.db`).

### Database and migrations

The database file `parking.db` is created with (run from `backend/`):

```bash
cd backend
dotnet ef database update
```

A different database (e.g. an isolated test one) can be selected with the same variable the application uses:

```bash
cd backend
ConnectionStrings__DefaultConnection="Data Source=/tmp/test.db" dotnet ef database update
```

After changing entities, add a new migration (`Migrations/` is committed, `parking.db` is not):

```bash
cd backend
dotnet ef migrations add MigrationName
dotnet ef database update
```

### Build / test

```bash
dotnet build backend
dotnet test tests/ParkingReservation.Api.Tests
```

The tests create their own temporary SQLite database through migrations, so they do not need a prior `dotnet ef database update`.

### Trying the endpoints

Run the API on a fixed port so that the examples below match (the default launch profile uses a different port):

```bash
cd backend
dotnet run --urls http://localhost:5299
```

Users have no endpoint yet, so after `dotnet ef database update` create them manually: driver `1` (`Driver`) and manager `9` (`Approver`). The manager then creates an area and a spot through the API. Enums are sent and returned in JSON as text (area `type`: `Garage`, `OpenLot`, `StreetZone`, `ParkAndRide`; spot `type`: `Standard`, `Compact`, `Disabled`, `EvCharging`, `Motorcycle`); a numeric value is accepted too. Send times in UTC (`...Z`):

```bash
sqlite3 backend/parking.db "INSERT INTO Users (Id, Role, IsActive) VALUES (1, 'Driver', 1), (9, 'Approver', 1);"

curl -X POST http://localhost:5299/parking-areas -H "Content-Type: application/json" \
  -d '{"approverUserId":9,"name":"Garage A","type":"Garage"}'
# -> {"id":1}

curl -X POST http://localhost:5299/parking-areas/1/spots -H "Content-Type: application/json" \
  -d '{"approverUserId":9,"type":"Standard"}'
# -> {"id":1,"state":"Free"}
```

**OP-01 Create + OP-02 Check Availability + OP-03 Confirm (without approval) + OP-04 Cancel:**

```bash
curl -X POST http://localhost:5299/reservations \
  -H "Content-Type: application/json" \
  -d '{"userId":1,"spotId":1,"start":"2030-01-01T10:00:00Z","end":"2030-01-01T11:00:00Z"}'

curl "http://localhost:5299/parking-spots/1/availability?start=2030-01-01T10:30:00Z&end=2030-01-01T11:30:00Z"

curl -X POST http://localhost:5299/reservations/1/confirm -H "Content-Type: application/json" -d '{"userId":1}'

curl -X POST http://localhost:5299/reservations/1/cancel -H "Content-Type: application/json" -d '{"userId":1}'
```

**OP-05 Approve/Reject (`ParkingSpot.RequiresApproval = 1`):** Confirm does not lead directly to `Confirmed` but to `PendingApproval`; Approve/Reject then decides.

```bash
curl -X POST http://localhost:5299/parking-areas/1/spots -H "Content-Type: application/json" \
  -d '{"approverUserId":9,"type":"Standard"}'
# RequiresApproval cannot be set through the API yet:
sqlite3 backend/parking.db "UPDATE ParkingSpots SET RequiresApproval = 1 WHERE Id = 2;"

curl -X POST http://localhost:5299/reservations -H "Content-Type: application/json" \
  -d '{"userId":1,"spotId":2,"start":"2030-01-01T10:00:00Z","end":"2030-01-01T11:00:00Z"}'

curl -X POST http://localhost:5299/reservations/2/confirm -H "Content-Type: application/json" -d '{"userId":1}'
# -> {"id":2,"state":"PendingApproval"}

curl -X POST http://localhost:5299/reservations/2/approve -H "Content-Type: application/json" -d '{"approverUserId":9}'
# -> {"id":2,"state":"Confirmed"}
# alternatively: curl -X POST http://localhost:5299/reservations/2/reject -H "Content-Type: application/json" -d '{"approverUserId":9}'
```

**OP-06 out of service / restore and OP-07 check-in / check-out:**

```bash
curl -X POST http://localhost:5299/parking-spots/1/out-of-service -H "Content-Type: application/json" -d '{"approverUserId":9}'
# -> {"id":1,"state":"OutOfService"}; spot availability is now false and Confirm returns spot_inactive
curl -X POST http://localhost:5299/parking-spots/1/restore -H "Content-Type: application/json" -d '{"approverUserId":9}'

# Check-in is possible only within the reservation window [start, end) by the server clock (UTC),
# so for a reservation in 2030 it returns outside_reservation_window.
curl -X POST http://localhost:5299/reservations/2/check-in -H "Content-Type: application/json" -d '{"approverUserId":9}'
curl -X POST http://localhost:5299/reservations/2/check-out -H "Content-Type: application/json" -d '{"approverUserId":9}'
```

## 8. Frontend

The frontend is not addressed yet. The `frontend/` folder is a placeholder; technology and run instructions will be added once the team agrees.

---

## 9. Definition of Done before C02

- [x] team of 3–4 members *(3 members)*
- [x] shared repository
- [x] clear reservation domain
- [x] Resource + Reservation + User
- [x] meaningful Reservation states
- [x] create + confirm/approve + cancel + availability *(implemented in the backend and verified by tests)*
- [x] common overlap rule *(implemented and verified in the Confirm/Availability tests)*
- [x] 1 domain-specific business rule *(defined in the Project Frame)*
- [x] 1 external/system boundary *(defined in the Project Frame — Notification Service)*
- [x] complete Project Frame *(`docs/intent-and-change.md`)*
- [x] 1 Q/C/R/L future pressure *(`docs/intent-and-change.md`, category Q)*
- [x] 1 reviewed and integrated change *(PR #3 `C04_and_others`, reviewed and merged by Robin Tokarský, merge commit `39b934a`)*
- [x] 1 executed engineering spike *(variant A — Persistence, issue #1)*
- [x] spike evidence + decision *(`docs/evidence-and-evolution.md`)*
- [x] defined CP1 walking skeleton

### What is done

- **Team and repository** — 3 members; a shared repository exists and everyone can commit to it.
- **Domain** — the reservation domain (parking spot/area), the Resource/Reservation/User entities and their states are described in the README (section 1) and formally in [`docs/intent-and-change.md`](docs/intent-and-change.md).
- **Project Frame** — fully completed in [`docs/intent-and-change.md`](docs/intent-and-change.md), including the common rule, domain-specific rule, boundary, assumption and unknown.
- **Future pressure** — category Q (Quality/Scale) selected and justified in [`docs/intent-and-change.md`](docs/intent-and-change.md).
- **CP1 walking skeleton** — the end-to-end backend flow is implemented through OP-01 to OP-07 and verified by tests.
- **Backend** — an ASP.NET Core Web API project in `backend/` with EF Core + SQLite, `ParkingDbContext`, reservations, parking areas and spots, and endpoints for OP-01 to OP-07.
- **Engineering spike A (Persistence)** — `Reservation` is stored in SQLite through EF Core, read back and verified by tests; evidence and decision are in [`docs/evidence-and-evolution.md`](docs/evidence-and-evolution.md).
- **`docs/` folder** — contains the Project Frame, specification, architecture, evidence and the project presentation.

### What is still missing

- **Production authorization and scaling** — local User/Role checks are implemented, but external identity and multi-instance database protection are not part of the solution yet.
