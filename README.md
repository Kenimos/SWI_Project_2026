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
| `state` | aktuální stav místa (viz níže) |

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
  → validate (volnost místa, kolize časového okna, oprávnění k typu místa)
  → persist (uložení rezervace do databáze, nastavení stavu místa na RESERVED)
  → return reservation ID
  → automated check (ověření, že rezervace je čitelná zpět z databáze se správným stavem)
```

---

## 6. Struktura repozitáře

```
backend/    ASP.NET Core Web API (.NET 8, EF Core, SQLite)
frontend/   zatím prázdné — frontend se zatím neřeší
```

Zatím je připraven jen holý základ backendu (prázdný Web API projekt + nainstalované EF Core / SQLite balíčky). Žádné entity, DbContext ani endpointy zatím nejsou definované — doména se ještě dolaďuje.

## 7. Backend — jak spustit

### Požadavky
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- (volitelně) [`dotnet-ef`](https://learn.microsoft.com/ef/core/cli/dotnet) globální nástroj — jen až budeme přidávat migrace: `dotnet tool install --global dotnet-ef`

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

Connection string na SQLite databázi je v `backend/appsettings.json` pod klíčem `ConnectionStrings:DefaultConnection` (`Data Source=parking.db`). Až přibude `DbContext`, migrace se založí příkazem:

```bash
cd backend
dotnet ef migrations add InitialCreate
dotnet ef database update
```

### Build / test

```bash
cd backend
dotnet build
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
- [ ] create + confirm/approve + cancel + availability
- [ ] common overlap rule
- [ ] 1 domain-specific business rule
- [ ] 1 external/system boundary
- [ ] kompletní Project Frame
- [ ] 1 Q/C/R/L future pressure
- [ ] 1 reviewed and integrated change
- [ ] 1 executed engineering spike
- [ ] spike evidence + decision
- [x] definovaný CP1 walking skeleton

### Co je hotové

- **Tým a repo** — 3 členi, sdílený repozitář existuje a jde do něj commitovat.
- **Doména** — reservation domain (parkovací místo/oblast), entity Resource/Reservation/User a stavy jsou popsané v tomto README (sekce 1).
- **CP1 walking skeleton** — end-to-end tok je definovaný (sekce 5), zatím není implementovaný.
- **Backend skeleton** — prázdný ASP.NET Core Web API projekt ve `backend/` s nainstalovaným EF Core + SQLite, bez entit/DbContextu/endpointů.

### Co ještě chybí

Body označené doménou v README (operace, common rule, domain-specific rule, boundary) jsou zatím jen **popsané v textu**, ne implementované ani formálně ověřené — proto zůstávají neodškrtnuté, dokud nebudou splňovat požadovaný formát zadání:

- **`docs/` složka** vůbec neexistuje — chybí `intent-and-change.md` (Project Frame), `architecture-and-decisions.md` a `evidence-and-evolution.md`.
- **Project Frame** — potřeba přesně vyplnit strukturu (Purpose, Users/Stakeholders, Persistent state, Assumption, Unknown, ...) do `docs/intent-and-change.md`.
- **Future pressure (Q/C/R/L)** — zatím nevybráno ani nezdůvodněno.
- **Review cyklus** — zatím neproběhla žádná změna vytvořená jedním členem a zkontrolovaná druhým před integrací.
- **Engineering spike** — zatím pouze scaffolding (prázdný projekt), ne skutečně provedený spike (A — Persistence / B — Boundary failure / C — Reproducible build). Nic z toho zatím není spuštěné a ověřené.
- **Evidence + decision** — nezapsáno do `docs/evidence-and-evolution.md`.
- **Common overlap rule a domain-specific rule** — formálně jde o body Project Frame, ne jen popis v README; potřeba je přenést tam.
