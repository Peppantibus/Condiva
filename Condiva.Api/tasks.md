# Backend Tasks Tracker (RBAC)

Source of truth: `Condiva.Api/BE_Tasks.md`

## Working rules

- One task = one commit.
- Status values allowed: `TODO`, `DONE`.
- A task can be marked `DONE` only when all acceptance criteria are satisfied and tests pass.
- Do not mix multiple task IDs in one commit.

## Commit convention

- Suggested format: `feat(rbac): BE-RBAC-0X <short-description>`
- Example: `feat(rbac): BE-RBAC-02 add Admin membership role`

## Task list

### BE-RBAC-01 - Official role/permission matrix
- Status: TODO
- Commit: excluded from commit plan (user request)
- Scope:
  - Add `Condiva.Api/RBAC_MATRIX.md`.
  - Define canonical permission set in code (single source).
  - Ensure no auth decision is delegated to FE.
- Acceptance:
  - [ ] `RBAC_MATRIX.md` present and aligned with API behavior.
  - [ ] Canonical permission identifiers centralized in backend code.
  - [ ] Backend-only authorization decisions.

### BE-RBAC-02 - Add Admin role to membership domain
- Status: TODO
- Commit: `feat(rbac): BE-RBAC-02 add Admin role to membership`
- Scope:
  - Extend membership role enum/model/validation/migrations.
  - Update role update endpoint and OpenAPI enum.
  - Preserve compatibility with existing roles (`Owner`, `Moderator`, `Member`).
- Acceptance:
  - [x] API accepts/persists `Admin`.
  - [ ] OpenAPI shows updated role enum.
  - [x] No undocumented breaking changes.

### BE-RBAC-03 - Member detail endpoint with effective permissions
- Status: TODO
- Commit: `feat(rbac): BE-RBAC-03 add member detail endpoint with permissions`
- Scope:
  - Add `GET /api/communities/{communityId}/members/{memberId}`.
  - Response includes: user summary, role, status, joinedAt, reputationSummary,
    effectivePermissions (target), allowedActions (caller on target).
- Acceptance:
  - [x] `effectivePermissions` and `allowedActions` are explicit and distinct.
  - [x] `403`/`404` behavior is coherent and tested.
  - [ ] Swagger response example added.

### BE-RBAC-04 - Align members list payload
- Status: DONE
- Commit: `feat(rbac): BE-RBAC-04 align members list payload`
- Scope:
  - Ensure `GET /api/communities/{id}/members` includes:
    role, status, reputationSummary, effectivePermissions, allowedActions.
  - Keep pagination/filter performance.
- Acceptance:
  - [x] FE can render role/permission state without extra optional calls.
  - [x] Pagination/filtering behavior remains stable and performant.

### BE-RBAC-05 - Server-side enforcement on critical endpoints
- Status: DONE
- Commit: `feat(rbac): BE-RBAC-05 enforce RBAC on critical operations`
- Scope:
  - Requests moderation/delete: `Moderator+` for community moderation,
    `Member` only on own resources.
  - Membership role updates: restricted by matrix (Admin/Owner policy).
  - Items/Loans/Offers rules aligned to RBAC matrix.
- Acceptance:
  - [x] `Admin` can perform all matrix-allowed actions.
  - [x] `Moderator` can moderate/delete community requests as defined.
  - [x] `Member` cannot operate on others' resources outside permissions.
  - [x] `401`/`403` are consistent.

### BE-RBAC-06 - Role-change security guard rails
- Status: TODO
- Commit: `feat(rbac): BE-RBAC-06 harden role-change guard rails`
- Scope:
  - Block unauthorized self-escalation.
  - Prevent removing/downgrading last admin/owner in a community.
  - Make role updates transaction-safe.
- Acceptance:
  - [ ] Illegitimate attempts return `403` with clear error code.
  - [x] Community always has required admin/owner integrity.

### BE-RBAC-07 - Audit logs for moderation and role changes
- Status: TODO
- Commit: `feat(rbac): BE-RBAC-07 add audit logs for critical authz actions`
- Scope:
  - Persist audit records for role change, moderation delete, member suspension/removal.
  - Required fields: actorUserId, targetUserId, communityId, action, oldValue, newValue, timestamp.
- Acceptance:
  - [ ] Audit is persisted for each critical action.
  - [ ] Integration tests cover main audit flows.

### BE-RBAC-08 - Authorization test matrix + OpenAPI update
- Status: TODO
- Commit: `feat(rbac): BE-RBAC-08 add authz matrix tests and refresh OpenAPI`
- Scope:
  - Integration tests for `Admin`/`Moderator`/`Member` across key endpoints.
  - Regenerate/validate OpenAPI with new fields/endpoints.
  - Handoff note for FE with new payload mappings.
- Acceptance:
  - [ ] Authz suite green for role matrix.
  - [ ] OpenAPI/client generation works without FE manual patching.

## Progress log

- 2026-02-17: tracker initialized from `BE_Tasks.md`; all RBAC tasks set to TODO pending implementation.
- 2026-02-17: implemented Admin role, centralized role policy, effective permissions on members list/detail, and hardened role-change safeguards. Added integration tests for Admin and member-detail permissions.
