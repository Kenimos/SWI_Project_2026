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
