# Usage limits monitor

A portable Windows 11 tray app for viewing the owner's Codex quota across multiple ChatGPT accounts. See [README.md](README.md) for use and [DESIGN-REVIEW.md](DESIGN-REVIEW.md) for verification and remaining live-account checks.

## Language

**Account**:
A ChatGPT identity the owner authorizes for monitoring. An account label or email alone does not authorize access.
_Avoid_: API key, profile, subscription.

**Monitor profile**:
The app's local entry for an authorized account. Each profile has an app-owned Codex home and strict OS credential storage. Dummy-credential isolation has been checked across restarts; real OAuth accounts still need verification. Codex sign-in selects the workspace; this version has no separate workspace picker.
_Avoid_: Codex configuration profile, account.

**Account label**:
A name chosen for display, such as Personal or Work, used in notification popups instead of an email address. It is not an authentication identifier.
_Avoid_: Account ID.

**Quota bucket**:
A separately metered allowance reported by Codex. Buckets must retain their identity rather than being combined into one total.
_Avoid_: Credit balance, account balance.

**Quota window**:
A bucket's reported usage period, with a used percentage and optional duration and reset time. A window is not necessarily a fixed five-hour or weekly period.
_Avoid_: Billing cycle, token allowance.

**Remaining percentage**:
The unused proportion of one reported quota window. It is not a count of messages or a comparable amount of work across different plans.
_Avoid_: Messages left, tokens left.

**Credit balance**:
The remaining spendable Codex credits for the account or workspace, when reported by the service. This is separate from quota percentages, API billing, and the count of available usage resets; a missing balance does not mean zero.
_Avoid_: Remaining quota, reset credits.

**Reset time**:
The time reported for a quota window to reset. Passing that time is not evidence that a refresh has confirmed restored capacity.
_Avoid_: Guaranteed refill time.

**Usage snapshot**:
The last successful usage response for a monitor profile, together with when the app received it. A failed refresh does not make that snapshot current.
_Avoid_: Live usage.

**OpenAI token history**:
Token totals and optional daily token counts returned by account/usage/read, shown as daily bars or an exact-value table. Its dates and coverage are supplied by OpenAI; token counts are not remaining quota percentages. It is not recorded to disk. Missing dates and unknown counts are not zero.
_Avoid_: Local estimates, historical quota balance.

**Low quota alert**:
A notification at 20% or 5% remaining for an account's quota window, delivered at most once per threshold until that window resets. A newly added account already at or below 5% receives only the 5% warning for that window.
_Avoid_: Limit reset, usage reset credit.

## Boundaries

- The owner authorizes their accounts; the app displays usage; OpenAI supplies the authoritative limits.
- The confirmed target is Codex quota and available credit balances, not ordinary ChatGPT message limits or API spending. Initial accounts use Plus and Free.
- Local quota-history recording has been removed. Normal startup cleans up recognized old history logs while retaining settings, alert state, profiles, and the latest quota snapshots.
- Separate profiles passed the installed runtime's dummy-credential isolation check. This does not establish live Plus/Free OAuth or every future Codex version's compatibility.
- See [Design review](DESIGN-REVIEW.md) for risks, decisions, and verification work.

## Sources

Terminology is grounded in the conversation and the [OpenAI account and rate-limit interface](https://learn.chatgpt.com/docs/app-server#auth-endpoints), reviewed on 2026-08-27. UI behavior and profile storage are this app's choices, not promises made by that interface.
