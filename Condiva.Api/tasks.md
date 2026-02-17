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

- [x] Dashboard aggregate endpoint
  - Status: DONE
  - Scope: `GET /api/dashboard/{communityId}` con preview e counters

- [x] Server-side filtering for items/loans/my views
  - Status: DONE
  - Scope: filtri owner/status/category/search/sort + perspective loans

- [x] My communities endpoint with membership context
  - Status: DONE
  - Scope: endpoint unico community + membership role/status/permissions

- [x] Notifications enriched payload + proper unread-count
  - Status: DONE
  - Scope: unread-count typed + notifications con actor/entitySummary/target

- [x] Media resolution strategy for list cards
  - Status: DONE
  - Scope: signed `imageUrl` in list o endpoint batch resolve

- [x] Ensure avatarUrl population in UserSummary everywhere
  - Status: DONE
  - Scope: tutti gli endpoint con `UserSummaryDto`

## P2 Contract consistency e platform quality

- [x] Canonical auth response shape
  - Status: DONE
  - Scope: login, google, refresh response uniforme

- [x] Uniform pagination/sorting/date contracts
  - Status: DONE
  - Scope: tutte le list response con schema unico + date ISO UTC

- [x] Concurrency control for updates
  - Status: DONE
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
- 2026-02-17: completato `Dashboard aggregate endpoint` (nuovo `GET /api/dashboard/{communityId}` con `openRequestsPreview`, `availableItemsPreview`, `myRequestsPreview`, `counters` e test payload/authorization).
- 2026-02-17: completato `Server-side filtering for items/loans/my views` (items con filtri `owner/status/category/search/sort` + paging opzionale, loans con filtro `perspective=lent|borrowed`, test di filtro/paging aggiornati).
- 2026-02-17: completato `My communities endpoint with membership context` (nuovo `GET /api/memberships/me/communities-context` con dati community + ruolo/stato membership + action sets).
- 2026-02-17: completato `Notifications enriched payload + proper unread-count` (`GET /api/notifications/unread-count` typed con `unreadCount`, `GET /api/notifications` arricchito con `message`, `actor`, `entitySummary`, `target` e test coverage dedicata).
- 2026-02-17: completato `Media resolution strategy for list cards` (nuovo `POST /api/storage/resolve` batch per firmare piu `objectKeys` in una chiamata, dedup input, validazioni key e response typed con `items[]` + `expiresIn`).
- 2026-02-17: completato `Ensure avatarUrl population in UserSummary everywhere` (mapper esteso con accesso servizi DI per firmare avatar URL da `ProfileImageKey` su items/requests/offers/loans/memberships + actor notifications, con test payload aggiornati).
- 2026-02-17: completato `Canonical auth response shape` (login/google/refresh allineati su response unica `AuthSessionResponseDto` con `accessToken`, `expiresIn`, `tokenType`, `expiresAt`, `refreshTokenExpiresAt`, `user`; test auth aggiornati sul nuovo contratto).
- 2026-02-17: completato `Uniform pagination/sorting/date contracts` (list endpoint uniformati su `PagedResponseDto` con `items/page/pageSize/total/sort/order`, ordinamenti di default esplicitati, converter JSON UTC per `DateTime`/`DateTime?`, notifiche allineate allo stesso contratto e test payload aggiornati su response paginate).
- 2026-02-17: completato `Concurrency control for updates` (ETag deterministico su detail/create/update delle entita principali, supporto `If-Match` su `PUT/DELETE` per communities/items/requests/offers/loans/memberships/events con `412 precondition_failed` su mismatch, e test API aggiunti per header ETag + conflitto optimistic concurrency).
