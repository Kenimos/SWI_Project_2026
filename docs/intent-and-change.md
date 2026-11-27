# Project Frame

## Reservation domain
Rezervace parkovacích míst (`ParkingSpot`) v rámci parkovacích oblastí (`ParkingArea`) — patro garáže, sekce venkovního parkoviště nebo pouliční zóna.

## Purpose
Systém umožňuje řidičům najít volné parkovací místo a zarezervovat si ho předem na konkrétní časové okno, aby po příjezdu měli jistotu dostupného místa. Správcům parkovišť dává přehled o obsazenosti a umožňuje spravovat parkovací oblasti a místa.

## Users / Stakeholders
- **Řidič** — vyhledává dostupná místa, vytváří, potvrzuje a ruší rezervace.
- **Správce parkoviště** — spravuje parkovací oblasti a místa, sleduje obsazenost.
- **Notification Service** *(externí systém, ne přímý uživatel)* — dostává podněty k odeslání notifikací.

## Core concepts
- **Reservation**
- **Resource** (`ParkingSpot`)
- **User**
- **ParkingArea** — nadřazený celek, do kterého místo patří
- **Vehicle** — vozidlo přiřazené k rezervaci (kvůli typu místa, např. EV nabíjení)

## Core operations
- Create reservation
- Confirm / approve reservation
- Cancel reservation
- Check availability

## Persistent state
**Reservation:** `id`, `spotId`, `userId`, `startTime`, `endTime`, `state`

**Resource (ParkingSpot):** `id`, `areaId`, `type`, `state`

## State-changing operation
`DRAFT → CONFIRMED` (potvrzení rezervace)

## Common business rule
Confirmed reservations for the same resource must not overlap.
(Dvě potvrzené rezervace téhož parkovacího místa se nesmí časově překrývat.)

## Domain-specific business rule
Rezervaci parkovacího místa typu `EV_CHARGING` nebo `DISABLED` smí vytvořit pouze uživatel, jehož vozidlo/oprávnění danému typu odpovídá (elektromobil, ZTP průkaz) — ověřuje se při vytváření rezervace.

## External / system boundary
**Notification Service** — informuje uživatele o potvrzení rezervace, blížícím se konci rezervace nebo jejím zrušení.

## Assumption
Předpokládáme, že každé parkovací místo patří vždy právě do jedné parkovací oblasti a nemůže být sdíleno mezi více oblastmi.

## Unknown
Zatím nevíme, jestli bude ceník/platba součástí rozsahu tohoto projektu, nebo bude řešena externím fakturačním systémem.

---

## Selected future pressure

**Category:** Q — Quality / Scale

**Concrete pressure:** Při špičkovém provozu (např. velká akce/stadion v blízkosti parkoviště) může nastat až 10× více současných pokusů o rezervaci stejné omezené sady míst během několika sekund.

**Why it is relevant to our reservation system:** Common rule vyžaduje, aby se dvě potvrzené rezervace téhož místa nikdy nepřekrývaly. Při vysoké souběžnosti roste riziko race condition mezi kontrolou dostupnosti a zápisem rezervace (dvojí rezervace stejného místa). To ovlivní návrh vrstvy perzistence (transakce, unikátní databázová omezení) — v C01 se to ještě neimplementuje, jen evidujeme jako budoucí tlak na architekturu.

---

## C08 / CR-01 — Controlled Change Card: uvolnění místa při nedostavení (no-show)

**Stav karty:** PROPOSED. Rozhodnutí níže je návrh autora, platné je až po review (viz *Peer reviewer*).

**Request source and concrete project need:**
Správce parkoviště (role `Approver`): „Když řidič s potvrzenou rezervací nepřijede, místo zůstane blokované až do konce rezervace a nemůžu ho nabídnout nikomu jinému.“ Přínos: vyšší využití omezené kapacity, což je přesně situace z future pressure Q (špička, málo míst).

**Before commit/tag + spec/BR/ADR + schema/config:**
- Kód: `0f039b6` (dokumentačně doplněno v tagu `c08-before`, kód beze změny).
- Specifikace: Baseline v0.2, `docs/specification.md`: BR-01 (interval `[start, end)`), BR-02 (zákaz překryvu `CONFIRMED`), BR-03 (Cancel jen před `start`).
- Architektura: ADR-004 a ADR-006 (`ReservationService` vlastní všechny přechody stavu, serializace přes `SpotLocks`).
- Schéma: poslední migrace `20261025151543_AddUsersParkingAreasAndSpotState`.
- Konfigurace: `Reservations:ApprovalWindowHours` (default 24 h), SQLite `Data Source=parking.db`, žádná tajemství.

**Old accepted behavior / observable limitation** (ověřeno během TC-C08-03, `docs/evidence-and-evolution.md`):
- `CONFIRMED` rezervace bez check-inu blokuje místo po celou dobu `[start, end)`; Check Availability vrací `available: false`.
- Řidič ji po `start` už nemůže zrušit (`too_late`, BR-03), Approver nemá žádnou cestu, jak ji zrušit.
- Check-in nemá žádný časový limit: projde i 2 h 20 min po začátku.

**New intended behavior, scope, out of scope:**
- Pokud k `CONFIRMED` rezervaci neproběhne check-in do `start + G` (grace period, návrh `G = 15 min`, konfigurovatelné), rezervace se považuje za **nedostavení (`NO_SHOW`)**. Přestane blokovat místo a pozdní check-in je odmítnut.
- Rozsah: stavový model rezervace, Check-in, výpočet překryvu v Check Availability (místo i oblast), Confirm, Approve.
- Mimo rozsah: notifikace řidiči (Notification Service neexistuje), poplatky za no-show, změna Cancel politiky BR-03, proaktivní worker (rozhoduje se ve variantách níže).

**Acceptance conditions:**

| AC | Typ | Podmínka |
|---|---|---|
| AC-01 | pozitivní | Check-in `CONFIRMED` rezervace v čase `start + 14:59` uspěje; místo je `OCCUPIED`, rezervace zůstává `CONFIRMED`. |
| AC-02 | negativní | Check-in v čase `start + 15:01` je odmítnut; rezervace je trvale `NO_SHOW` a místo není `OCCUPIED`. |
| AC-03 | hranice | Přesně v `start + 15:00` platí už no-show, check-in je odmítnut (stejná konvence `≥` jako u vypršení schválení, REQ-08). |
| AC-04 | pozitivní | Po `start + G` bez check-inu vrací Check Availability pro zbytek okna `available: true` a jiná rezervace překrývající tento zbytek jde potvrdit. |
| AC-05 | regrese | Před `start + G`, nebo po úspěšném check-inu, se rezervace dál chová jako `CONFIRMED`: překrývající Confirm/Approve je odmítnut (BR-02). Create, Cancel, Approve/Reject a oprávnění se nemění (TC-C08-01/02 a stávajících 53 testů). |

**Impact analysis method / evidence reviewed:**
Postupovali jsme od AC-02/AC-04 k vlastníkovi rozhodnutí (`ReservationService`, ADR-006). Pak jsme prošli všechna místa, kde se čte `State == Confirmed` (`grep` v `backend/`: Check Availability, area availability, overlap v Confirm a Approve, Check-in/out, Cancel), datový model (`Reservation`, migrace) a testy.

| Dimension / exact artifact | CHANGE/CHECK/NO IMPACT + why | Needed delta/verification | Owner/evidence |
|---|---|---|---|
| Spec/BR/state: `docs/specification.md` BR-02, 3b | CHANGE: nové pravidlo BR-06 (no-show) a nový terminální stav `NO_SHOW`; BR-02 musí říct, že no-show neblokuje | BR-06 a přechod `CONFIRMED → NO_SHOW` ve stavovém diagramu, baseline v0.3 | Robin; review spec |
| Architecture/ownership: ADR-006, `SpotLocks` | CHECK: rozhodnutí o no-show musí vlastnit `ReservationService` (jediný writer stavu, conformance skript) | Skript `check-architecture-conformance.sh` musí dál projít | Robin; běh skriptu |
| Interfaces: `POST /reservations/{id}/check-in` | CHANGE: nová chyba `no_show` (409) | Integrační test HTTP | Robin; `ApiEndpointTests` |
| Interfaces: `GET .../availability` (spot i area) | CHANGE (sémantika, ne signatura): no-show rezervace se nepočítá jako překryv | Test AC-04 | Robin |
| Detailed design/code: `ReservationService` (CheckIn, CheckAvailability, CheckAreaAvailability, overlap v Confirm/Approve) | CHANGE: společný predikát „blokující rezervace“ místo `State == Confirmed` + líná detekce v check-inu | Unit testy s `FakeClock` na 14:59 / 15:00 / 15:01 | Robin |
| Detailed design/code: Cancel, Reject, Create | CHECK: Cancel po `start` je už `too_late`, takže se nemění | Regrese stávajících testů | Robin |
| Data/schema/migration: `Reservation` | CHANGE: nový sloupec `CheckedInAt` (nullable); bez něj nejde odlišit „přijel“ od „nepřijel“ | Migrace + **backfill**: stávající `Confirmed` s místem `Occupied` dostanou `CheckedInAt = StartTime`, jinak by se omylem staly no-show | Robin; test migrace na DB s existujícími řádky |
| Configuration: `appsettings.json` | CHANGE: `Reservations:NoShowGraceMinutes` (default 15) | Zdokumentovat v README, jednotky v minutách, časy v UTC | Robin |
| CI/release | CHECK: zatím žádné CI, řeší C09 | CI job `dotnet test` (C09) | Robin / C09 |
| Tests/integration/risks | CHANGE: nové testy AC-01–AC-04, regrese AC-05 | Zapsat test basis **před** implementací (níže) | Robin; Marek review |
| Operation/recovery | CHECK: rollback aplikace nevrátí stav `NO_SHOW` zpět na `CONFIRMED`; starší verze by nový stav neuměla načíst | Zapsat jako known limitation v release záznamu C09 | Robin |
| Notification Service | NO IMPACT: write path neexistuje, boundary je zatím jen definovaná | — | — |
| Admin/import/worker paths | NO IMPACT (varianta A): taková write path v systému neexistuje | — | — |

**Alternatives and comparable criteria:**

| Option | Meets AC? | Benefit | Cost/failure/rollback | Verification |
|---|---|---|---|---|
| A: líná detekce. Predikát „blokující“ = `Confirmed && (CheckedInAt != null \|\| now < start + G)` ve všech overlap dotazech; check-in po deadline uloží `NO_SHOW` | AC-01–AC-05 ano. Stav `NO_SHOW` se v DB objeví až při pokusu o check-in, dostupnost ale funguje okamžitě | Malá změna v existujícím vlastníkovi, žádný nový proces, stejný vzor jako REQ-08 (EXPIRED) | Rezervace zůstává v DB `Confirmed`, dokud na ni nikdo nesáhne; přehledy „podle stavu“ ji ukážou špatně. Rollback: nový sloupec zůstane, `NO_SHOW` řádky stará verze nenačte | FakeClock testy hranice + integrační HTTP test + regrese |
| B: worker + guard. `BackgroundService` každou minutu označí `NO_SHOW` + stejný guard v check-inu | Ano, stav je v DB aktuální do ~1 min | Uložený stav odpovídá realitě i bez interakce | Nový proces, souběh workeru s check-inem (musí brát `SpotLocks`), testování času a restartů, ve více instancích duplicitní běh | Testy workeru, restart, souběh check-in vs. worker |
| C: beze změny kódu, proces. Správce ručně řeší nedostavení | Ne: AC-02/AC-04 nesplní, v systému žádná cesta pro správce neexistuje | Nulová změna | Potřeba zůstává nevyřešená, místo blokované do konce rezervace | — |

**Inaction consequences / feasible compensation:** bez změny zůstane místo při nedostavení blokované až do `end` a nikdo ho nemůže uvolnit, protože Cancel po `start` je zakázán a Approver nemá žádný zásah. Ve špičce (pressure Q) to přímo snižuje kapacitu.

**Peer reviewer and challenged assumption:** *doplní reviewer:* Robin Tokarský. Zpochybnit hlavně: (1) je `G = 15 min` přijatelné pro správce? (2) stačí, že `NO_SHOW` se do DB zapíše líně, nebo je proaktivní stav nutný?

**Decision ACCEPT / REJECT / DEFER / NO_CHANGE, owner, date, reason:**
PROPOSED: **ACCEPT, varianta A**. Owner: Jan Ječmínek, 27. 11. 2026. Důvod: splní všechna AC s nejmenším zásahem v existujícím vlastníkovi rozhodnutí. Opakuje osvědčený vzor líného vypršení z REQ-08 a nepřidává nový provozní proces. Omezení varianty A (uložený stav se aktualizuje až při dotyku) je přijatelné, protože o dostupnosti rozhoduje predikát, ne uložený stav. *Finální rozhodnutí potvrdí reviewer.*

**If ACCEPT: smallest change and acceptance tests to perform in C09:**
1. `ReservationState.NoShow`, `Reservation.CheckedInAt` + migrace s backfillem, `Reservations:NoShowGraceMinutes`.
2. Jeden privátní predikát/metoda „blokující rezervace“ v `ReservationService`, použitý v CheckAvailability, CheckAreaAvailability, Confirm a Approve.
3. Check-in: po zámku `SpotLocks`, pokud `now ≥ start + G` a `CheckedInAt == null`, uloží `NoShow`, místo `Free`, vrátí `no_show`; jinak nastaví `CheckedInAt = now`.
4. Spec v0.3 (BR-06, stavový diagram), README.
5. Testy (test basis, expected stanoveno teď, před implementací):

| Test ID | Setup | Akce | Expected |
|---|---|---|---|
| TC-C09-01 (AC-01) | B1 `CONFIRMED` [10:00, 12:00), místo `Reserved`, FakeClock 10:14:59 | check-in B1 | OK, místo `Occupied`, B1 `Confirmed`, `CheckedInAt` = 10:14:59 |
| TC-C09-02 (AC-03) | stejné, FakeClock 10:15:00 | check-in B1 | `no_show`; po readbacku z nového DbContextu B1 `NoShow`, místo ne `Occupied` |
| TC-C09-03 (AC-02) | stejné, FakeClock 10:15:01 | check-in B1 | `no_show`; B1 `NoShow` |
| TC-C09-04 (AC-04) | B1 `CONFIRMED` [10:00, 12:00) bez check-inu, B2 `DRAFT` [11:00, 12:00), FakeClock 10:20 | availability [11:00, 12:00), pak confirm B2 | `available: true`; B2 `Confirmed`; B1 nikdy současně s B2 jako blokující |
| TC-C09-05 (AC-05) | B1 `CONFIRMED` [10:00, 12:00) s check-inem v 10:05, B2 `DRAFT` [11:00, 12:00), FakeClock 10:20 | confirm B2 | `overlap`, B2 zůstává `Draft` |
| Regrese | — | TC-C08-01, TC-C08-02, celá sada testů, conformance skript | beze změny výsledku |

**If NO_CHANGE/REJECT:** N/A.

**Open uncertainty and next trigger:** hodnota `G` (potvrdit se správcem). Pokud se objeví požadavek na přehled rezervací podle uloženého stavu nebo na notifikaci o no-show, je potřeba přehodnotit na variantu B.
