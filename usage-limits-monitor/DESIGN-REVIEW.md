# Usage limits monitor review

Status: the 30 September 2026 source update groups all windows in one account row and tooltip line, adds provider-reported available reset counts, and adds optional manual subscription dates. Build, automated checks, and runtime inspection have not been run for this update, and existing packages have not been rebuilt. The earlier tooltip-theme version was checked on 28 August as recorded below. Live Plus/Free OAuth and usage responses remain unverified. No real account has been enrolled by the implementation agent.

## Scope and decisions

The folder was empty during the initial design review. Farid subsequently approved implementation, the build, and focused automated checks. The current app monitors **Codex** quota, not ordinary ChatGPT message limits or OpenAI API spending.

| Decision | Accepted behavior |
| --- | --- |
| Accounts | Roughly three or four owner-controlled accounts, initially Plus and Free. No arbitrary account-count or plan-name cap; broader compatibility is unverified. |
| Prerequisite | A compatible native Codex executable, installed separately. The monitor does not install or bundle it. |
| Refresh | Every 60 seconds plus Refresh now. No overlapping operations. Failed accounts back off independently from 1 to 15 minutes. |
| Quotas and credits | Group windows in one account row with aligned ` | ` values. Separate buckets retain names; percentages stay independent. Show spendable credits once per bucket, including after quota exhaustion. Missing data remains unavailable. |
| Available resets | Show the provider's rateLimitResetCredits.availableCount once per account in the table and tooltip. Unknown is Unavailable; explicit zero is 0. Never count optional detail rows or redeem a reset. |
| Subscription | Distinguish Renews from Ends. Current provider date availability is unestablished, so use the approved optional manual fallback, labelled Manual. Display local date/time; store UTC. Free is Not applicable. Past dates stay manual and do not roll forward or change the account plan. |
| Low quota alerts | Once at 20% and 5% per account/window, persisted across restarts. Initial readings at or below 5% send only the urgent warning. |
| Notification privacy | Use the owner's chosen account label, not an automatically collected email address. |
| Delivery | Portable Windows 11 executable and ZIP. |
| Startup | On by default, registered for the current user on first actual launch. Preserve opt-out. Extraction and automated checks do not register startup. |
| Window | Start in the tray at Windows sign-in. Hover shows only a non-focusing tooltip with all account summaries; clicking the tray opens the full dashboard. Close hides; Quit stops. |
| Hover format | Preserve every account line with all window labels, corresponding percentages, short reset countdowns, and local reset dates/times in matching order. Join window values with ` | ` and append available reset count and subscription text. Label stale/unknown readings. The nonactivating popup keeps the system font, thin border, and shadow, with colors following the selected app theme. No native tray text limit. |
| Theme | Dark by default, including existing settings without a preference. Light mode is available in Settings; Windows high contrast takes precedence. |
| Additional features | Farid explicitly included reset notifications, history charts, and a numeric tray icon. |
| History | OpenAI daily tokens only. Default 30-day bar chart for the selected account, 7/30/all range selector, and exact-value table. No local history recording, disk cache, or local fallback. Missing days/counts never become zero. |
| Old history | Farid approved deleting old recorder logs. On normal startup, validate all entries before deleting recognized dated logs; reject redirects and unexpected contents. Preserve profiles and current account state. |
| Tray design | Rounded number badge, neutral normal state, amber at 20%, red at 5%, gray question mark for stale/unknown. Draw at the system's small-icon size. |
| Verification permission | The 28 August build and checks were approved and performed. The 30 September instructions prohibit builds, test runs, new test files, Git, and lint without a separate request; this source update does not run them. |

The implementation uses Windows Forms and the installed .NET Framework compiler without new runtime/package dependencies. OpenAI daily token history is fetched on demand and kept only while the history window is open. The 7/30-day filters use UTC dates through today; All available shows the returned date span. The tray icon represents one pinned quota; its account picker preserves window-specific selection after grouping. The tooltip includes all windows per account. Percentages are never summed or averaged across windows, accounts, or plans.

## Account and credit interface

The app uses newline-delimited JSON over Codex App Server standard input/output: initialize, initialized, account/login/start, account/login/cancel, account/read, account/rateLimits/read, account/usage/read, and account/logout. It never creates agent turns or invokes model tasks. [Official interface](https://learn.chatgpt.com/docs/app-server#auth-endpoints)

The documented account/usage/read method returns token summary fields and optional dailyUsageBuckets. The chart represents daily tokens, not remaining quota percentages. The installed runtime's support and real Plus/Free responses have not been checked for this update. Unsupported methods, missing buckets, invalid dates, and unknown counts produce explicit unavailable/error states. Identity is checked before requesting history. [Token usage interface](https://learn.chatgpt.com/docs/app-server#7-token-usage-chatgpt)

Every profile has its own app-owned CODEX_HOME, passed only to that child process. The profile config forces cli_auth_credentials_store to keyring. There is no auto or plaintext fallback. OAuth and refresh belong to Codex; the monitor never collects passwords. This is not a usage-only authorization scope. [Credential storage](https://learn.chatgpt.com/docs/auth#credential-storage)

The parser prefers rateLimitsByLimitId when present, otherwise rateLimits. Windows retain primary/secondary identity and reported duration. The credits.balance, credits.unlimited, and credits.hasCredits fields are independent of quota percentages. A missing balance is not zero. Usage-reset credits are not spendable credits. A positive balance does not prove that a current request would charge against it. [Rate-limit fields](https://learn.chatgpt.com/docs/app-server#6-rate-limits-chatgpt)

The available reset count comes from rateLimitResetCredits.availableCount. Missing, negative, fractional, or malformed values remain unavailable. Manual subscription dates are saved independently from snapshots, labelled with their chosen Renews/Ends meaning, and never derived from OAuth-token expiry. No subscription-date field was found in the documented account interface reviewed for this update. [Account interface](https://learn.chatgpt.com/docs/app-server#auth-endpoints)

Plus users can purchase additional Codex credits after included limits. This review does not establish Free credit eligibility or prove that every plan returns a balance. The app does not purchase credits or redeem resets. [Usage after limits](https://learn.chatgpt.com/docs/pricing#what-happens-when-you-hit-usage-limits)

## Risk review

| Priority | Risk and implemented safeguard | Evidence / remaining check |
| --- | --- | --- |
| High | Credentials could cross profiles. Separate app-owned homes and strict keyring storage; removal logs out only that profile. | Dummy-key profiles passed isolation, restart, and selective logout checks. Repeat with real Plus and Free OAuth sign-ins. |
| High | A viewer holds broad account credentials. No password input, cookie scraping, plaintext fallback, or diagnostic logging in the monitor. Child processes discard inherited API-key overrides. | Neither test profile created auth.json. Live OAuth storage and refresh still need observation. Protect access to the Windows account. |
| High | Reconnecting with the wrong account could mix history. Bind a hash of the reported identity and reject mismatches or duplicates. | Hash normalization is checked; real reconnect needs a live check. If only email is reported, an email change requires re-enrollment. Multiple workspaces sharing that identity are not separate entries. |
| High | A failed refresh or elapsed reset could imply restored capacity. Preserve timestamps, mark failed/unverified/older-than-150-second snapshots stale, and use ? in the tray. | Parser and native smoke checks pass. An expired countdown says Awaiting confirmation. |
| Medium | Alerts could repeat or refer to another window. Persist a ledger per account/quota before scheduling notifications. | Threshold and restart checks pass. Without reset times, rearming is suppressed. A crash after ledger save can lose an alert; Windows may suppress delivery. |
| Medium | A reset notification could claim a refill from the clock alone. Require a fresh later window, an elapsed previous reset, and capacity returning after a zero reading. | Synthetic-response test passes. No alert for a window boundary if exhaustion was not observed. |
| Medium | History could invent activity. Plot returned daily counts only; leave absent dates blank, unknown counts marked, and explicit zero at the baseline. Clear prior data on refresh failure. | Range boundaries, missing/zero values, chart/table consistency, and unavailable/empty/error states pass synthetic checks. Live availability still needs checking. |
| Medium | Removal could delete unrelated files. Profile removal validates GUID paths and redirects. Legacy-history cleanup validates its fixed folder, ancestor redirects, and every dated filename before deletion; it never recurses. | Cleanup tests reject unexpected files/directories and preserve settings, snapshots, alert state, and a sibling profile. The old running version must quit before upgrading. |
| Medium | Portability and startup need verification. Use Windows Framework and per-user startup, with explicit Quit. | The app builds and runs here. Actual startup registration, Windows sign-in, and a second PC remain manual checks. The executable is unsigned. |

Labels, identity hashes, latest snapshots/reset counts, manual subscription dates, and alert state remain local without encryption. They are not credentials but may be private. Token history is not written to disk. Removal clears that profile's local credentials and saved account state, not every remote session or OpenAI's service history. Codex may create runtime files in its profile. Full uninstall instructions are in [README.md](README.md).

## Previous-version verification record

Host: Windows 11 Home, version 10.0.26200, 64-bit. Compiler: installed Windows .NET Framework C# compiler. Integration runtime: codex-cli 0.150.0-alpha.8.

- Build compiles the app and focused test runner with warnings treated as errors. No package installation.
- Model/storage checks cover absent and explicit credits, credit-only responses, multiple windows, elapsed resets, warning thresholds, restart deduplication, new-window alerts, corrupt settings preservation, startup preference persistence, history recovery, and selective removal.
- The credential probe uses two new test homes. Preflight confirms no inherited login; invalid dummy keys are stored, restarted, and selectively logged out. No model requests. Credential cleanup passed. Windows Credential Manager access was approved outside the sandbox.
- The native smoke check uses four synthetic accounts and eight rows. It exercises popup open paths, close-to-tray, an exhausted pinned quota, and stale tray display. It renders dashboard, history, and settings screenshots for inspection. It does not synthesize actual mouse movement over the Windows tray icon.
- Tests skip startup registration and real account enrollment. No Git, lint, browser tests, real-account credential reads, or dependency downloads were performed.

Commands and reports are in build.ps1, artifacts/checks.txt, and artifacts/ui-checks.txt. Screenshots contain test data, not the owner's usage. The portable package contains the executable and documentation, not test profiles or credentials.

## Previous verification: 28 August 2026

- The app and test runner compile with warnings treated as errors. All 41 focused checks pass, including full tooltip formatting, service token parsing, 7/30/all range boundaries, missing/zero counts, safe legacy-history cleanup, and preservation of account state alongside the existing quota/credit/alert checks.
- The native UI smoke check passes with four synthetic accounts and eight quota rows. Hover keeps all full lines, leaves the dashboard hidden, and preserves the Windows foreground window. Click opens the dashboard and hides the tooltip; close hides; exhausted and stale tray values remain correct. The daily-token chart defaults to 30 days, matches the exact-value table, filters 7/30/all ranges, and clears values for unavailable, empty, and failed responses. No local history directory is created.
- Screenshots cover the Windows-style tooltip, dark/light chart, minimum history-window size, exact-value table, unavailable state, and number badges at 16/20/24/32 pixels on dark/light backgrounds. The smoke check invokes tray interaction handlers; it does not move the physical mouse over the Windows tray.

The current reports are artifacts/checks.txt and artifacts/ui-checks.txt. Startup registration, network access, and real sign-ins were skipped. The prior dummy-credential integration probe was not rerun, and the installed runtime's account/usage/read support remains unverified. The tooltip text/background color checks and rendered-background pixel checks pass in both dark and light mode. Both tooltip screenshots were visually inspected for readable text and clipping.

During the 27 August update, the old dated history log and its empty folder were removed after the old app stopped, following path, redirect, and filename checks. Other installations receive guarded history cleanup on their next normal launch. The 28 August tooltip-color build uses build.ps1 and the tested artifacts/UsageLimitsMonitor.exe; the portable ZIP contains that same executable. No live profile or account settings were read or modified during verification.

## Source update: 30 September 2026

The dashboard now has one row per account. Windows, remaining percentages, countdowns, and reset dates use the same stable bucket/window order; credits are shown once per bucket. Pin quota offers a window picker and keeps the existing saved account/quota keys. Available reset counts come from the current usage response. Subscription opens a manual date editor with Renews/Ends, cancellation, and clearing; saving rolls back the account fields if settings cannot be written. Existing settings omit the new optional fields and remain readable.

Source paths and call sites were reviewed. Existing focused-check expectations and the native smoke check's row-count expectation were adjusted for grouping, but no tests or application runs were performed. The previous verification record does not validate these changes. No new test files were created; no artifacts, real account data, credentials, or global settings were inspected or changed for this update.

Remaining verification includes compilation, grouped tooltip alignment and stale/unknown fields, authoritative reset counts and missing-count behavior on a compatible Codex runtime, manual subscription save/cancel/clear/restart behavior, Free accounts, and pinning a window from a grouped row. These are pending checks.

## Remaining live acceptance checks

1. Enroll the owner's Plus and Free accounts separately. Compare buckets, reset times, and reported credits against their authorized service responses. Confirm the existing Codex session stays unchanged.
2. Restart; test token refresh and reconnect, including choosing the wrong account. Remove one profile and verify the other still refreshes.
3. Exercise offline, signed-out, throttled, absent-credit, and exhausted-quota states. No missing field may become a fabricated number.
4. Physically hover and click the tray icon. Hover must show all formatted account summaries without opening/focusing the dashboard. Verify dark/light mode, keyboard navigation, high-DPI/multiple-monitor setups, and Windows notification delivery.
5. On actual first launch, check startup registration. Disable it, restart to confirm opt-out, then re-enable and verify tray startup at Windows sign-in. Check Quit stops child processes.
6. Run the extracted ZIP on another Windows 11 machine with a compatible Codex CLI. Recheck after changing Codex versions.
7. Compare the OpenAI daily token rows to the account's authoritative history where available. Confirm unsupported runtime, missing history, explicit zero, and network failure states; never label daily tokens as remaining quota.

These are unverified checks, not claims of live-account compatibility.
