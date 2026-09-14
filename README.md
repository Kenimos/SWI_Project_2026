# SWI C01 — Rezervační systém parkovacích míst

> Tým 3–4 studenti · pracovní blok 90 minut · **deadline před C02**

## 1. Zadání cvičení

**Deadline:** před začátkem C02.

Nemusíte všechno dokončit během 90 minut C01. Na cvičení udělejte maximum. Všechny povinné výstupy a celý *Definition of Done* musí být hotové před začátkem vašeho C02. C02 předpokládá, že C01 je dokončeno.

**Výsledek C01:**
- společný repo,
- jasně vymezený rezervační systém,
- Project Frame,
- jedna review smyčka,
- jeden skutečně provedený engineering spike,
- evidence,
- definovaný walking skeleton pro CP1.

### Rozsah implementace v C01

V C01 **nemusíte implementovat celý** reservation system. Operace, stavy a pravidla popsané níže definují **zamýšlený rozsah systému pro další týdny**. V C01 musí být technicky skutečně provedena **pouze jedna zvolená engineering spike**.

---

## 2. Doména: Rezervační systém parkovacích míst

Systém umožňuje uživatelům vyhledat, zarezervovat a spravovat parkovací místa v rámci parkovacích oblastí (parkovišť). Cílem je pokrýt celý životní cyklus od vytvoření parkoviště, přes rezervaci konkrétního místa, až po jeho fyzické obsazení a uvolnění.

### 2.1 Hlavní entity

| Entita | Popis |
|---|---|
| **Parkovací oblast** (`ParkingArea`) | Fyzický celek/parkoviště (např. hala, otevřené venkovní parkoviště, ulice se zónovým stáním). Obsahuje více parkovacích míst, má vlastní kapacitu, otevírací dobu a ceník. |
| **Parkovací místo** (`ParkingSpot`) | Konkrétní, jednoznačně identifikovatelné místo v rámci oblasti (např. patro, řada, číslo). Má typ, stav a je vázáno na aktuální/plánované rezervace. |
| **Rezervace** (`Reservation`) | Vazba mezi uživatelem a konkrétním parkovacím místem na definovaný časový interval. |
| **Uživatel** (`User`) | Registrovaný zákazník, který vytváří rezervace; případně administrátor spravující oblasti a místa. |
| **Vozidlo** (`Vehicle`) | SPZ/typ vozidla přiřazené k rezervaci (kvůli kontrole vjezdu/výjezdu a typu místa — např. elektromobil). |
| **Ceník / Tarif** (`PricingPolicy`) | Pravidla výpočtu ceny za rezervaci (hodinová sazba, denní strop, storno poplatky). |

---

## 3. Parkovací oblasti (Parking Areas)

Parkovací oblast reprezentuje samostatně spravovaný celek parkovacích míst. Každá oblast má:

- **Identifikaci** — název, adresa/GPS souřadnice, jedinečné ID.
- **Typ oblasti**:
  - `GARAGE` — krytá vícepodlažní garáž,
  - `OPEN_LOT` — otevřené venkovní parkoviště,
  - `STREET_ZONE` — pouliční rezidentní/placené zóny,
  - `PARK_AND_RIDE` — parkoviště u MHD/nádraží.
- **Kapacitu** — celkový počet míst, rozpad podle typu místa (viz níže).
- **Provozní dobu** — nonstop / omezená otevírací doba, svátky.
- **Ceník** — vlastní tarif nebo odkaz na sdílený tarif.
- **Stav oblasti**:

| Stav oblasti | Popis |
|---|---|
| `ACTIVE` | Oblast je v provozu, rezervace jsou možné. |
| `FULL` | Oblast nemá žádné volné místo odpovídajícího typu — odvozený/agregovaný stav z obsazenosti míst, rezervace nelze vytvořit. |
| `MAINTENANCE` | Oblast je dočasně mimo provoz (např. rekonstrukce), rezervace pozastaveny. |
| `CLOSED` | Oblast je trvale/sezónně uzavřena a nezobrazuje se ve vyhledávání. |

### Vztah oblast → místa
Jedna oblast obsahuje 1..N parkovacích míst. Kapacita oblasti se **odvozuje** z počtu a stavů jejích míst (agregace), nikoli z ručně zadaného čísla — to zajišťuje konzistenci mezi zobrazovanou dostupností a skutečným stavem jednotlivých míst.

---

## 4. Parkovací místa (Parking Spots) a jejich stavy

Každé parkovací místo patří do právě jedné parkovací oblasti a má vlastní identifikaci (patro, sekce, číslo místa) a typ.

### 4.1 Typy parkovacích míst

| Typ | Popis |
|---|---|
| `STANDARD` | Běžné osobní vozidlo. |
| `COMPACT` | Menší vozidla (užší rozteč). |
| `DISABLED` | Vyhrazeno pro ZTP, širší rozměry, blíže vchodu. |
| `EV_CHARGING` | Vybaveno nabíjecí stanicí pro elektromobily. |
| `MOTORCYCLE` | Vyhrazeno pro motocykly. |
| `VIP_RESERVED` | Trvale rezervované/předplacené místo (např. pro předplatitele). |

### 4.2 Stavový model parkovacího místa

Stav místa je **primárním zdrojem pravdy** pro dostupnost. Navrhovaný konečný automat:

```
                ┌────────────┐
        ┌──────►│    FREE    │◄───────────────┐
        │       └─────┬──────┘                │
        │             │ vytvoření rezervace    │
        │             ▼                        │
        │       ┌────────────┐                 │
        │       │  RESERVED  │                 │
        │       └─────┬──────┘                 │
        │             │ příjezd / check-in      │
   uvolnění           ▼                        │ uvolnění
   (cancel/expire)┌────────────┐          (odjezd / check-out)
        │       │  OCCUPIED  │──────────────────┘
        │       └─────┬──────┘
        │             │ vypršení bez odjezdu
        │             ▼
        │       ┌────────────┐
        └───────│  OVERSTAY  │  (řešeno penalizací / eskalací)
                └────────────┘

  Kdykoliv (administrativně, nezávisle na rezervačním toku):
  FREE / RESERVED  ──►  OUT_OF_SERVICE  (porucha, úklid, revize)
  OUT_OF_SERVICE   ──►  FREE            (uvedení zpět do provozu)
```

| Stav místa | Popis | Kdo/co způsobuje přechod |
|---|---|---|
| `FREE` | Místo je volné a lze na něj vytvořit rezervaci nebo jej obsadit bez rezervace (walk-in), pokud to oblast povoluje. | výchozí stav / uvolnění po odjezdu / zrušení rezervace |
| `RESERVED` | Místo je zamluvené konkrétní rezervací na budoucí nebo právě probíhající časové okno, fyzicky ale ještě neobsazené. | vytvoření rezervace uživatelem |
| `OCCUPIED` | Na místě fyzicky stojí vozidlo (detekováno senzorem / potvrzeno check-inem obsluhy / manuálním záznamem). | check-in / detekce vjezdu |
| `OVERSTAY` | Vozidlo zůstává na místě po vypršení rezervovaného okna bez prodloužení. | uplynutí `reservation.endTime` bez check-outu |
| `OUT_OF_SERVICE` | Místo je administrativně blokováno (porucha nabíječky, údržba, únik oleje, sníh). | administrátor / senzor poruchy |

> **Invariant:** místo ve stavu `OUT_OF_SERVICE` nesmí být nikdy nabídnuto ve výsledcích vyhledávání dostupnosti, bez ohledu na to, že by jinak bylo `FREE`.

---

## 5. Rezervace (Reservations)

### 5.1 Atributy rezervace

- `id`, `userId`, `spotId`, `vehicleId`
- `startTime`, `endTime` (plánované okno)
- `actualCheckIn`, `actualCheckOut` (skutečné časy, nepovinné do doby, než nastanou)
- `status`
- `price`, `paymentStatus`

### 5.2 Stavový model rezervace

| Stav rezervace | Popis |
|---|---|
| `PENDING` | Rezervace odeslána, čeká na potvrzení (platba / autorizace / kontrola kolize). |
| `CONFIRMED` | Rezervace potvrzena, místo je v čase okna nastaveno na `RESERVED`. |
| `ACTIVE` | Rezervace právě probíhá — uživatel provedl check-in, místo je `OCCUPIED`. |
| `COMPLETED` | Rezervace řádně ukončena check-outem v rámci okna (nebo s prodloužením). |
| `CANCELLED` | Rezervace zrušena uživatelem nebo systémem před začátkem okna. |
| `EXPIRED` | Rezervace nebyla využita (uživatel nedorazil) — časové okno uplynulo bez check-inu (no-show). |
| `NO_SHOW` | Alternativně evidované jako speciální případ `EXPIRED` s dopadem na storno poplatek / reputaci uživatele. |

### 5.3 Přechody stavů (zjednodušeně)

```
PENDING ──(potvrzení/platba)──► CONFIRMED ──(check-in)──► ACTIVE ──(check-out)──► COMPLETED
   │                                │                         │
   └──(zrušení)──► CANCELLED ◄──────┘                         └──(vypršení bez check-outu)──► (OVERSTAY na místě)
                                    │
                                    └──(uplynutí bez check-inu)──► EXPIRED / NO_SHOW
```

### 5.4 Business pravidla (návrh, upřesní se dle domény zvolené týmem)

1. Jedno parkovací místo může mít v jednom čase **maximálně jednu aktivní/potvrzenou rezervaci** — kolize časových oken musí být systémem detekována a odmítnuta.
2. Rezervaci lze zrušit pouze ve stavu `PENDING` nebo `CONFIRMED`, a to do určité doby před `startTime` (storno politika dle tarifu).
3. Pokud uživatel nedorazí do `startTime + grace period`, rezervace přechází do `EXPIRED`/`NO_SHOW` a místo se uvolňuje zpět na `FREE`.
4. Pokud vozidlo zůstane po `endTime` bez prodloužení, místo přechází do `OVERSTAY` a systém eskaluje (penalizace, notifikace, případně blokace další rezervace uživatele).
5. Rezervaci na místo typu `EV_CHARGING` nebo `DISABLED` smí vytvořit pouze uživatel/vozidlo splňující odpovídající kritérium (typ vozidla, ZTP průkaz) — validace při vytváření rezervace.
6. Kapacita a dostupnost oblasti (`FULL`/`ACTIVE`) se vždy odvozuje agregací aktuálních stavů jednotlivých míst, nikdy se needituje ručně.

---

## 6. Klíčové operace (use cases) systému

| # | Operace | Popis |
|---|---|---|
| 1 | **Vyhledat dostupná místa** | Filtrování podle oblasti, typu místa, časového okna. |
| 2 | **Vytvořit rezervaci** | Zamluvení konkrétního místa na časové okno, kontrola kolizí a business pravidel. |
| 3 | **Zrušit rezervaci** | Přechod do `CANCELLED`, uvolnění místa. |
| 4 | **Check-in (příjezd)** | Potvrzení fyzického příjezdu — `CONFIRMED → ACTIVE`, místo `RESERVED → OCCUPIED`. |
| 5 | **Check-out (odjezd)** | Ukončení pobytu — `ACTIVE → COMPLETED`, místo `OCCUPIED → FREE`, výpočet finální ceny. |
| 6 | **Prodloužit rezervaci** | Posun `endTime`, pokud navazující čas není obsazen jinou rezervací. |
| 7 | **Správa parkovacích oblastí** (admin) | CRUD nad oblastmi — kapacita, ceník, otevírací doba, stav. |
| 8 | **Správa parkovacích míst** (admin) | CRUD nad místy, ruční přepnutí do/z `OUT_OF_SERVICE`. |
| 9 | **Zobrazení obsazenosti v reálném čase** | Agregovaný přehled volných/obsazených/rezervovaných míst v oblasti. |
| 10 | **Notifikace** | Upozornění na blížící se konec rezervace, no-show, overstay. |

---

## 7. Engineering spike (C01)

Z výše uvedeného rozsahu tým vybírá **jednu konkrétní engineering spike**, kterou v C01 skutečně technicky provede (např. ověření datového modelu stavů místa/rezervace, průchozí walking skeleton jedné operace typu "vytvořit rezervaci", nebo ověření detekce kolizí časových oken). Výběr, průběh a výsledek spike se zaznamená jako evidence dle Definition of Done.

---

## 8. Definition of Done pro C01

- [ ] Založené společné repo se strukturou projektu.
- [ ] Project Frame (cíl, rozsah, mimo rozsah, předpoklady) zdokumentován.
- [ ] Doména rezervačního systému jasně vymezena (viz sekce 2–6 výše, upravit dle rozhodnutí týmu).
- [ ] Proběhla alespoň jedna review smyčka (peer review / code review záznam).
- [ ] Jedna engineering spike skutečně provedena a zdokumentována (viz sekce 7).
- [ ] Evidence průběhu (poznámky, commity, zápis z cvičení) uložena v repu.
- [ ] Definovaný walking skeleton pro CP1 (minimální end-to-end průchod systémem).

---

## 9. Tým

| Role | Jméno | Zodpovědnost |
|---|---|---|
| _doplnit_ | | |
| _doplnit_ | | |
| _doplnit_ | | |
| _doplnit_ | | |
