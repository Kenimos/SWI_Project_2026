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
- *(stav v době spike C01, později doplněno: cizí klíče, `User`, `ParkingSpot` i kontrola překryvu viz ADR-003)* `SpotId` a `UserId` jsou zatím jen čísla bez cizích klíčů; entity `ParkingSpot` a `User` přibudou, až se tým shodne na doméně. Kontrola překryvu rezervací (common rule) tímto spikem ověřena **není** — zůstává na další iterace (souvisí s vybraným future pressure Q).

## C04 — Quality and Measurement Traceability

The supplied assessment form marks C04 as `N/A`: the provided exercise documents do not contain a corresponding C04 quality-and-measurement assignment. No project quality criterion or measurement target is invented here. This is a source-material limitation, not evidence that an unrequested project artifact was omitted.

## C03 — Architecture Evidence

Baseline: v0.2. The selected scenario is OP-03 Confirm Reservation with BR-01, BR-02, BR-05, and the related OP-04 race behavior.

The AS-IS trace, architecture drivers, alternatives, ADR-006, structural/runtime views, ownership, delta, and conformance rule are documented in `docs/architecture-and-decisions.md`. The main unresolved risk is that `SpotLocks` is process-local and therefore not sufficient for multi-instance deployment.

Behaviour evidence: existing C02 tests cover success, overlap, boundary touching, inactive spots, approval, expiry, cancellation, persistence, and same-spot concurrent confirmation. The strict Confirm/Cancel rule now has a focused regression test: a Cancel request that observed `DRAFT` returns `Conflict` when Confirm persisted `CONFIRMED` first.

Architecture conformance check: `bash scripts/check-architecture-conformance.sh` passed. The full .NET 8 test suite passed with 53 tests, 0 failures, and 0 skips, including HTTP integration tests through `WebApplicationFactory<Program>`.

## C05 — Detailed Design Verification Evidence

Selected unit: `ReservationService`, focused on `ConfirmReservationAsync` and `CancelReservationAsync`.

Derived verification includes lifecycle success, invalid state, overlap rejection, half-open interval boundaries, inactive spots, authorization failures, persistence of state changes, approval transitions, expiry, cancellation, and the strict Confirm/Cancel race. The detailed design and contract mapping are in `docs/architecture-and-decisions.md`.

Current verification status (C05, commit `eaae325`): the backend and test project compiled with .NET SDK 8 and the full suite passed with 53 tests, 0 failures, and 0 skips. After the v0.2.1 audit fixes the suite has 66 tests, see *C07 — Interní audit* below.

HTTP integration evidence: `ApiEndpointTests` uses `WebApplicationFactory<Program>` with temporary SQLite and verifies reservation creation, ownership authorization, area availability, and Approver-only area creation.

## Audit disposition

- AUD-10: README CP1 and implementation claims reconciled.
- AUD-11: README Definition of Done and current-status wording reconciled.
- AUD-12: ADR statuses reconciled with the current implementation.
- AUD-13: Evidence and presentation report the verified 53-test result.
- AUD-14: Current-branch evidence is accepted as sufficient; the external assessment form remains an unchanged historical artifact.
- AUD-15: Team approval of Specification Baseline v0.2 recorded in `docs/specification.md`.

## Current branch implementation evidence

The current branch adds persisted `User`/`UserRole`, `ParkingArea`, `ParkingAreaType`, and `ParkingSpotState`. Approvers can create areas and spots, manage `OUT_OF_SERVICE`/`FREE`, and perform reservation check-in/check-out. Drivers create reservations, and only the owning active Driver may confirm or cancel them.

The generated migration is `AddUsersParkingAreasAndSpotState`. No production database update was performed as part of this change.

## C07 — C06 findings disposition

Assessment od jiného týmu (Kirnig, Kalus, Gala, Carnevale) je v `docs/REPORT.md`. Hodnocená baseline byla `45351cd`, opravy jsou ověřené na `0f039b6`.

| Finding | Verify | Disposition | Closure evidence |
|---|---|---|---|
| F1: Cancel po souběžném Confirmu zrušil potvrzenou rezervaci (rozpor OP-04 / BR-03) | VERIFIED FINDING (reprodukováno inspekcí na `45351cd`) | ACCEPT. Striktní výklad: Cancel, který viděl `DRAFT`, po zámku a nalezení `CONFIRMED` vrací `Conflict` | `eaae325`: `ReservationService.CancelReservationAsync`, test v `ReservationConfirmCancelTests.cs` (assert `Conflict`), 53/53 passed |
| F2: `ParkingSpot` neměl `State`, přestože dokumentace stavy popisovala | VERIFIED FINDING | ACCEPT. Stav místa implementován | `eaae325`: `ParkingSpotState`, migrace `AddUsersParkingAreasAndSpotState`, `ParkingAreaAndSpotStateTests.cs` |
| F3: chybí C04 Quality & Measurement | REJECTED CANDIDATE (hodnotitelé i tým) | REJECT, no-change | C04 nemělo samostatné zadání, Q1–Q3 = N/A |

Otevřené problémy z assessmentu: žádný z F1–F3 nezůstává otevřený. Riziko `SpotLocks` (jen jedna instance) zůstává vědomě přijaté v ADR-006.

## C07 — Interní audit konzistence docs ↔ kód (27. 11. 2026)

Po C06 jsme sami prošli celý projekt proti specifikaci, README, prezentaci a kódu. Podezřelé případy jsme ověřili přes běžící API (`dotnet run`, izolovaná SQLite) a čtením stavu přímo z DB. Auditovaná verze `8a5b312` (tag `c08-before`). Podle C07 B1 jde o **defekty/issues** (prokázaná neshoda), ne o rizika.

| ID | Claim (zdroj) | Observed na `8a5b312` | Disposition | Closure evidence |
|---|---|---|---|---|
| D-01 | README: místo `OUT_OF_SERVICE` „se nikdy nenabízí jako dostupné“ | out-of-service → OP-02 `{"available":true}`, Confirm `Confirmed` 200, stav místa přepsán na `Reserved`, restore pak `invalid_state` | ACCEPT, opraveno (BR-06) | `Out_of_service_spot_is_unavailable_and_cannot_be_confirmed` |
| D-02 | REQ-02: jedno pravidlo dostupnosti | availability oblasti ignorovala `IsActive` (místo s `IsActive=0` hlášeno jako dostupné), OP-02 ignorovalo `OutOfService` | ACCEPT, opraveno (jedna metoda `IsSpotAvailableAsync`) | `Spot_and_area_availability_agree_and_area_reports_reserved` |
| D-03 | README: stav místa odpovídá rezervaci | dvě `CONFIRMED` rezervace na jednom místě, zrušení první → místo `Free` → check-in druhé `invalid_state` 409 | ACCEPT, opraveno (BR-07, `CheckedInAt`) | `Cancelling_one_reservation_keeps_check_in_of_another_possible` |
| D-04 | OP-02 v0.2: `PENDING_APPROVAL` neblokuje | pending nastavil místo `Reserved`, Reject ho neuvolnil → out-of-service `invalid_state` | ACCEPT, opraveno | `Pending_and_rejected_reservations_do_not_lock_spot` |
| D-05 | README: check-in = příjezd k potvrzené rezervaci | check-in prošel roky před `start`; následný Cancel `invalid_state` (není ve spec); po check-outu rezervace dál blokovala okno | ACCEPT, opraveno (okno `[start, end)`, stav `Completed`) | `Check_in_only_inside_reservation_window` (4 hranice), `Check_out_completes_reservation_and_releases_rest_of_window`, `Check_in_before_start_returns_outside_reservation_window` (HTTP) |
| D-06 | Spec: OP pro správu míst a check-in/out | implementováno v `eaae325` bez specifikace; TBD k autorizaci neodpovídalo kódu | ACCEPT, doplněno: spec v0.2.1 (sekce 0, BR-06, BR-07, OP-06, OP-07) | `docs/specification.md` sekce 6 |
| D-07 | Spec sekce 0: všechny časy UTC | čas s offsetem (`+01:00`) se uložil jako lokální čas | ACCEPT, opraveno (`ToUtc`) | `Create_normalizes_local_time_to_utc` |
| D-08 | Conformance pravidlo ADR-006 | skript kontroloval jen `backend/Services` a regex chytal i `==` | ACCEPT, opraveno | skript na čistém stromu exit 0, s podstrčeným přiřazením exit 1 |
| D-09 | README/prezentace/evidence | README popisovalo neexistující atributy (`location`, `spots`, složené ID), prezentace tvrdila „Physical state … verified“, spec sekce 5 nesla hlavičku „Draft“, odstavec spike C01 byl v evidence mimo své místo | ACCEPT, opraveno | tento PR |
| D-10 | README: API | enumy šly v JSON jen jako čísla, `dotnet ef` ignoroval `ConnectionStrings__DefaultConnection` | ACCEPT, opraveno (`JsonStringEnumConverter`, design-time factory) | `Enum_values_are_accepted_as_strings` |

Migrace `SpotStateSingleSourceAndCheckIn`:
- převede `IsActive=0` na `OutOfService`;
- `Reserved` změní na `Free`;
- platnému check-inu doplní `CheckedInAt`;
- `Occupied` bez platného check-inu vrátí na `Free`.

Ověřuje to `Migration_backfills_spot_state_and_check_in`, který migruje z předchozí verze schématu s daty.

Výsledek po opravě: `dotnet test` **66 passed, 0 failed, 0 skipped**, conformance skript passed.

Zůstává otevřené (vědomě):
- no-show stále blokuje místo do `end`, což řeší CR-01 (C08);
- vyřazení místa z provozu nezruší jeho budoucí `CONFIRMED` rezervace (OP-06 assumption, souvisí s chybějící Notification Service);
- `RequiresApproval` nejde nastavit přes API;
- overstay se nemodeluje.

## C08 — Before baseline a test cases

> **Platná before baseline pro CR-01 je tag `c08-before-v0.2.1`** (po opravách interního auditu), re-run TC je níže v *C08 — Re-run na `c08-before-v0.2.1`*. Následující záznam z tagu `c08-before` je historický první běh.

**Historický before baseline:** tag `c08-before` (kód shodný s `0f039b6`, spec Baseline v0.2 BR-01/BR-02/BR-03, ADR-004/ADR-006), schéma `20261025151543_AddUsersParkingAreasAndSpotState`, runtime .NET SDK 8 (EF Core 8.0.10), testovací DB izolovaný SQLite soubor vytvořený `dotnet ef database update --connection "Data Source=<tmp>/tc.db"`, aplikace `dotnet run --no-build --urls http://localhost:5299` s `ConnectionStrings__DefaultConnection` na tento soubor. Žádná tajemství. Seed: `Users(1, Driver)`, `Users(9, Approver)`, `ParkingAreas(1)`, `ParkingSpots(1, Standard, IsActive=1, RequiresApproval=0, Free)`. Před každým TC se DB obnovila z prázdné kopie po migraci. Provedeno 27. 11. 2026 ~16:23 UTC, provedl Jan Ječmínek.

Autoritativní stav se čte přímo z SQLite (`sqlite3`), ne jen z HTTP odpovědi. Kontrolní dotaz BR-02:
`SELECT count(*) FROM Reservations a JOIN Reservations b ON a.Id<b.Id AND a.SpotId=b.SpotId AND a.State='Confirmed' AND b.State='Confirmed' AND a.StartTime<b.EndTime AND b.StartTime<a.EndTime;`

### C08 / TC-C08-01 — sousední rezervace lze obě potvrdit

Test basis: BR-01 (`[start, end)`), BR-02, OP-03 @ v0.2. Riziko: N/A.

| # | Action / input | Expected BEFORE execution | Actual AFTER execution | Evidence |
|---|---|---|---|---|
| 1 | Reset DB, seed; Create + Confirm B1 [10:00, 11:00), Create B2 [11:00, 12:00) (2030-01-01, UTC) | B1 `Confirmed`, B2 `Draft` | `{"id":1,"state":"Confirmed"}`, `{"id":2,"state":"Draft"}` | výstup níže |
| 2 | Readback z DB | `1 Confirmed 10–11`, `2 Draft 11–12` | `1\|1\|10:00\|11:00\|Confirmed`, `2\|1\|11:00\|12:00\|Draft` | sqlite3 |
| 3 | `POST /reservations/2/confirm {"userId":1}` | úspěch | `{"id":2,"state":"Confirmed"}` HTTP 200 | curl |
| 4 | Readback z DB + BR-02 dotaz | obě `Confirmed`, 0 překryvů | obě `Confirmed`, `confirmed_overlaps = 0` | sqlite3 |
| 5 | Opakovatelnost | reset z prázdné kopie DB | provedeno (stejná prázdná kopie pro TC-02) | — |

Result: **PASS**.

### C08 / TC-C08-02 — překrývající potvrzení je odmítnuto

Test basis: BR-02, OP-03 failure outcome `overlap` @ v0.2. Riziko: double booking (pressure Q).

| # | Action / input | Expected BEFORE execution | Actual AFTER execution | Evidence |
|---|---|---|---|---|
| 1 | Reset DB, seed; Create + Confirm B1 [10:00, 11:00), Create B2 [10:30, 11:30) | B1 `Confirmed`, B2 `Draft` | `Confirmed`, `Draft` | curl |
| 2 | Readback z DB | časově se překrývají, potvrzená je jen B1 | `1 Confirmed 10:00–11:00`, `2 Draft 10:30–11:30` | sqlite3 |
| 3 | `POST /reservations/2/confirm {"userId":1}` | odmítnuto jako `overlap` (409) | `{"error":"overlap"}` HTTP 409 | curl |
| 4 | Readback po pokusu | B1 beze změny, B2 **není** `Confirmed`, 0 překryvů | B1 `Confirmed`, B2 `Draft`, `confirmed_overlaps = 0` | sqlite3 |
| 5 | Opakovatelnost | reset z prázdné kopie | provedeno | — |

Result: **PASS**. Limit: test běží s jedním požadavkem, souběžné Confirmy pokrývá unit test souběhu v `ReservationConfirmCancelTests.cs`.

### C08 / TC-C08-03 — sonda současného chování při nedostavení (motivace CR-01)

Test basis: chování baseline v0.2, ne nový požadavek. Ukazuje omezení popsané v CR-01.

Setup: přímý zápis do DB, protože přes API nejde potvrdit rezervaci s `start` v minulosti: B1 `Confirmed` [2026-11-27 14:00, 20:00) bez check-inu, místo `Reserved`. Čas běhu 16:23:51 UTC, tedy 2 h 23 min po `start`.

| # | Action | Observed |
|---|---|---|
| 1 | `GET /parking-spots/1/availability?start=17:00&end=18:00` | `{"available":false}`: místo blokované |
| 2 | Driver `POST /reservations/1/cancel` | `{"error":"too_late"}` HTTP 409 (BR-03) |
| 3 | Approver `POST /reservations/1/check-in` | `{"id":1,"state":"Occupied"}` HTTP 200: check-in nemá časový limit |
| 4 | Readback | `Confirmed`, místo `Occupied` |

Závěr: nedostavení nelze v systému nijak vyřešit, místo zůstává blokované až do `end`. To je výchozí stav pro CR-01 (`docs/intent-and-change.md`).

**Postřeh pro C09 (reprodukovatelnost):** `ParkingDbContextFactory` (design-time) má natvrdo `Data Source=parking.db`, takže `dotnet ef database update` ignoruje `ConnectionStrings__DefaultConnection` a pro jinou DB je potřeba `--connection`. README a příklady s ručním `INSERT INTO ParkingSpots` na této baseline nefungovaly: chyběl `AreaId`/`State` a uživatelé. Opraveno v README v tagu `c08-before` a ověřeno průchodem celého postupu na čisté DB.

## C08 — Re-run na `c08-before-v0.2.1`

Proč znovu: interní audit (D-01 až D-10) změnil kód stavu místa a check-inu, takže CR-01 se analyzuje proti opravené verzi. Postup i oracle jsou stejné jako výše. Seed je bez sloupce `IsActive`, který byl migrací odstraněn. Schéma `20261127163915_SpotStateSingleSourceAndCheckIn`, testovací DB vytvořená přes `ConnectionStrings__DefaultConnection=... dotnet ef database update` (bez `--connection`, tím se ověřila i oprava D-10). Provedeno 27. 11. 2026 16:44 UTC, Jan Ječmínek, kód branche `fix/spot-state-audit`.

| TC | Expected (beze změny oproti prvnímu běhu) | Actual | Result |
|---|---|---|---|
| TC-C08-01 | B2 [11:00, 12:00) se potvrdí, obě `Confirmed`, 0 překryvů | `{"id":2,"state":"Confirmed"}` 200; readback obě `Confirmed`, místo `Free` (Reserved se už neukládá), `confirmed_overlaps = 0` | **PASS** |
| TC-C08-02 | B2 [10:30, 11:30) odmítnuto, B2 zůstává `Draft`, 0 překryvů | `{"error":"overlap"}` 409; readback B1 `Confirmed`, B2 `Draft`, `confirmed_overlaps = 0` | **PASS** |

**TC-C08-03 (sonda nedostavení) na v0.2.1:** B1 `Confirmed` [14:00, 19:00) bez check-inu, běh v 16:44 UTC.
- Availability [17:00, 18:00) vrací `{"available":false}`.
- Cancel řidiče vrací `too_late` 409.
- Check-in ~2 h 44 min po startu projde (`Occupied`, `CheckedInAt` uložen).

Omezení z CR-01 tedy na opravené baseline trvá: nedostavení systém nerozpozná, check-in je povolen v celém okně `[start, end)`.

**Sondy auditu na opravené verzi** (stejné kroky jako při auditu):
- D-01: out-of-service vede na OP-02 `available:false`, Confirm `spot_inactive` 409 a restore 200.
- D-05: check-in v roce 2026 k rezervaci v roce 2030 vrací `outside_reservation_window` 409.
- D-04: Reject a následné out-of-service vrací 200.
- D-02: oblast hlásí `Free/true`, `Reserved/false` a `OutOfService/false`.

