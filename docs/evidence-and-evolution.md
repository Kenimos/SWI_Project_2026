# C01 Engineering Spike

Varianta: **A — Persistence** (issue #1)

Question / unknown:
Umíme uložit `Reservation` přes EF Core do skutečné SQLite databáze, načíst ji zpátky z jiného `DbContext` a dostat stejné hodnoty (včetně stavu a časů)? Funguje celý řetěz balíčky → `DbContext` → migrace → soubor `.db`, nebo narazíme na problém s verzemi (.NET 8 SDK vs. globální `dotnet-ef` 9.0.9)?

What we did:
- Přidali jsme minimální entitu `Reservation` (`Id`, `SpotId`, `UserId`, `StartTime`, `EndTime`, `State`) a enum `ReservationState` (`Draft`, `Confirmed`, `Cancelled`) do `backend/Domain/`.
- Přidali jsme `ParkingDbContext` (`backend/Data/`), stav se ukládá jako text (`HasConversion<string>()`), a zaregistrovali jsme ho v `Program.cs` přes `UseSqlite` s connection stringem z `appsettings.json`.
- Vygenerovali jsme migraci: `dotnet ef migrations add InitialCreate` (složka `backend/Migrations/`).
- Napsali jsme dva xUnit testy (`tests/ParkingReservation.Api.Tests`): každý použije dočasný soubor `.db`, schéma vytvoří přes `Database.Migrate()`, rezervaci uloží v jednom `DbContext` a načte ve druhém, novém.
- Ručně jsme spustili `dotnet ef database update` proti reálnému `parking.db` a prohlédli schéma přes `sqlite3`.

Observed result:
- `dotnet test tests/ParkingReservation.Api.Tests` → `Passed! Failed: 0, Passed: 2, Skipped: 0, Total: 2`.
- Test 1: uložená rezervace (`SpotId=42`, `UserId=7`, `State=Confirmed`, start/end v UTC) se načte v novém kontextu se stejnými hodnotami a `Id > 0` (přidělené DB).
- Test 2: změna stavu `Draft → Confirmed` a `SaveChanges()` se v DB opravdu uloží.
- `dotnet ef database update` proběhl (globální `dotnet-ef` 9.0.9 funguje s EF Core 8.0.10 balíčky), vznikla tabulka `Reservations` (`Id` INTEGER PK AUTOINCREMENT, `SpotId`, `UserId`, `StartTime` TEXT, `EndTime` TEXT, `State` TEXT) a `__EFMigrationsHistory`.
- SQLite ukládá `DateTime` jako TEXT; v testech držíme časy v UTC, aby se hodnoty vrátily přesně.

## C04 — Quality and Measurement Traceability

The supplied assessment form marks C04 as `N/A`: the provided exercise documents do not contain a corresponding C04 quality-and-measurement assignment. No project quality criterion or measurement target is invented here. This is a source-material limitation, not evidence that an unrequested project artifact was omitted.

## C03 — Architecture Evidence

Baseline: v0.2. The selected scenario is OP-03 Confirm Reservation with BR-01, BR-02, BR-05, and the related OP-04 race behavior.

The AS-IS trace, architecture drivers, alternatives, ADR-006, structural/runtime views, ownership, delta, and conformance rule are documented in `docs/architecture-and-decisions.md`. The main unresolved risk is that `SpotLocks` is process-local and therefore not sufficient for multi-instance deployment.

Behaviour evidence: existing C02 tests cover success, overlap, boundary touching, inactive spots, approval, expiry, cancellation, persistence, and same-spot concurrent confirmation. The strict Confirm/Cancel rule now has a focused regression test: a Cancel request that observed `DRAFT` returns `Conflict` when Confirm persisted `CONFIRMED` first.

Architecture conformance check: `bash scripts/check-architecture-conformance.sh`. It has been added but not executed in this phase. Executable test evidence remains blocked by the missing .NET 8 runtime.

## C05 — Detailed Design Verification Evidence

Selected unit: `ReservationService`, focused on `ConfirmReservationAsync` and `CancelReservationAsync`.

Derived verification includes lifecycle success, invalid state, overlap rejection, half-open interval boundaries, inactive spots, authorization failures, persistence of state changes, approval transitions, expiry, cancellation, and the strict Confirm/Cancel race. The detailed design and contract mapping are in `docs/architecture-and-decisions.md`.

Current verification status: compilation succeeded during the focused test attempt, but execution was aborted because the container has .NET runtimes 9.0 and 10.0 and lacks the required .NET 8 runtime. No test result is claimed until the runtime is available.

Decision / what changes because of the result:
- Persistence přes EF Core + SQLite funguje, zůstáváme u tohoto stacku (ADR-001 platí).
- Schéma se spravuje výhradně přes EF migrace (`dotnet ef migrations add ...` / `database update`); `parking.db` se necommituje, `Migrations/` ano.
- Časy ukládáme a porovnáváme vždy v UTC.
- Stav rezervace se ukládá jako text kvůli čitelnosti v DB.
- `SpotId` a `UserId` jsou zatím jen čísla bez cizích klíčů; entity `ParkingSpot` a `User` přibudou, až se tým shodne na doméně. Kontrola překryvu rezervací (common rule) tímto spikem ověřena **není** — zůstává na další iterace (souvisí s vybraným future pressure Q).
