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

Decision / what changes because of the result:
- Persistence přes EF Core + SQLite funguje, zůstáváme u tohoto stacku (ADR-001 platí).
- Schéma se spravuje výhradně přes EF migrace (`dotnet ef migrations add ...` / `database update`); `parking.db` se necommituje, `Migrations/` ano.
- Časy ukládáme a porovnáváme vždy v UTC.
- Stav rezervace se ukládá jako text kvůli čitelnosti v DB.
- `SpotId` a `UserId` jsou zatím jen čísla bez cizích klíčů; entity `ParkingSpot` a `User` přibudou, až se tým shodne na doméně. Kontrola překryvu rezervací (common rule) tímto spikem ověřena **není** — zůstává na další iterace (souvisí s vybraným future pressure Q).
