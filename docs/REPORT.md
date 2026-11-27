# C06 — Assessment Form

> The assessment was written in Czech by the assessing team. This is an English translation made by the assessed team; the original Czech text is preserved unchanged in commit `0f039b6` (`git show 0f039b6:docs/REPORT.md`). Line references point to the files at the assessed commit `45351cd`.

## Project

```text
Assessing team: Tomáš Kirnig, Martin Kalus, Petr Gala, Dennis Carnevale
Assessed team: Jan Ječmínek, Marek Šikula, Robin Tokarský
Project: Parking Reservation System (SWI_Project_2026)
Assessed commit/tag: 45351cd (45351cddf443e10a72083e54f56453269d68e0fc)
Date: 23 Oct 2026
```

## States

`YES / PARTLY / NO / N/A`

Every row must contain Evidence.

## Baseline

| ID | Answer | Evidence / comment |
|---|---|---|
| B1 Same identified baseline for docs/code/tests | PARTLY | The code (`backend/Services/ReservationService.cs`), tests (`tests/`) and specification (`docs/specification.md`) correspond to Baseline v0.2 (commit `45351cd`). However, `README.md` section 9 is outdated (it still shows the C01 state: "not implemented"). |
| B2 The relevant product or tests can be run | YES | `dotnet test tests/ParkingReservation.Api.Tests` passes without errors: 41 passed, 0 failed (1.0 s). DB migrations run automatically into a temporary SQLite. |

## C02 — Engineering Specification

Selected operation: OP-03 — Confirm Reservation  
Selected business rule / invariant: BR-02 — Exclusive Resource invariant (`[start, end)` half-open intervals)

| ID | Answer | Evidence / comment |
|---|---|---|
| R1 Operation: input + precondition + postcondition/state | YES | `docs/specification.md` (lines 165–275, v0.2 lines 582–600). Input: `id`, `userId`. Preconditions: state DRAFT, `now < start`, active spot. Postcondition: state `CONFIRMED` / `PENDING_APPROVAL`, slot allocation. |
| R2 The business rule/invariant is unambiguous | YES | `docs/specification.md` (lines 26–30). BR-02 forbids two overlapping `CONFIRMED` reservations on the same spot. Half-open intervals `[start, end)` rule out a collision when boundaries touch. |
| R3 Operation/rule is consistent with the state model | YES | `docs/specification.md` (diagrams 3b and 3d). The transitions `DRAFT -> CONFIRMED` (with `RequiresApproval=false`) and `DRAFT -> PENDING_APPROVAL` (with `RequiresApproval=true`) match the specification exactly. |
| R4 The implementation matches the specified semantics | YES | `backend/Services/ReservationService.cs` (`ConfirmReservationAsync`, lines 119–194). It checks the state, time, activity, the `RequiresApproval` branch and the DB overlap (`r.StartTime < end && start < r.EndTime`). |
| R5 Suitable verification evidence exists | YES | `tests/ParkingReservation.Api.Tests/ReservationConfirmCancelTests.cs` (lines 66–179). 8 tests verify success, collision, touching boundaries, inactive spot, expiry and parallel concurrency. |

## C03 — Architecture

Selected responsibility: Reservation management and serialization of spot allocation (`ReservationService` + `SpotLocks`)  
Selected interaction: Confirm Reservation execution path (including the approval branch and locks)  
Selected ADR/decision: ADR-004: Confirm/Cancel concurrency (OP-03, OP-04)

| ID | Answer | Evidence / comment |
|---|---|---|
| A1 Responsibility + owner are explicit | PARTLY | The layers are described in `README.md` (section 6) and ADR-004. However, a formal C03 architecture document (structural / component view) is missing. |
| A2 Actual code ownership matches the architecture | YES | The logic and serialization are owned by `backend/Services/ReservationService.cs` with `SpotLocks.cs`. Data is managed by `ParkingDbContext.cs`, HTTP is mapped by `Program.cs`. |
| A3 Dynamic interaction matches the actual call path | PARTLY | The call path in the code follows activity diagram 3d (`docs/specification.md`) exactly. However, a sequence diagram capturing the component interaction is missing. |
| A4 ADR/decision matches the current implementation | YES | ADR-004 defines serialization via `SemaphoreSlim` and `IClock`. Implemented in `backend/Services/SpotLocks.cs` and `backend/Services/IClock.cs`. |

## C04 — Quality & Measurement

Quality concern: N/A — not assessed.  
Criterion: N/A — not assessed.

Justification: The C06 assignment requires assessing C04, but the corresponding task is missing from the supplied exercise assignments (CV01–CV03, CV05). This is an inconsistency in the source materials. According to the Important rule of C06 ("Do not check anything the previous assignments did not require"), this part is not checked and its absence is not considered a project defect.

| ID | Answer | Evidence / comment |
|---|---|---|
| Q1 The criterion is actually evaluable | N/A | Not checked: the requirement in the C04 part of assignment 6 has no corresponding assignment in the supplied materials CV01–CV05. |
| Q2 The measurement measures the criterion in the right context | N/A | Not checked for the same reason; see the justification of the source-material inconsistency above. |
| Q3 The decision matches the measurement result | N/A | Not checked for the same reason; see the justification of the source-material inconsistency above. |

## C05 — Detailed Design & Unit Verification

Selected software unit: ReservationService

| ID | Answer | Evidence / comment |
|---|---|---|
| D1 Design intent is explicit and understandable | YES | `docs/specification.md` (sections 2 and 5) and the comments in `ReservationService.cs`. The intent of orchestration, validation and locking is entirely clear. |
| D2 DD matches the actual implementation | YES | The service methods match the contracts in `backend/Contracts/` and the typed error returns in `ReservationResults.cs` exactly. |
| D3 Unit/component tests can be derived from DD | YES | The tests in `tests/` directly mirror the "Verification examples" sections of the specification (e.g. OP-02 interval queries in `ReservationServiceTests.cs`). |
| D4 Relevant branches/boundaries/states/failures are verified | YES | Verified by 41 tests: touching boundaries, zero length, times before/after start, active/inactive spots, all 6 reservation states and the error codes. |
| D5 The selected test has a meaningful oracle/expected result | YES | The test `Confirm_succeeds_when_intervals_only_touch_at_boundary` verifies the half-open intervals according to rule BR-01, not by copying the code. |

## Candidate findings

### F1

```text
Source: C02
Engineering claim: docs/specification.md (lines 334–336 and diagram 3c) states: if Confirm and Cancel race on the same reservation and Confirm wins first, Cancel must be rejected as a conflict (the state stays CONFIRMED).
Expected: After the reservation is confirmed, the competing Cancel must be rejected.
Observed: In backend/Services/ReservationService.cs (lines 228–262), after acquiring the lock Cancel finds the state CONFIRMED, but because the transition CONFIRMED -> CANCELLED is generally allowed (line 255), it cancels the reservation and returns success. Note: the specification itself contradicts itself here — lines 310–312 and the verification example on line 340 explicitly allow the transition CONFIRMED -> CANCELLED, while the failure outcome on lines 334–336 says that Cancel after a concurrent Confirm must be rejected. The code follows the more permissive interpretation (the main definition of state changes + verification examples).
Evidence: docs/specification.md (lines 334–336, diagram 3c step 463) vs. backend/Services/ReservationService.cs (lines 228–262).
Verification performed: Code inspection of ReservationService.cs: there is no check of the original state / concurrency token; Cancel after Confirm always succeeds.
Result: VERIFIED FINDING
```

### F2

```text
Source: C01 / C02
Engineering claim: README.md (sections 1 and 5) and docs/intent-and-change.md (line 30) claim that ParkingSpot has the states FREE, RESERVED, OCCUPIED, OUT_OF_SERVICE and that confirming a reservation sets the spot state to RESERVED.
Expected: ParkingSpot has a state attribute and the system changes it when a reservation is confirmed.
Observed: backend/Domain/ParkingSpot.cs has no State attribute (only IsActive and RequiresApproval). The physical state is not tracked; occupancy is computed dynamically from the Reservations table. Note on impact: this discrepancy does not threaten the functionality of the system — computing availability dynamically from reservations is even cleaner from a design point of view (Single Source of Truth, removes redundancy and the risk of the spot state getting out of sync). It is unresolved documentation debt from the C01 phase.
Evidence: README.md (lines 32–41, 99) vs. backend/Domain/ParkingSpot.cs (lines 8–22) and the EF migrations.
Verification performed: Inspection of the DB schema in backend/Migrations/: the ParkingSpots table has no State column; operations OP-01..05 work correctly without it.
Result: VERIFIED FINDING
```

### F3

```text
Source: C04 — inconsistency of the exercise materials
Engineering claim: The original suspicion was that the project is missing the mandatory C04 output (Quality & Measurement).
Expected: Only requirements actually assigned in the previous exercises can be assessed (Important rule of C06).
Observed: The C06 assignment assumes C04, but the supplied exercise assignments do not contain the corresponding task.
Evidence: C06 instructions (Important rule: C06 must not add requirements retroactively); form Q1–Q3; supplied assignments CV01–CV05.
Verification performed: Comparison of the exercise assignments with the assessment instructions; an obligation to submit C04 was not demonstrated.
Result: REJECTED CANDIDATE — not a project defect; Q1–Q3 = N/A.
```

## Owner disposition

| Finding | ACCEPTED / REJECTED / DEFERRED | Reason/evidence | Action / no-change |
|---|---|---|---|
| F1 | ACCEPTED | VERIFIED FINDING, reproduced on `45351cd`: after taking the lock, `CancelReservationAsync` also cancelled a reservation that a concurrent Confirm had just confirmed. The specification contradicted itself (OP-04 failure outcome vs. the general transition `CONFIRMED → CANCELLED`). The team chose the strict interpretation of BR-03: a Cancel that observed `DRAFT` but finds `CONFIRMED` after the lock returns `Conflict`. | Fixed in `eaae325`: `backend/Services/ReservationService.cs` (compares `stateBeforeLock` with the state after `ReloadAsync`, returns `CancelReservationError.Conflict`), regression test `tests/ParkingReservation.Api.Tests/ReservationConfirmCancelTests.cs` (asserts `Conflict`). Full suite: 53 passed / 0 failed on `0f039b6`. Owner: Marek Šikula. |
| F2 | ACCEPTED | VERIFIED FINDING: on `45351cd`, `ParkingSpot` had no `State` attribute, although the README and Project Frame described `FREE / RESERVED / OCCUPIED / OUT_OF_SERVICE`. The team chose to implement the spot state (needed for check-in/out and `OUT_OF_SERVICE`) rather than only adjust the documentation. | Fixed in `eaae325`: `backend/Domain/ParkingSpotState.cs`, `ParkingSpot.State`, migration `20261025151543_AddUsersParkingAreasAndSpotState`, transitions in `ReservationService`, tests `ParkingAreaAndSpotStateTests.cs`. The model was later refined by the internal audit (spec v0.2.1, BR-06/BR-07, ADR-007). Owner: Marek Šikula. |
| F3 | REJECTED | REJECTED CANDIDATE: we agree with the assessors that C04 Quality & Measurement had no separate assignment in the supplied materials, so Q1–Q3 = N/A and it is not a project defect. | No-change. The selected future pressure Q (concurrent Confirm) is covered by the concurrent-confirmation test and by the `SpotLocks` (single-instance) risk in `docs/architecture-and-decisions.md`, ADR-006. |

Note on the PARTLY items: B1 (outdated README, section 9), A1 (missing C03 architecture document) and A3 (missing sequence diagram) were addressed in `eaae325` (`docs/architecture-and-decisions.md`, section *C03 — Architecture Evidence*); the README was further corrected in the commit of tag `c08-before`.

Disposition recorded by: Jan Ječmínek, 27 Nov 2026. Assessed baseline `45351cd`, fixes verified on `0f039b6`.
