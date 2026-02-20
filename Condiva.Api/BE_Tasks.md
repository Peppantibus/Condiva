# BE Tasks - RBAC and Permissions

## BE-RBAC-01 - Definizione matrice ruoli/permessi ufficiale
**Obiettivo:** formalizzare RBAC unico lato server (source of truth).

**Ruoli richiesti:**
- `Admin`: full access su tutta l'app/community.
- `Moderator`: moderazione contenuti + gestione richieste/prestiti a livello community.
- `Member`: operativita standard (crea/gestisce solo proprie risorse, richieste, offerte, prestiti consentiti).

**Acceptance Criteria:**
- Documento `RBAC_MATRIX.md` in repo BE con tabella ruoli -> permessi.
- Set permessi canonici (es. `requests.delete.any`, `members.role.update`) centralizzato in codice.
- Nessuna decisione di autorizzazione demandata al FE.

---

## BE-RBAC-02 - Estensione dominio ruolo `Admin`
**Obiettivo:** introdurre `Admin` nel modello membership.

**Scope tecnico:**
- Aggiornare enum/validator/DB migration per includere `Admin`.
- Aggiornare endpoint update ruolo (attuale `role(id, body)`).
- Compatibilita con ruoli esistenti (`Owner` se ancora presente).

**Acceptance Criteria:**
- API accetta/salva `Admin` correttamente.
- OpenAPI aggiornata con nuovo enum.
- Nessun breaking non documentato.

---

## BE-RBAC-03 - Endpoint dettaglio membro + permessi effettivi
**Obiettivo:** dare al FE dati chiari per scheda membro senza inferenze.

**Nuovo contratto (consigliato):**
- `GET /api/communities/{communityId}/members/{memberId}`

**Response minima:**
- `user` summary
- `role`, `status`, `joinedAt`
- `reputationSummary`
- `effectivePermissions` (permessi del membro target)
- `allowedActions` (azioni del caller su quel membro)

**Acceptance Criteria:**
- Distinzione esplicita tra `effectivePermissions` e `allowedActions`.
- `403`/`404` coerenti.
- Swagger con esempi response.

---

## BE-RBAC-04 - Allineamento endpoint lista membri
**Obiettivo:** evitare ambiguita nel payload lista membri.

**Scope:**
- `GET /api/communities/{id}/members` deve includere gli stessi campi chiave del dettaglio (almeno `role`, `status`, `reputationSummary`, `effectivePermissions`, `allowedActions`).

**Acceptance Criteria:**
- FE puo renderizzare ruoli/permessi per ogni membro senza endpoint extra opzionali.
- Paginazione/filtri invariati e performanti.

---

## BE-RBAC-05 - Enforcement policy server-side su endpoint critici
**Obiettivo:** applicare RBAC reale, non solo esporre metadata.

**Scope minimo:**
- Richieste: delete/moderazione (`Moderator+` su contenuti community; `Member` solo own).
- Membership/ruoli: update ruolo solo `Admin` (o `Owner` se previsto).
- Items/Loans/Offers: regole coerenti con matrice.

**Acceptance Criteria:**
- `Admin` puo fare tutte le azioni previste dalla matrice.
- `Moderator` puo moderare e cancellare richieste community.
- `Member` non puo operare su risorse altrui fuori permessi.
- `401`/`403` corretti e consistenti.

---

## BE-RBAC-06 - Guard rail sicurezza sui cambi ruolo
**Obiettivo:** prevenire escalation e lockout amministrativo.

**Regole richieste:**
- No self-escalation non autorizzata.
- No downgrade/rimozione dell'ultimo admin/owner della community.
- Validazione transazionale su update ruolo.

**Acceptance Criteria:**
- Tentativi illegittimi -> `403` con error code chiaro.
- Integrita ruolo admin community sempre garantita.

---

## BE-RBAC-07 - Audit log azioni di moderazione e roleing
**Obiettivo:** tracciabilita security/compliance.

**Scope:**
- Loggare: role change, delete request moderazione, sospensioni/rimozioni membri.
- Campi minimi: `actorUserId`, `targetUserId`, `communityId`, `action`, `oldValue`, `newValue`, `timestamp`.

**Acceptance Criteria:**
- Audit persistito lato BE per ogni azione critica.
- Test di integrazione per eventi principali.

---

## BE-RBAC-08 - Test suite autorizzazioni + aggiornamento OpenAPI
**Obiettivo:** chiudere il ciclo con qualita.

**Scope:**
- Test integration matrix-based (`Admin`/`Moderator`/`Member`) sui principali endpoint.
- Rigenerazione OpenAPI con nuovi campi/endpoint.
- Changelog handoff FE con mapping campi nuovi.

**Acceptance Criteria:**
- Suite authz verde.
- OpenAPI/client rigenerabili senza patch manuali FE.

