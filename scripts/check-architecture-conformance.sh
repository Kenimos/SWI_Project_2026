#!/usr/bin/env bash
set -euo pipefail

matches=$(find backend/Services -type f -name '*.cs' -exec grep -lE 'State[[:space:]]*=' {} + || true)
expected='backend/Services/ReservationService.cs'

if [[ "$matches" != "$expected" ]]; then
  printf 'Reservation.State mutation found outside ReservationService:\n%s\n' "$matches" >&2
  exit 1
fi

printf 'Architecture conformance passed: lifecycle state mutation is owned by ReservationService.\n'