# Architecture and Decisions

## ADR-001: Backend stack

**Decision:** Backend je postaven na ASP.NET Core Web API (.NET 8), s Entity Framework Core a SQLite jako databází.

**Why:**
- .NET 8 je aktuálně nainstalované LTS SDK v prostředí týmu (.NET 9 zatím není k dispozici).
- EF Core + SQLite umožňuje rychlý start bez nutnosti spravovat externí databázový server, zatímco EF Core abstrakce zůstává stejná i při pozdějším přechodu na jiný SQL engine (např. PostgreSQL).
- Swagger/OpenAPI je součástí výchozí šablony a usnadní ruční testování endpointů bez frontendové vrstvy.

**Status:** platí pro C01 — projekt je zatím pouze scaffold (prázdné Web API, nainstalované balíčky), bez entit, `DbContext` nebo endpointů.

## ADR-002: Frontend

**Decision:** Frontend zatím není řešen, složka `frontend/` je připravena jako placeholder.

**Why:** tým se ještě neshodl na technologii/frameworku, priorita C01 je backend doména a datový model.

**Status:** open — doplní se, až padne rozhodnutí.

## ADR-003: Persistence Reservation (výsledek spike A)

**Decision:** Schéma DB se spravuje přes EF Core migrace; stav rezervace se ukládá jako text; časy jsou v UTC; soubor `parking.db` se necommituje.

**Why:** spike A (viz [evidence-and-evolution.md](evidence-and-evolution.md)) ověřil, že uložení a znovunačtení `Reservation` přes EF Core + SQLite funguje včetně migrací. Text ve sloupci `State` je čitelnější při ručním prohlížení DB než číslo enumu.

**Status:** platí. Entity `ParkingSpot`/`User`, cizí klíče a kontrola překryvů jsou otevřené.
