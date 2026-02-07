# Auth Alignment Tasks

## Status Legend
- `todo`
- `in_progress`
- `done`
- `blocked`

## Tasks

1. `id`: `AUTH-001`
   `title`: Align rate limiting host wiring (DI + middleware + config rules)
   `status`: `done`
   `commit_message`: `fix AUTH-001 enable auth rate limiter in host pipeline`

2. `id`: `AUTH-002`
   `title`: Harden auth token input validation
   `status`: `done`
   `commit_message`: `fix AUTH-002 enforce token format and length validation`

3. `id`: `AUTH-003`
   `title`: Add unique DB constraints for auth users
   `status`: `todo`
   `commit_message`: `fix AUTH-003 add unique indexes for username and email`

4. `id`: `AUTH-004`
   `title`: Harden auth configuration defaults (redis/proxy/rules/placeholders)
   `status`: `todo`
   `commit_message`: `fix AUTH-004 harden auth configuration defaults`

5. `id`: `AUTH-005`
   `title`: Upgrade AuthLibrary.Core to 1.0.5
   `status`: `done`
   `commit_message`: `fix AUTH-005 upgrade AuthLibrary.Core to 1.0.5`
   `notes`: `Updated AuthRepository to new IAuthRepository/ITransactionalAuthRepository members; added ExternalAuthLogins persistence + migration; added design-time DbContext factory; fixed DI ambiguity for IAuthService<User>; adjusted RateLimit config for 1.0.5 validation.`
