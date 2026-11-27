#!/usr/bin/env bash
# Architecture conformance rule (ADR-006, docs/architecture-and-decisions.md):
# lifecycle state of Reservation and ParkingSpot is assigned only in ReservationService.
# Scans all application code in backend/ (migrations and build output excluded).
set -euo pipefail

cd "$(dirname "$0")/.."

# Matches `reservation.State = ...` / `spot.State = ...` (member assignment on an entity
# instance), not `==`/`!=` comparisons and not DTO object initializers in Contracts/.
pattern='[A-Za-z_][A-Za-z0-9_]*\.State[[:space:]]*=([^=]|$)'
expected='backend/Services/ReservationService.cs'

matches=$(grep -rlE "$pattern" backend --include='*.cs' \
  --exclude-dir=Migrations --exclude-dir=bin --exclude-dir=obj | sort || true)

violations=$(printf '%s\n' "$matches" | grep -vx "$expected" | sed '/^$/d' || true)

if [[ -n "$violations" ]]; then
  printf 'State assignment found outside ReservationService:\n%s\n' "$violations" >&2
  exit 1
fi

if ! printf '%s\n' "$matches" | grep -qx "$expected"; then
  printf 'Expected state transitions in %s were not found.\n' "$expected" >&2
  exit 1
fi

printf 'Architecture conformance passed: lifecycle state assignments are owned by ReservationService.\n'
