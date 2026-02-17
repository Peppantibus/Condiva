# FE Handoff (Backend Ready)

Data aggiornamento: 2026-02-17

## Stato generale

- Tutti i task backend derivati da `BE_Tickets.md` e `NeedFromBE.md`: **DONE** (tracking in `Condiva.Api/tasks.md`).
- Test backend: **113/113 PASS** (`dotnet test Condiva.Tests/Condiva.Tests.csproj`).

## Contratti disponibili per FE

1. Auth canonica
- `POST /api/auth/login`
- `POST /api/auth/google`
- `POST /api/auth/refresh`
- Response unificata:
  - `accessToken`
  - `expiresIn`
  - `tokenType`
  - `expiresAt`
  - `refreshTokenExpiresAt`
  - `user`

2. Error envelope standard
- Tutti gli errori espongono:
  - `error.code`
  - `error.message`
  - `error.fields` (quando validation)
  - `traceId`

3. Members community paginato/filtrabile
- `GET /api/communities/{id}/members?page&pageSize&search&role&status`
- Include:
  - user summary (`id`, `displayName`, `userName`, `avatarUrl`)
  - `reputationSummary`
  - `allowedActions`

4. Offers di request con item summary embedded
- `GET /api/requests/{id}/offers`
- Ogni offer include `item` summary (no fetch N+1 lato FE).

5. Dashboard aggregata
- `GET /api/dashboard/{communityId}`
- Include preview e counters in singola chiamata.

6. Filtri server-side
- Items: `GET /api/items?communityId=...&owner=me&status=...&category=...&search=...&sort=...&page=...&pageSize=...`
- Loans: supporto `perspective=lent|borrowed` + paging/filtri.

7. Context community utente in endpoint unico
- `GET /api/memberships/me/communities-context`
- Community + ruolo + stato membership + allowed actions.

8. Notifiche arricchite
- `GET /api/notifications`
  - include `message`, `actor`, `entitySummary`, `target`
- `GET /api/notifications/unread-count`
  - response typed: `{ unreadCount: number }`

9. Strategia media
- `POST /api/storage/resolve` per risoluzione batch di object key -> signed URL.
- `avatarUrl` valorizzato in `UserSummaryDto` quando disponibile.

10. Coerenza contrattuale
- Paginazione uniforme: `items/page/pageSize/total/sort/order`
- Date in UTC ISO-8601
- Concurrency: `ETag` + `If-Match` su update/delete principali

## Note operative FE

- `POST /api/communities` ritorna `201 Created` (non `200`).
- Cookie auth/CSRF rispettano la configurazione `AuthCookies` runtime (`SameSite`, `Path`, `Secure`, ecc.).
- CSRF richiesto su metodi state-changing quando richiesto da middleware.

## Check rapido integrazione FE

1. Rigenerare client OpenAPI.
2. Allineare gestione status code create (`201`).
3. Usare `allowedActions` server-side per gating UI (evitare logica locale su `userId/role`).
4. Rimuovere fallback legacy sui payload auth multipli.
