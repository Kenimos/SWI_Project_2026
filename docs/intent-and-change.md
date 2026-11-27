# Project Frame

## Reservation domain
Reservation of parking spots (`ParkingSpot`) within parking areas (`ParkingArea`) — a garage floor, a section of an open lot, or a street zone.

## Purpose
The system lets drivers find a free parking spot and reserve it in advance for a specific time window, so that on arrival they are sure a spot is available. It gives parking managers an overview of occupancy and lets them manage parking areas and spots.

## Users / Stakeholders
- **Driver** — searches for available spots, creates, confirms and cancels reservations.
- **Parking manager** — manages parking areas and spots, monitors occupancy.
- **Notification Service** *(external system, not a direct user)* — receives triggers to send notifications.

## Core concepts
- **Reservation**
- **Resource** (`ParkingSpot`)
- **User**
- **ParkingArea** — the parent unit the spot belongs to
- **Vehicle** — the vehicle assigned to the reservation (because of the spot type, e.g. EV charging)

## Core operations
- Create reservation
- Confirm / approve reservation
- Cancel reservation
- Check availability

## Persistent state
**Reservation:** `id`, `spotId`, `userId`, `startTime`, `endTime`, `state`

**Resource (ParkingSpot):** `id`, `areaId`, `type`, `state`

## State-changing operation
`DRAFT → CONFIRMED` (reservation confirmation)

## Common business rule
Confirmed reservations for the same resource must not overlap.

## Domain-specific business rule
A reservation of a parking spot of type `EV_CHARGING` or `DISABLED` may be created only by a user whose vehicle/permit matches that type (electric vehicle, disabled parking permit) — checked when the reservation is created.

## External / system boundary
**Notification Service** — informs users about a reservation confirmation, the upcoming end of a reservation, or its cancellation.

## Assumption
We assume that every parking spot always belongs to exactly one parking area and cannot be shared between several areas.

## Unknown
We do not know yet whether pricing/payment will be part of the project scope or handled by an external billing system.

---

## Selected future pressure

**Category:** Q — Quality / Scale

**Concrete pressure:** During peak traffic (e.g. a big event or a stadium near the parking lot) there may be up to 10× more simultaneous attempts to reserve the same limited set of spots within a few seconds.

**Why it is relevant to our reservation system:** The common rule requires that two confirmed reservations of the same spot never overlap. With high concurrency the risk of a race condition between the availability check and writing the reservation (double booking of the same spot) grows. This affects the design of the persistence layer (transactions, unique database constraints) — it is not implemented in C01 yet; we only record it as a future pressure on the architecture.

---

## C08 / CR-01 — Controlled Change Card: releasing the spot on a no-show

**Card status:** PROPOSED. The decision below is the author's proposal; it becomes valid only after review (see *Peer reviewer*).

**Request source and concrete project need:**
Parking manager (role `Approver`): "When a driver with a confirmed reservation does not arrive, the spot stays blocked until the end of the reservation and I cannot offer it to anyone else." Benefit: better use of limited capacity, which is exactly the situation of future pressure Q (peak, few spots).

**Before commit/tag + spec/BR/ADR + schema/config:**
- Code: tag `c08-before-v0.2.1`, i.e. the state after the internal audit fixes (PR `fix/spot-state-audit`). The original tag `c08-before` (`8a5b312`) is historical: the audit found spot-state defects D-01 to D-10 on it, so it would not be a fair starting point for a check-in change.
- Specification: Baseline v0.2.1, `docs/specification.md`: BR-01 (interval `[start, end)`), BR-02 (no overlap of `CONFIRMED`), BR-03 (Cancel only before `start`), BR-07 (occupancy bound to a reservation), OP-07 (check-in/out).
- Architecture: ADR-004, ADR-006, ADR-007 (`ReservationService` owns all state transitions, serialization via `SpotLocks`).
- Schema: latest migration `20261127163915_SpotStateSingleSourceAndCheckIn` (contains `Reservation.CheckedInAt`).
- Configuration: `Reservations:ApprovalWindowHours` (default 24 h), SQLite `Data Source=parking.db`, no secrets.

**Old accepted behavior / observable limitation** (verified by TC-C08-03, `docs/evidence-and-evolution.md`):
- A `CONFIRMED` reservation without check-in blocks the spot for the whole `[start, end)`; Check Availability returns `available: false`.
- The driver can no longer cancel it after `start` (`too_late`, BR-03); the Approver has no way to cancel it.
- Check-in is allowed during the whole window `[start, end)` (BR-07): it succeeds even 2 h 20 min after the start, so the system cannot detect a no-show.

**New intended behavior, scope, out of scope:**
- If a `CONFIRMED` reservation is not checked in by `start + G` (grace period, proposed `G = 15 min`, configurable), the reservation is treated as a **no-show (`NO_SHOW`)**. It stops blocking the spot and a late check-in is rejected.
- Scope: reservation state model, Check-in, overlap calculation in Check Availability (spot and area), Confirm, Approve.
- Out of scope: notifying the driver (the Notification Service does not exist), no-show fees, changing the BR-03 cancellation policy, a proactive worker (decided among the alternatives below).

**Acceptance conditions:**

| AC | Type | Condition |
|---|---|---|
| AC-01 | positive | Check-in of a `CONFIRMED` reservation at `start + 14:59` succeeds; the spot is `OCCUPIED`, the reservation stays `CONFIRMED`. |
| AC-02 | negative | Check-in at `start + 15:01` is rejected; the reservation is permanently `NO_SHOW` and the spot is not `OCCUPIED`. |
| AC-03 | boundary | Exactly at `start + 15:00` it is already a no-show and check-in is rejected (same `≥` convention as approval expiry, REQ-08). |
| AC-04 | positive | After `start + G` without check-in, Check Availability returns `available: true` for the rest of the window and another reservation overlapping that rest can be confirmed. |
| AC-05 | regression | Before `start + G`, or after a successful check-in, the reservation still behaves as `CONFIRMED`: an overlapping Confirm/Approve is rejected (BR-02). Create, Cancel, Approve/Reject and authorization do not change (TC-C08-01/02 and the existing 66 tests). |

**Impact analysis method / evidence reviewed:**
We went from AC-02/AC-04 to the owner of the decision (`ReservationService`, ADR-006). Then we went through every place that reads `State == Confirmed` (`grep` in `backend/`: Check Availability, area availability, overlap in Confirm and Approve, Check-in/out, Cancel), the data model (`Reservation`, migrations) and the tests.

| Dimension / exact artifact | CHANGE/CHECK/NO IMPACT + why | Needed delta/verification | Owner/evidence |
|---|---|---|---|
| Spec/BR/state: `docs/specification.md` BR-02, BR-07, OP-07, 3b | CHANGE: new rule BR-08 (no-show), new terminal state `NO_SHOW`; BR-02 must state that a no-show does not block; the check-in window in OP-07 narrows to `[start, start + G)` | BR-08, updated REQ-11, transition `CONFIRMED → NO_SHOW` in the state diagram, baseline v0.3 | Robin; spec review |
| Architecture/ownership: ADR-006, `SpotLocks` | CHECK: the no-show decision must be owned by `ReservationService` (the only state writer, conformance script) | `check-architecture-conformance.sh` must still pass | Robin; script run |
| Interfaces: `POST /reservations/{id}/check-in` | CHANGE: new error `no_show` (409) instead of `outside_reservation_window` after `start + G` | HTTP integration test | Robin; `ApiEndpointTests` |
| Interfaces: `GET .../availability` (spot and area) | CHANGE (semantics, not signature): a no-show reservation does not count as an overlap | Test AC-04 | Robin |
| Detailed design/code: `ReservationService` (`HasBlockingOverlapAsync`, `CheckInAsync`) | CHANGE: extend the existing shared predicate `HasBlockingOverlapAsync` (used since v0.2.1 by OP-02, area availability, Confirm and Approve) with the no-show condition + lazy detection in check-in | Unit tests with `FakeClock` at 14:59 / 15:00 / 15:01 | Robin |
| Detailed design/code: Cancel, Reject, Create | CHECK: Cancel after `start` is already `too_late`, so it does not change | Regression of the existing tests | Robin |
| Data/schema/migration: `Reservation` | CHECK: `CheckedInAt` exists since v0.2.1 (the backfill of valid check-ins is in migration `SpotStateSingleSourceAndCheckIn`); the state is stored as text, so `NoShow` needs no new schema | Verify that existing rows without `CheckedInAt` after `start + G` behave as a no-show (intended) | Robin; test on existing rows |
| Configuration: `appsettings.json` | CHANGE: `Reservations:NoShowGraceMinutes` (default 15) | Document in the README, unit minutes, times in UTC | Robin |
| CI/release | CHECK: no CI yet, handled in C09 | CI job `dotnet test` (C09) | Robin / C09 |
| Tests/integration/risks | CHANGE: new tests AC-01–AC-04, regression AC-05 | Write the test basis **before** implementation (below) | Robin; Marek review |
| Operation/recovery | CHECK: an application rollback does not turn `NO_SHOW` back into `CONFIRMED`; an older version could not read the new state | Record as a known limitation in the C09 release record | Robin |
| Notification Service | NO IMPACT: the write path does not exist, the boundary is only defined so far | — | — |
| Admin/import/worker paths | NO IMPACT (option A): no such write path exists in the system | — | — |

**Alternatives and comparable criteria:**

| Option | Meets AC? | Benefit | Cost/failure/rollback | Verification |
|---|---|---|---|---|
| A: lazy detection. Predicate `HasBlockingOverlapAsync` = `Confirmed && (CheckedInAt != null \|\| now < start + G)`; check-in after the deadline stores `NO_SHOW` | AC-01–AC-05 yes. The `NO_SHOW` state appears in the DB only on a check-in attempt, but availability works immediately | Small change in the existing owner, no new process, same pattern as REQ-08 (EXPIRED) | The reservation stays `Confirmed` in the DB until someone touches it; reports "by state" show it wrong. Rollback: no schema change, but an older version cannot read `NO_SHOW` rows | FakeClock boundary tests + HTTP integration test + regression |
| B: worker + guard. A `BackgroundService` marks `NO_SHOW` every minute + the same guard in check-in | Yes, the DB state is current within ~1 min | The stored state matches reality even without interaction | New process, race between the worker and check-in (must take `SpotLocks`), testing time and restarts, duplicate runs with multiple instances | Worker tests, restart, check-in vs. worker race |
| C: no code change, process only. The manager handles no-shows manually | No: AC-02/AC-04 not met, no path for the manager exists in the system | Zero change | The need stays unresolved, the spot is blocked until the end of the reservation | — |

**Inaction consequences / feasible compensation:** without the change, a no-show spot stays blocked until `end` and nobody can release it, because Cancel after `start` is forbidden and the Approver has no way to intervene. At peak times (pressure Q) this directly reduces capacity.

**Peer reviewer and challenged assumption:** *to be filled in by the reviewer:* Robin Tokarský. Challenge mainly: (1) is `G = 15 min` acceptable for the manager? (2) is it enough that `NO_SHOW` is written to the DB lazily, or is a proactive state required?

**Decision ACCEPT / REJECT / DEFER / NO_CHANGE, owner, date, reason:**
PROPOSED: **ACCEPT, option A**. Owner: Jan Ječmínek, 27 Nov 2026. Reason: it meets all AC with the smallest change in the existing owner of the decision. It reuses the proven lazy-expiry pattern of REQ-08 and adds no new operational process. The limitation of option A (the stored state is updated only on access) is acceptable, because availability is decided by the predicate, not by the stored state. *The reviewer confirms the final decision.*

**If ACCEPT: smallest change and acceptance tests to perform in C09:**
1. `ReservationState.NoShow` and configuration `Reservations:NoShowGraceMinutes` (no migration, `CheckedInAt` already exists).
2. Extend `HasBlockingOverlapAsync` in `ReservationService` with the no-show condition; thanks to v0.2.1 it takes effect in OP-02, area availability, Confirm and Approve at once.
3. Check-in (already runs under `SpotLocks` and sets `CheckedInAt`): if `now ≥ start + G`, store `NoShow` and return `no_show`; the spot state stays `Free`.
4. Spec v0.3 (BR-08, updated REQ-11, state diagram), README.
5. Tests (test basis, expected results set now, before implementation):

| Test ID | Setup | Action | Expected |
|---|---|---|---|
| TC-C09-01 (AC-01) | B1 `CONFIRMED` [10:00, 12:00), spot `Free`, FakeClock 10:14:59 | check-in B1 | OK, spot `Occupied`, B1 `Confirmed`, `CheckedInAt` = 10:14:59 |
| TC-C09-02 (AC-03) | same, FakeClock 10:15:00 | check-in B1 | `no_show`; after readback from a new DbContext B1 `NoShow`, spot `Free`, `CheckedInAt` null |
| TC-C09-03 (AC-02) | same, FakeClock 10:15:01 | check-in B1 | `no_show`; B1 `NoShow`, spot `Free` |
| TC-C09-04 (AC-04) | B1 `CONFIRMED` [10:00, 12:00) without check-in, B2 `DRAFT` [11:00, 12:00), FakeClock 10:20 | availability [11:00, 12:00), then confirm B2 | `available: true`; B2 `Confirmed`; B1 never blocking at the same time as B2 |
| TC-C09-05 (AC-05) | B1 `CONFIRMED` [10:00, 12:00) checked in at 10:05, B2 `DRAFT` [11:00, 12:00), FakeClock 10:20 | confirm B2 | `overlap`, B2 stays `Draft` |
| Regression | — | TC-C08-01, TC-C08-02, the full test suite, the conformance script | results unchanged |

**If NO_CHANGE/REJECT:** N/A.

**Open uncertainty and next trigger:** the value of `G` (to be confirmed with the manager). If a requirement appears for reservation reports by stored state or for a no-show notification, reconsider option B.
