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
