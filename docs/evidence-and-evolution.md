# C01 Engineering Spike

Variant: **A — Persistence** (issue #1)

Question / unknown:
Can we store a `Reservation` through EF Core in a real SQLite database, read it back from another `DbContext` and get the same values (including state and times)? Does the whole chain packages → `DbContext` → migrations → `.db` file work, or do we hit a version problem (.NET 8 SDK vs. the global `dotnet-ef` 9.0.9)?

What we did:
- Added a minimal `Reservation` entity (`Id`, `SpotId`, `UserId`, `StartTime`, `EndTime`, `State`) and the `ReservationState` enum (`Draft`, `Confirmed`, `Cancelled`) to `backend/Domain/`.
- Added `ParkingDbContext` (`backend/Data/`); the state is stored as text (`HasConversion<string>()`). Registered it in `Program.cs` via `UseSqlite` with the connection string from `appsettings.json`.
- Generated a migration: `dotnet ef migrations add InitialCreate` (folder `backend/Migrations/`).
- Wrote two xUnit tests (`tests/ParkingReservation.Api.Tests`): each uses a temporary `.db` file, creates the schema via `Database.Migrate()`, stores a reservation in one `DbContext` and reads it in a second, new one.
- Manually ran `dotnet ef database update` against a real `parking.db` and inspected the schema with `sqlite3`.

Observed result:
- `dotnet test tests/ParkingReservation.Api.Tests` → `Passed! Failed: 0, Passed: 2, Skipped: 0, Total: 2`.
- Test 1: the stored reservation (`SpotId=42`, `UserId=7`, `State=Confirmed`, start/end in UTC) is loaded in a new context with the same values and `Id > 0` (assigned by the DB).
- Test 2: changing the state `Draft → Confirmed` and calling `SaveChanges()` is really persisted in the DB.
- `dotnet ef database update` succeeded (the global `dotnet-ef` 9.0.9 works with the EF Core 8.0.10 packages); the table `Reservations` (`Id` INTEGER PK AUTOINCREMENT, `SpotId`, `UserId`, `StartTime` TEXT, `EndTime` TEXT, `State` TEXT) and `__EFMigrationsHistory` were created.
- SQLite stores `DateTime` as TEXT; the tests keep times in UTC so the values come back exactly.

Decision / what changes because of the result:
- Persistence through EF Core + SQLite works; we stay with this stack (ADR-001 holds).
- The schema is managed only through EF migrations (`dotnet ef migrations add ...` / `database update`); `parking.db` is not committed, `Migrations/` is.
- Times are always stored and compared in UTC.
- The reservation state is stored as text for readability in the DB.
- *(state at the time of the C01 spike; foreign keys, `User`, `ParkingSpot` and the overlap check were added later, see ADR-003)* `SpotId` and `UserId` are plain numbers without foreign keys for now; the `ParkingSpot` and `User` entities will be added once the team agrees on the domain. The reservation overlap check (common rule) is **not** verified by this spike — it remains for later iterations (related to the selected future pressure Q).

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

Current verification status (C05, commit `eaae325`): the backend and test project compiled with .NET SDK 8 and the full suite passed with 53 tests, 0 failures, and 0 skips. After the v0.2.1 audit fixes the suite has 66 tests, see *C07 — Internal audit* below.

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

The assessment by another team (Kirnig, Kalus, Gala, Carnevale) is in `docs/REPORT.md`. The assessed baseline was `45351cd`; the fixes are verified on `0f039b6`.

| Finding | Verify | Disposition | Closure evidence |
|---|---|---|---|
| F1: Cancel after a concurrent Confirm cancelled a confirmed reservation (contradiction OP-04 / BR-03) | VERIFIED FINDING (reproduced by inspection on `45351cd`) | ACCEPT. Strict interpretation: a Cancel that observed `DRAFT` and finds `CONFIRMED` after the lock returns `Conflict` | `eaae325`: `ReservationService.CancelReservationAsync`, test in `ReservationConfirmCancelTests.cs` (asserts `Conflict`), 53/53 passed |
| F2: `ParkingSpot` had no `State` although the documentation described the states | VERIFIED FINDING | ACCEPT. Spot state implemented | `eaae325`: `ParkingSpotState`, migration `AddUsersParkingAreasAndSpotState`, `ParkingAreaAndSpotStateTests.cs` |
| F3: C04 Quality & Measurement missing | REJECTED CANDIDATE (by the assessors and the team) | REJECT, no-change | C04 had no separate assignment, Q1–Q3 = N/A |

Open issues from the assessment: none of F1–F3 remains open. The `SpotLocks` risk (single instance only) remains consciously accepted in ADR-006.

## C07 — Internal audit of docs ↔ code consistency (27 Nov 2026)

After C06 we went through the whole project ourselves, comparing the specification, README and presentation with the code. Suspicious cases were verified through the running API (`dotnet run`, isolated SQLite) and by reading the state directly from the DB. Audited version `8a5b312` (tag `c08-before`). In terms of C07 B1 these are **defects/issues** (demonstrated nonconformity), not risks.

| ID | Claim (source) | Observed on `8a5b312` | Disposition | Closure evidence |
|---|---|---|---|---|
| D-01 | README: an `OUT_OF_SERVICE` spot "is never offered as available" | out-of-service → OP-02 `{"available":true}`, Confirm `Confirmed` 200, spot state overwritten to `Reserved`, restore then `invalid_state` | ACCEPT, fixed (BR-06) | `Out_of_service_spot_is_unavailable_and_cannot_be_confirmed` |
| D-02 | REQ-02: one availability rule | area availability ignored `IsActive` (a spot with `IsActive=0` reported as available), OP-02 ignored `OutOfService` | ACCEPT, fixed (one method `IsSpotAvailableAsync`) | `Spot_and_area_availability_agree_and_area_reports_reserved` |
| D-03 | README: the spot state matches the reservation | two `CONFIRMED` reservations on one spot, cancelling the first → spot `Free` → check-in of the second `invalid_state` 409 | ACCEPT, fixed (BR-07, `CheckedInAt`) | `Cancelling_one_reservation_keeps_check_in_of_another_possible` |
| D-04 | OP-02 v0.2: `PENDING_APPROVAL` does not block | pending set the spot to `Reserved`, Reject did not release it → out-of-service `invalid_state` | ACCEPT, fixed | `Pending_and_rejected_reservations_do_not_lock_spot` |
| D-05 | README: check-in = arrival for a confirmed reservation | check-in succeeded years before `start`; the following Cancel returned `invalid_state` (not in the spec); after check-out the reservation still blocked the window | ACCEPT, fixed (window `[start, end)`, state `Completed`) | `Check_in_only_inside_reservation_window` (4 boundaries), `Check_out_completes_reservation_and_releases_rest_of_window`, `Check_in_before_start_returns_outside_reservation_window` (HTTP) |
| D-06 | Spec: operations for spot management and check-in/out | implemented in `eaae325` without a specification; the authorization TBDs did not match the code | ACCEPT, added: spec v0.2.1 (section 0, BR-06, BR-07, OP-06, OP-07) | `docs/specification.md` section 6 |
| D-07 | Spec section 0: all times are UTC | a time with an offset (`+01:00`) was stored as local time | ACCEPT, fixed (`ToUtc`) | `Create_normalizes_local_time_to_utc` |
| D-08 | ADR-006 conformance rule | the script checked only `backend/Services` and its regex also matched `==` | ACCEPT, fixed | script exit 0 on a clean tree, exit 1 with a planted assignment |
| D-09 | README/presentation/evidence | the README described non-existent attributes (`location`, `spots`, composite ID), the presentation claimed "Physical state … verified", spec section 5 carried a "Draft" header, and the C01 spike paragraph was out of place in the evidence | ACCEPT, fixed | PR `fix/spot-state-audit` |
| D-10 | README: API | enums were accepted in JSON only as numbers; `dotnet ef` ignored `ConnectionStrings__DefaultConnection` | ACCEPT, fixed (`JsonStringEnumConverter`, design-time factory) | `Enum_values_are_accepted_as_strings` |

Migration `SpotStateSingleSourceAndCheckIn`:
- converts `IsActive=0` to `OutOfService`;
- changes `Reserved` to `Free`;
- adds `CheckedInAt` to a valid check-in;
- returns `Occupied` without a valid check-in to `Free`.

This is verified by `Migration_backfills_spot_state_and_check_in`, which migrates from the previous schema version with data.

Result after the fix: `dotnet test` **66 passed, 0 failed, 0 skipped**, the conformance script passed.

Consciously left open:
- a no-show still blocks the spot until `end`, which CR-01 addresses (C08);
- taking a spot out of service does not cancel its future `CONFIRMED` reservations (OP-06 assumption, related to the missing Notification Service);
- `RequiresApproval` cannot be set through the API;
- overstay is not modelled.

## C08 — Before baseline and test cases

> **The valid before baseline for CR-01 is tag `c08-before-v0.2.1`** (after the internal audit fixes); the TC re-run is below in *C08 — Re-run on `c08-before-v0.2.1`*. The following record from tag `c08-before` is the historical first run.

**Historical before baseline:** tag `c08-before` (code identical to `0f039b6`, spec Baseline v0.2 BR-01/BR-02/BR-03, ADR-004/ADR-006), schema `20261025151543_AddUsersParkingAreasAndSpotState`, runtime .NET SDK 8 (EF Core 8.0.10). The test DB was an isolated SQLite file created with `dotnet ef database update --connection "Data Source=<tmp>/tc.db"`, the application ran as `dotnet run --no-build --urls http://localhost:5299` with `ConnectionStrings__DefaultConnection` pointing to that file. No secrets. Seed: `Users(1, Driver)`, `Users(9, Approver)`, `ParkingAreas(1)`, `ParkingSpots(1, Standard, IsActive=1, RequiresApproval=0, Free)`. Before each TC the DB was restored from an empty post-migration copy. Executed 27 Nov 2026 ~16:23 UTC by Jan Ječmínek.

The authoritative state is read directly from SQLite (`sqlite3`), not only from the HTTP response. BR-02 control query:
`SELECT count(*) FROM Reservations a JOIN Reservations b ON a.Id<b.Id AND a.SpotId=b.SpotId AND a.State='Confirmed' AND b.State='Confirmed' AND a.StartTime<b.EndTime AND b.StartTime<a.EndTime;`

### C08 / TC-C08-01 — adjacent reservations can both be confirmed

Test basis: BR-01 (`[start, end)`), BR-02, OP-03 @ v0.2. Risk: N/A.

| # | Action / input | Expected BEFORE execution | Actual AFTER execution | Evidence |
|---|---|---|---|---|
| 1 | Reset DB, seed; Create + Confirm B1 [10:00, 11:00), Create B2 [11:00, 12:00) (2030-01-01, UTC) | B1 `Confirmed`, B2 `Draft` | `{"id":1,"state":"Confirmed"}`, `{"id":2,"state":"Draft"}` | curl output |
| 2 | Readback from the DB | `1 Confirmed 10–11`, `2 Draft 11–12` | `1\|1\|10:00\|11:00\|Confirmed`, `2\|1\|11:00\|12:00\|Draft` | sqlite3 |
| 3 | `POST /reservations/2/confirm {"userId":1}` | success | `{"id":2,"state":"Confirmed"}` HTTP 200 | curl |
| 4 | Readback from the DB + BR-02 query | both `Confirmed`, 0 overlaps | both `Confirmed`, `confirmed_overlaps = 0` | sqlite3 |
| 5 | Repeatability | reset from the empty DB copy | done (same empty copy used for TC-02) | — |

Result: **PASS**.

### C08 / TC-C08-02 — an overlapping confirmation is rejected

Test basis: BR-02, OP-03 failure outcome `overlap` @ v0.2. Risk: double booking (pressure Q).

| # | Action / input | Expected BEFORE execution | Actual AFTER execution | Evidence |
|---|---|---|---|---|
| 1 | Reset DB, seed; Create + Confirm B1 [10:00, 11:00), Create B2 [10:30, 11:30) | B1 `Confirmed`, B2 `Draft` | `Confirmed`, `Draft` | curl |
| 2 | Readback from the DB | they overlap in time, only B1 is confirmed | `1 Confirmed 10:00–11:00`, `2 Draft 10:30–11:30` | sqlite3 |
| 3 | `POST /reservations/2/confirm {"userId":1}` | rejected as `overlap` (409) | `{"error":"overlap"}` HTTP 409 | curl |
| 4 | Readback after the attempt | B1 unchanged, B2 **not** `Confirmed`, 0 overlaps | B1 `Confirmed`, B2 `Draft`, `confirmed_overlaps = 0` | sqlite3 |
| 5 | Repeatability | reset from the empty copy | done | — |

Result: **PASS**. Limitation: the test runs a single request; concurrent Confirms are covered by the concurrency unit test in `ReservationConfirmCancelTests.cs`.

### C08 / TC-C08-03 — probe of the current no-show behavior (motivation for CR-01)

Test basis: behavior of baseline v0.2, not a new requirement. It shows the limitation described in CR-01.

Setup: written directly to the DB, because a reservation with `start` in the past cannot be confirmed through the API: B1 `Confirmed` [2026-11-27 14:00, 20:00) without check-in, spot `Reserved`. Run at 16:23:51 UTC, i.e. 2 h 23 min after `start`.

| # | Action | Observed |
|---|---|---|
| 1 | `GET /parking-spots/1/availability?start=17:00&end=18:00` | `{"available":false}`: spot blocked |
| 2 | Driver `POST /reservations/1/cancel` | `{"error":"too_late"}` HTTP 409 (BR-03) |
| 3 | Approver `POST /reservations/1/check-in` | `{"id":1,"state":"Occupied"}` HTTP 200: check-in has no time limit |
| 4 | Readback | `Confirmed`, spot `Occupied` |

Conclusion: a no-show cannot be resolved in the system at all; the spot stays blocked until `end`. This is the starting point for CR-01 (`docs/intent-and-change.md`).

**Note for C09 (reproducibility):** `ParkingDbContextFactory` (design-time) had `Data Source=parking.db` hard-coded, so `dotnet ef database update` ignored `ConnectionStrings__DefaultConnection` and another DB needed `--connection`. The README examples with a manual `INSERT INTO ParkingSpots` did not work on this baseline: `AreaId`/`State` and the users were missing. Fixed in the README in tag `c08-before` and verified by running the whole procedure on a clean DB.

## C08 — Re-run on `c08-before-v0.2.1`

Why again: the internal audit (D-01 to D-10) changed the spot-state and check-in code, so CR-01 is analysed against the fixed version. Procedure and oracle are the same as above. The seed has no `IsActive` column, which the migration removed. Schema `20261127163915_SpotStateSingleSourceAndCheckIn`; the test DB was created with `ConnectionStrings__DefaultConnection=... dotnet ef database update` (without `--connection`, which also verified fix D-10). Executed 27 Nov 2026 16:44 UTC by Jan Ječmínek, code of branch `fix/spot-state-audit`.

| TC | Expected (unchanged from the first run) | Actual | Result |
|---|---|---|---|
| TC-C08-01 | B2 [11:00, 12:00) is confirmed, both `Confirmed`, 0 overlaps | `{"id":2,"state":"Confirmed"}` 200; readback both `Confirmed`, spot `Free` (Reserved is no longer stored), `confirmed_overlaps = 0` | **PASS** |
| TC-C08-02 | B2 [10:30, 11:30) rejected, B2 stays `Draft`, 0 overlaps | `{"error":"overlap"}` 409; readback B1 `Confirmed`, B2 `Draft`, `confirmed_overlaps = 0` | **PASS** |

**TC-C08-03 (no-show probe) on v0.2.1:** B1 `Confirmed` [14:00, 19:00) without check-in, run at 16:44 UTC.
- Availability [17:00, 18:00) returns `{"available":false}`.
- The driver's Cancel returns `too_late` 409.
- Check-in ~2 h 44 min after the start succeeds (`Occupied`, `CheckedInAt` stored).

The CR-01 limitation therefore persists on the fixed baseline: the system cannot detect a no-show; check-in is allowed during the whole window `[start, end)`.

**Audit probes on the fixed version** (same steps as during the audit):
- D-01: out-of-service leads to OP-02 `available:false`, Confirm `spot_inactive` 409 and restore 200.
- D-05: check-in in 2026 for a reservation in 2030 returns `outside_reservation_window` 409.
- D-04: Reject followed by out-of-service returns 200.
- D-02: the area reports `Free/true`, `Reserved/false` and `OutOfService/false`.
