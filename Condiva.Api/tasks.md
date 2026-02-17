# Backend Tasks Tracker

Fonte: `Condiva.Api/BE_Tickets.md` + `Condiva.Api/NeedFromBE.md`

Regola stato:
- `TODO`: non completato o parzialmente completato
- `DONE`: completato e committato

## P0 Security, Auth, Authorization

- [x] Server-authoritative identity on write operations
  - Status: DONE
  - Scope: create/update su item, request, offer, loan, membership
  - Note: derivare sempre identity da token/sessione, no trust su `*UserId`

- [x] Authorization matrix + allowedActions
  - Status: DONE
  - Scope: communities, members, requests, offers, items, loans
  - Note: enforcement server-side + `allowedActions` coerenti in list/detail

- [x] Standard error envelope
  - Status: DONE
  - Scope: tutti gli endpoint
  - Note: shape unica `error.code`, `error.message`, `error.fields`, `traceId`

- [x] CSRF/session contract hardening
  - Status: DONE
  - Scope: cookie auth/session, rotazione CSRF, CORS allowlist

- [x] Idempotency-Key on critical POST
  - Status: DONE
  - Scope: create item/request/offer/loan, join community, rotate invite

## P1 Performance e query model

- [x] Members endpoint per community, paginato e filtrabile
  - Status: DONE
  - Scope: `GET /api/communities/{id}/members` con `page,pageSize,search,role,status`

- [x] Request offers include item summary
  - Status: DONE
  - Scope: `GET /api/requests/{id}/offers` include item summary completo

- [ ] Dashboard aggregate endpoint
  - Status: TODO
  - Scope: `GET /api/dashboard/{communityId}` con preview e counters

- [ ] Server-side filtering for items/loans/my views
  - Status: TODO
  - Scope: filtri owner/status/category/search/sort + perspective loans

- [ ] My communities endpoint with membership context
  - Status: TODO
  - Scope: endpoint unico community + membership role/status/permissions

- [ ] Notifications enriched payload + proper unread-count
  - Status: TODO
  - Scope: unread-count typed + notifications con actor/entitySummary/target

- [ ] Media resolution strategy for list cards
  - Status: TODO
  - Scope: signed `imageUrl` in list o endpoint batch resolve

- [ ] Ensure avatarUrl population in UserSummary everywhere
  - Status: TODO
  - Scope: tutti gli endpoint con `UserSummaryDto`

## P2 Contract consistency e platform quality

- [ ] Canonical auth response shape
  - Status: TODO
  - Scope: login, google, refresh response uniforme

- [ ] Uniform pagination/sorting/date contracts
  - Status: TODO
  - Scope: tutte le list response con schema unico + date ISO UTC

- [ ] Concurrency control for updates
  - Status: TODO
  - Scope: ETag/If-Match o rowVersion su update/delete principali

- [ ] OpenAPI fidelity and SDK validation pipeline
  - Status: TODO
  - Scope: spec/runtime alignment + check pipeline client generation

## Progress Log

- Inizio tracciamento: 2026-02-17
- 2026-02-17: completato `Server-authoritative identity on write operations` (hardening write path + test anti-impersonificazione).
- 2026-02-17: completato `Authorization matrix + allowedActions` (allowedActions su list/detail + 403 su permessi mutativi mancanti).
- 2026-02-17: completato `Standard error envelope` (envelope uniforme + traceId + fields validazione + handler globale 500).
- 2026-02-17: completato `CSRF/session contract hardening` (error envelope uniforme nel middleware CSRF, logout protetto da CSRF, endpoint rotazione token CSRF, CORS origin allowlist centralizzata).
- 2026-02-17: completato `Idempotency-Key on critical POST` (middleware su POST critici con replay response persistita, conflitto su payload differente, CORS aggiornato per header Idempotency-Key).
- 2026-02-17: completato `Members endpoint per community, paginato e filtrabile` (nuovo `GET /api/communities/{id}/members` con filtri `search/role/status`, payload user+reputation, paginazione server-side e test coverage dedicata).
- 2026-02-17: completato `Request offers include item summary` (DTO offer esteso con `item` embedded, include query `Item+Owner` su request offers e test payload aggiornati).
