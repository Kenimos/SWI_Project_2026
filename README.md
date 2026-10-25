# Parking Reservation System

Systém pro rezervaci parkovacích míst — umožňuje uživatelům vyhledat volné parkovací místo v rámci parkovací oblasti, zarezervovat jej na časové okno, rezervaci potvrdit/zrušit a sledovat jeho aktuální stav (volné, rezervované, obsazené).

---

## 1. Doména

### Resource — co se rezervuje
Rezervovaným zdrojem je **parkovací místo** (`ParkingSpot`), které je vždy součástí konkrétní **parkovací oblasti** (`ParkingArea`) — např. patro garáže, sekce venkovního parkoviště nebo pouliční zóna.

### Parkovací oblasti (`ParkingArea`)
| Atribut | Popis |
|---|---|
| `id`, `name` | identifikace oblasti |
| `address` / `location` | adresa nebo GPS souřadnice |
| `type` | `GARAGE`, `OPEN_LOT`, `STREET_ZONE`, `PARK_AND_RIDE` |
| `openingHours` | provozní doba (nonstop / omezená) |
| `pricingPolicy` | tarif (hodinová sazba, denní strop) |
| `spots` | seznam parkovacích míst v oblasti |

Kapacita a obsazenost oblasti se vždy **odvozují** agregací stavů jejích jednotlivých míst, nikdy se nezadávají ručně.

### Parkovací místa (`ParkingSpot`)
| Atribut | Popis |
|---|---|
| `id` | identifikace v rámci oblasti (patro, řada, číslo) |
| `areaId` | vazba na parkovací oblast |
| `type` | `STANDARD`, `COMPACT`, `DISABLED`, `EV_CHARGING`, `MOTORCYCLE` |
| `state` | aktuální fyzický/provozní stav místa (viz níže) |

**Stavy parkovacího místa:**

| Stav | Popis |
|---|---|
| `FREE` | místo je volné, lze na něj vytvořit rezervaci |
| `RESERVED` | místo je zamluvené konkrétní rezervací, fyzicky zatím neobsazené |
| `OCCUPIED` | na místě fyzicky stojí vozidlo |
| `OUT_OF_SERVICE` | místo je administrativně blokováno (porucha, údržba) |

Přechody: `FREE → RESERVED` (vznik rezervace) → `OCCUPIED` (příjezd/check-in) → `FREE` (odjezd/check-out nebo zrušení rezervace). `OUT_OF_SERVICE` lze nastavit/zrušit kdykoliv nezávisle na rezervačním toku a místo v tomto stavu se nikdy nenabízí jako dostupné.

### Reservation — rezervace
| Atribut | Popis |
|---|---|
| `id` | identita rezervace |
| `spotId` | rezervované parkovací místo |
| `userId` | uživatel, který rezervaci vytvořil |
| `startTime`, `endTime` | rezervovaný časový slot |
| `state` | stav rezervace (viz níže) |

**Stavy rezervace:**

| Stav | Popis |
|---|---|
| `DRAFT` | rezervace vytvořena, ještě nepotvrzena |
| `CONFIRMED` | rezervace potvrzena, místo je pro dané okno vyhrazeno |
| `CANCELLED` | rezervace zrušena uživatelem nebo systémem |

Stavová operace: `DRAFT → CONFIRMED` (potvrzení rezervace).

### User — uživatel
Osoba, která vytváří a spravuje rezervace parkovacích míst. Základní role: **řidič** (vytváří rezervace) a **správce parkoviště** (spravuje oblasti a místa).

---

## 2. Core operace

- **Create reservation** — vytvoření rezervace na volné místo a časové okno.
- **Confirm reservation** — potvrzení rezervace (`DRAFT → CONFIRMED`).
- **Cancel reservation** — zrušení rezervace (`→ CANCELLED`), uvolnění místa.
- **Check availability** — vyhledání volných míst v oblasti pro daný časový interval a typ místa.
- **Manage parking areas/spots** — Approver vytváří oblasti a místa a spravuje jejich provozní stav.
- **Check-in / check-out** — Approver zaznamená příjezd a odjezd vozidla u potvrzené rezervace.

---

## 3. Business pravidla

### Common rule
Dvě potvrzené (`CONFIRMED`) rezervace téhož parkovacího místa se nesmí časově překrývat.

### Domain-specific rule
Rezervaci místa typu `EV_CHARGING` nebo `DISABLED` smí vytvořit pouze uživatel, jehož vozidlo odpovídající typ/oprávnění skutečně má (elektromobil, ZTP průkaz) — kontroluje se při vytváření rezervace.

---

## 4. External / system boundary

**Notification Service** — externí závislost, která uživatele informuje o potvrzení, blížícím se konci rezervace nebo jejím zrušení (e-mail / push notifikace).

---

## 5. CP1 walking skeleton

Minimální end-to-end tok, který má být reálně spustitelný:

```
POST /reservations
  → validate (existence místa, platnost intervalu, oprávnění k typu místa)
  → persist (uložení rezervace do databáze ve stavu DRAFT)
  → return reservation ID
  → automated check (ověření, že rezervace je čitelná zpět z databáze se správným stavem)
```

---

## 6. Struktura repozitáře

```
backend/    ASP.NET Core Web API (.NET 8, EF Core, SQLite)
  Domain/       entity a enumy (Reservation, ReservationState, ParkingSpot, ParkingSpotType)
  Contracts/    DTOs a výsledky operací (request/response)
  Services/     ReservationService (OP-01..OP-05), IClock, SpotLocks
  Data/         ParkingDbContext
  Migrations/   EF Core migrace
tests/      xUnit testy (ParkingReservation.Api.Tests)
frontend/   zatím prázdné — frontend se zatím neřeší
docs/
  intent-and-change.md          Project Frame + Selected future pressure (C01)
  architecture-and-decisions.md architektonická rozhodnutí (ADR)
  evidence-and-evolution.md     evidence a rozhodnutí z engineering spike
  specification.md              C02: OP-01..OP-04, BR pravidla, diagramy, baseline v0.1/v0.2
```

Backend implementuje všech pět operací podle [docs/specification.md](docs/specification.md) — Baseline v0.1 (OP-01 Create, OP-02 Check Availability, OP-03 Confirm, OP-04 Cancel) i Baseline v0.2, schvalovací proces (OP-05 Approve/Reject, stav `PENDING_APPROVAL`/`REJECTED`/`EXPIRED` pro `ParkingSpot.RequiresApproval = true`).

## 7. Backend — jak spustit

### Požadavky
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [`dotnet-ef`](https://learn.microsoft.com/ef/core/cli/dotnet) globální nástroj (pro migrace a `dotnet ef database update`): `dotnet tool install --global dotnet-ef`

### Použitý stack
- ASP.NET Core Web API (.NET 8)
- Entity Framework Core + `Microsoft.EntityFrameworkCore.Sqlite` (databáze SQLite, soubor `parking.db`)
- Swagger / OpenAPI (`Microsoft.AspNetCore.OpenApi`, `Swashbuckle.AspNetCore`)

### Spuštění

```bash
cd backend
dotnet restore
dotnet run
```

Po spuštění je API dostupné na adrese vypsané v konzoli (např. `http://localhost:5xxx`), Swagger UI na `/swagger` (v Development prostředí).

Connection string na SQLite databázi je v `backend/appsettings.json` pod klíčem `ConnectionStrings:DefaultConnection` (`Data Source=parking.db`).

### Databáze a migrace

Databázový soubor `parking.db` se vytvoří příkazem (spouští se z `backend/`):

```bash
cd backend
dotnet ef database update
```

Po změně entit se přidá nová migrace (`Migrations/` se commituje, `parking.db` ne):

```bash
cd backend
dotnet ef migrations add NazevMigrace
dotnet ef database update
```

### Build / test

```bash
dotnet build backend
dotnet test tests/ParkingReservation.Api.Tests
```

Testy si samy vytvoří dočasnou SQLite databázi přes migrace, takže nepotřebují předchozí `dotnet ef database update`.

### Vyzkoušení endpointů

Po `dotnet ef database update` je potřeba v DB ručně založit aspoň jedno `ParkingSpot` (zatím bez endpointu na jejich vytváření), např.:

```bash
sqlite3 backend/parking.db "INSERT INTO ParkingSpots (Type, IsActive, RequiresApproval) VALUES ('Standard', 1, 0);"
```

**OP-01 Create + OP-02 Check Availability + OP-03 Confirm (bez schvalování) + OP-04 Cancel:**

```bash
curl -X POST http://localhost:5299/reservations \
  -H "Content-Type: application/json" \
  -d '{"userId":1,"spotId":1,"start":"2030-01-01T10:00:00Z","end":"2030-01-01T11:00:00Z"}'

curl "http://localhost:5299/parking-spots/1/availability?start=2030-01-01T10:30:00Z&end=2030-01-01T11:30:00Z"

curl -X POST http://localhost:5299/reservations/1/confirm -H "Content-Type: application/json" -d '{"userId":1}'

curl -X POST http://localhost:5299/reservations/1/cancel -H "Content-Type: application/json" -d '{"userId":1}'
```

**OP-05 Approve/Reject (`ParkingSpot.RequiresApproval = 1`):** Confirm nevede přímo do `Confirmed`, ale do `PendingApproval`; teprve Approve/Reject rozhodne.

```bash
sqlite3 backend/parking.db "INSERT INTO ParkingSpots (Type, IsActive, RequiresApproval) VALUES ('Standard', 1, 1);"

curl -X POST http://localhost:5299/reservations -H "Content-Type: application/json" \
  -d '{"userId":1,"spotId":2,"start":"2030-01-01T10:00:00Z","end":"2030-01-01T11:00:00Z"}'

curl -X POST http://localhost:5299/reservations/2/confirm -H "Content-Type: application/json" -d '{"userId":1}'
# -> {"id":2,"state":"PendingApproval"}

curl -X POST http://localhost:5299/reservations/2/approve -H "Content-Type: application/json" -d '{"approverUserId":9}'
# -> {"id":2,"state":"Confirmed"}
# alternativně: curl -X POST http://localhost:5299/reservations/2/reject -H "Content-Type: application/json" -d '{"approverUserId":9}'
```

## 8. Frontend

Frontend zatím není řešen — složka `frontend/` je připravena jako placeholder, technologie a spuštění doplníme, až se tým shodne.

---

## 9. Definition of Done před C02

- [x] tým 3–4 členové *(3 členi)*
- [x] společný repo
- [x] jasný reservation domain
- [x] Resource + Reservation + User
- [x] meaningful Reservation states
- [x] create + confirm/approve + cancel + availability *(implementováno v backendu a ověřeno testy)*
- [x] common overlap rule *(implementováno a ověřeno v Confirm/Availability testech)*
- [x] 1 domain-specific business rule *(definováno v Project Frame)*
- [x] 1 external/system boundary *(definováno v Project Frame — Notification Service)*
- [x] kompletní Project Frame *(`docs/intent-and-change.md`)*
- [x] 1 Q/C/R/L future pressure *(`docs/intent-and-change.md`, kategorie Q)*
- [ ] 1 reviewed and integrated change *(external PR/review evidence is not present in this workspace)*
- [x] 1 executed engineering spike *(varianta A — Persistence, issue #1)*
- [x] spike evidence + decision *(`docs/evidence-and-evolution.md`)*
- [x] definovaný CP1 walking skeleton

### Co je hotové

- **Tým a repo** — 3 členi, sdílený repozitář existuje a jde do něj commitovat.
- **Doména** — reservation domain (parkovací místo/oblast), entity Resource/Reservation/User a stavy jsou popsané v README (sekce 1) i formálně v [`docs/intent-and-change.md`](docs/intent-and-change.md).
- **Project Frame** — kompletně vyplněný v [`docs/intent-and-change.md`](docs/intent-and-change.md) včetně common rule, domain-specific rule, boundary, assumption, unknown.
- **Future pressure** — vybrána kategorie Q (Quality/Scale) a zdůvodněna v [`docs/intent-and-change.md`](docs/intent-and-change.md).
- **CP1 walking skeleton** — end-to-end backend tok je implementovaný přes OP-01 až OP-05 a ověřený testy.
- **Backend** — ASP.NET Core Web API projekt ve `backend/` s EF Core + SQLite, `ParkingDbContext`, rezervacemi, parkovacími místy a endpointy OP-01 až OP-05.
- **Engineering spike A (Persistence)** — `Reservation` se ukládá do SQLite přes EF Core, načítá zpět a ověřuje testy; evidence a rozhodnutí jsou v [`docs/evidence-and-evolution.md`](docs/evidence-and-evolution.md).
- **`docs/` složka** — obsahuje Project Frame, specifikaci, architekturu, evidenci a projektovou prezentaci.

### Co ještě chybí

- **Produkční autorizace a škálování** — lokální User/Role kontrola je implementovaná, ale externí identity a multi-instance databázová ochrana ještě nejsou součástí řešení.
