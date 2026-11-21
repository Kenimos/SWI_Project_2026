# C06 — Assessment Form (CZ)

## Projekt

```text
Hodnotící tým: Tomáš Kirnig, Martin Kalus, Petr Gala, Dennis Carnevale
Hodnocený tým: Jan Ječmínek, Marek Šikula, Robin Tokarský
Projekt: Parking Reservation System (SWI_Project_2026)
Assessed commit/tag: 45351cd (45351cddf443e10a72083e54f56453269d68e0fc)
Datum: 23. 10. 2026
```

## Stavy

`YES / PARTLY / NO / N/A`

Každý řádek musí obsahovat Evidence.

## Baseline

| ID | Odpověď | Evidence / komentář |
|---|---|---|
| B1 Stejná identifikovaná baseline pro docs/code/tests | PARTLY | Kód (`backend/Services/ReservationService.cs`), testy (`tests/`) i specifikace (`docs/specification.md`) odpovídají Baseline v0.2 (commit `45351cd`). `README.md` v sekci 9 je však neaktuální (stále uvádí C01 stav: „neimplementováno“). |
| B2 Relevantní produkt nebo testy lze spustit | YES | `dotnet test tests/ParkingReservation.Api.Tests` projde bez chyb: 41 passed, 0 failed (1,0 s). DB migrace běží automaticky do temp SQLite. |

## C02 — Engineering Specification

Vybraná operace: OP-03 — Confirm Reservation  
Vybraný business rule / invariant: BR-02 — Exclusive Resource invariant (`[start, end)` polootevřené intervaly)

| ID | Odpověď | Evidence / komentář |
|---|---|---|
| R1 Operation: input + precondition + postcondition/state | YES | `docs/specification.md` (řádky 165–275, v0.2 řádky 582–600). Vstup: `id`, `userId`. Preconditions: stav DRAFT, `now < start`, aktivní spot. Postcondition: stav `CONFIRMED` / `PENDING_APPROVAL`, alokace slotu. |
| R2 Business rule/invariant je jednoznačný | YES | `docs/specification.md` (řádky 26–30). BR-02 zakazuje překryv dvou `CONFIRMED` rezervací na témže místě. Polootevřené intervaly `[start, end)` vylučují kolizi při dotyku hranic. |
| R3 Operation/rule je konzistentní se state modelem | YES | `docs/specification.md` (diagramy 3b a 3d). Přechody `DRAFT -> CONFIRMED` (při `RequiresApproval=false`) a `DRAFT -> PENDING_APPROVAL` (při `RequiresApproval=true`) přesně odpovídají specifikaci. |
| R4 Implementace odpovídá specifikované semantics | YES | `backend/Services/ReservationService.cs` (`ConfirmReservationAsync`, řádky 119–194). Ověřuje stav, čas, aktivitu, větvení `RequiresApproval` i DB překryv (`r.StartTime < end && start < r.EndTime`). |
| R5 Existuje vhodná verification evidence | YES | `tests/ParkingReservation.Api.Tests/ReservationConfirmCancelTests.cs` (řádky 66–179). 8 testů ověřuje úspěch, kolizi, dotyk hranic, neaktivní spot, expiraci i paralelní souběh. |

## C03 — Architecture

Vybraná responsibility: Správa rezervací a serializace alokace míst (`ReservationService` + `SpotLocks`)  
Vybraná interaction: Confirm Reservation execution path (včetně větvení schvalování a zámků)  
Vybrané ADR/decision: ADR-004: Souběh Confirm/Cancel (OP-03, OP-04)

| ID | Odpověď | Evidence / komentář |
|---|---|---|
| A1 Responsibility + owner jsou explicitní | PARTLY | Vrstvy jsou popsány v `README.md` (sekce 6) a ADR-004. Chybí však formální C03 architektonický dokument (structural / component view). |
| A2 Skutečný code ownership odpovídá architektuře | YES | Logiku a serializaci vlastní `backend/Services/ReservationService.cs` s `SpotLocks.cs`. Data spravuje `ParkingDbContext.cs`, HTTP mapuje `Program.cs`. |
| A3 Dynamic interaction odpovídá skutečnému call path | PARTLY | Call path v kódu přesně kopíruje diagram aktivit 3d (`docs/specification.md`). Chybí však sekvenční diagram zachycující interakci komponent. |
| A4 ADR/decision odpovídá současné implementaci | YES | ADR-004 definuje serializaci přes `SemaphoreSlim` a `IClock`. Implementováno v `backend/Services/SpotLocks.cs` a `backend/Services/IClock.cs`. |

## C04 — Quality & Measurement

Quality concern: N/A — nehodnoceno.  
Criterion: N/A — nehodnoceno.

Odůvodnění: Zadání C06 požaduje hodnocení C04, ale odpovídající úkol v dodaných zadáních cvičení (CV01–CV03, CV05) chybí. Jde o nesoulad podkladů. Podle Důležitého pravidla C06 („Nekontrolujete nic, co předchozí zadání nepožadovala“) se tato část nekontroluje a její absence se nepovažuje za chybu projektu.

| ID | Odpověď | Evidence / komentář |
|---|---|---|
| Q1 Criterion je skutečně evaluable | N/A | Nekontrolováno: požadavek v části C04 Zadání 6 nemá odpovídající zadání v dodaných podkladech CV01–CV05. |
| Q2 Measurement měří criterion ve správném contextu | N/A | Nekontrolováno ze stejného důvodu; viz odůvodnění nesouladu podkladů výše. |
| Q3 Decision odpovídá výsledku měření | N/A | Nekontrolováno ze stejného důvodu; viz odůvodnění nesouladu podkladů výše. |

## C05 — Detailed Design & Unit Verification

Vybraná software unit: ReservationService

| ID | Odpověď | Evidence / komentář |
|---|---|---|
| D1 Design intent je explicitní a pochopitelný | YES | `docs/specification.md` (sekce 2 a 5) a komentáře v `ReservationService.cs`. Záměr orchestrace, validací a zamykání je zcela zřejmý. |
| D2 DD odpovídá skutečné implementaci | YES | Metody servisy přesně odpovídají kontraktům v `backend/Contracts/` i typovaným chybovým návratům v `ReservationResults.cs`. |
| D3 Unit/component tests jsou odvoditelné z DD | YES | Testy v `tests/` přímo zrcadlí sekce „Verification examples“ ze specifikace (např. OP-02 dotazy na intervaly v `ReservationServiceTests.cs`). |
| D4 Relevantní branches/boundaries/states/failures jsou ověřeny | YES | Ověřeno 41 testy: mezní dotyky hranic, nulová délka, časy před/po startu, aktivní/neaktivní místa, všech 6 stavů rezervace i chybové kódy. |
| D5 Vybraný test má smysluplný oracle/expected result | YES | Test `Confirm_succeeds_when_intervals_only_touch_at_boundary` ověřuje polootevřenost intervalů podle pravidla BR-01, ne pouhým opisem kódu. |

## Candidate findings

### F1

```text
Source: C02
Engineering claim: V docs/specification.md (řádky 334–336 a diagram 3c) se uvádí: pokud Confirm a Cancel soupeří na stejné rezervaci a Confirm vyhraje první, Cancel má být odmítnut jako konflikt (stav zůstane CONFIRMED).
Expected: Po potvrzení rezervace má být konkurenční Cancel odmítnut.
Observed: V backend/Services/ReservationService.cs (řádky 228–262) Cancel po získání zámku zjistí stav CONFIRMED, ale protože přechod CONFIRMED -> CANCELLED je obecně povolen (řádek 255), rezervaci zruší a vrátí úspěch. Poznámka: samotná specifikace si v tomto bodě interně protiřečí — řádky 310–312 a verification example na řádku 340 explicitně povolují přechod CONFIRMED -> CANCELLED, zatímco failure outcome na řádcích 334–336 tvrdí, že Cancel po concurrent Confirmu má být odmítnut. Kód sleduje permisivnější interpretaci (hlavní definice state changes + verification examples).
Evidence: docs/specification.md (řádky 334–336, diagram 3c krok 463) vs. backend/Services/ReservationService.cs (řádky 228–262).
Verification performed: Kódová inspekce ReservationService.cs: chybí kontrola původního stavu / concurrency token; Cancel po Confirmu bezpodmínečně projde.
Result: VERIFIED FINDING
```

### F2

```text
Source: C01 / C02
Engineering claim: README.md (sekce 1 a 5) a docs/intent-and-change.md (řádek 30) tvrdí, že ParkingSpot má stavy FREE, RESERVED, OCCUPIED, OUT_OF_SERVICE a potvrzení rezervace nastavuje stav místa na RESERVED.
Expected: ParkingSpot obsahuje stavový atribut a systém jej při potvrzení rezervace mění.
Observed: V backend/Domain/ParkingSpot.cs žádný atribut State není (pouze IsActive a RequiresApproval). Fyzický stav se nesleduje; obsazenost se počítá dynamicky z tabulky Reservations. Poznámka k dopadu: Tento rozpor nijak neohrožuje funkčnost systému — dynamický výpočet dostupnosti z rezervací je z pohledu návrhu dokonce čistší (Single Source of Truth, eliminuje redundanci a riziko desynchronizace stavu místa). Jedná se o neuklizený dokumentační dluh z fáze C01.
Evidence: README.md (řádky 32–41, 99) vs. backend/Domain/ParkingSpot.cs (řádky 8–22) a EF migrace.
Verification performed: Inspekce DB schématu v backend/Migrations/: tabulka ParkingSpots sloupec State neobsahuje; operace OP-01..05 fungují správně i bez něj.
Result: VERIFIED FINDING
```

### F3

```text
Source: C04 — nesoulad podkladů cvičení
Engineering claim: Původní podezření bylo, že projektu chybí povinný výstup C04 (Quality & Measurement).
Expected: Hodnotit lze pouze skutečně zadané požadavky předchozích cvičení (Důležité pravidlo C06).
Observed: Zadání C06 předpokládá C04, ale dodaná zadání cvičení odpovídající úkol neobsahují.
Evidence: Pokyny C06 (Důležité pravidlo: C06 nesmí zpětně přidávat požadavky); formulář Q1–Q3; dodaná zadání CV01–CV05.
Verification performed: Porovnání zadání cvičení s pokyny pro hodnocení; povinnost odevzdat C04 nebyla doložena.
Result: REJECTED CANDIDATE — nejde o chybu projektu; Q1–Q3 = N/A.
```

## Owner disposition

| Finding | ACCEPTED / REJECTED / DEFERRED | Reason/evidence | Action / no-change |
|---|---|---|---|
| F1 | | | |
| F2 | | | |
| F3 | | | |
