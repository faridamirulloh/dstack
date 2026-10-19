# Mini Codex implementation plan

Status: Phase 0 assessment, native UI, folder management, and initial Codex integration are implemented. The connected Release build passes; live acceptance checks remain outstanding.

Decision date: 15 October 2026.

Phase 0 assessment date: 16 October 2026.

This plan replaces the Tauri/React/Rust approach with C# and WPF on .NET 10 for Windows 11. [ARCHITECTURE.md](ARCHITECTURE.md) defines component ownership, profile isolation, lifecycle rules, and open decisions.

The original Phase 0 request authorized inspection and documentation only. The user then authorized the WPF UI and folder management, followed by Codex integration and a rebuild. The latter includes installing the complete official CLI as the runtime dependency. Git, tests, lint, interactive sign-in checks, schema generation, and release packaging were not requested. Verification activities below remain future acceptance work.

## Current UI delivery

Implemented in `src/MiniCodex` with no third-party packages:

- Dark Compact Windows layout, native account manager, one window per local profile, and a single settings writer.
- Projects with a staged Manage folders dialog, native multi-select picker, canonical path duplicate checks, add/remove folders, and a required primary folder.
- Project-wide folder scope for every local chat, resolved through `WorkspaceForChat`; the primary directory is separate from the full folder list.
- Atomic app-owned settings commits, preserved previous-state backup, and startup rejection of invalid or future settings schemas.
- Local draft chat creation, renaming, duplication, pinning, archiving/restoring, and deletion. Draft message text is in-memory only.
- Per-profile Codex launcher and JSONL transport with version checks, isolated home/state paths, keyring policy, initialization, and bounded requests/events.
- Browser/device-code login, cancellation/logout, account summaries, discovered models/efforts, usage/credits refresh, and context remaining.
- App Server history pagination, thread start/resume/fork and CRUD, streamed text/command items, scoped approvals/questions, interruption, and explicit recovery from uncertain outcomes.
- A sandbox policy containing every project folder, overlap warnings between tasks, and Stop-or-Cancel window closing.

The Release build succeeded on 16 October 2026 with SDK 10.0.402: 0 warnings and 0 errors. The first attempt was blocked by .NET first-run writes outside the workspace; the build then used a project-local `DOTNET_CLI_HOME`. A compile pass found missing `System.IO` imports, which were corrected before the successful build. No application launch, interactive checks, tests, or resource measurements were run.

The connected build output is `src/MiniCodex/bin/Release/Connected/MiniCodex.exe`, separate from the earlier UI executable. A later update implements existing-home selection, legacy turn-history fallback, per-account sign-out in the manager, independent window closing, hidden sign-in controls after login, and bottom-left connection status. Those fixes were rebuilt with 0 warnings and 0 errors. Runtime acceptance still needs live verification for account isolation, tool execution, Windows sandbox setup, auto-review, interruption, and recovery. Markdown formatting, server subscription eviction, and crash-safe Job Object supervision remain unimplemented.

The latest requested source update adds a combined list across all projects and accounts, account-bound session opening/actions, sequential background refresh with temporary runtime leases, hidden-until-used inline search, a smaller New chat button below project selection, and dark scrollbar/menu templates. Session lists page independently per account and archived state; the project selector does not filter them. These latest changes have not been rebuilt, launched, or tested.

## Outcome and scope

The MVP lets the user add dynamically named profiles, authenticate different ChatGPT accounts, open them in independent native windows, resume compatible local conversations, and execute tasks concurrently without mixing account state or events.

The accepted priorities are reliability, profile isolation, session compatibility, and low total RAM across multiple windows. Native basic Markdown is sufficient. Imported homes may receive normal App Server writes, but Mini Codex does not directly rewrite Codex storage. Only one client may use a conversation at a time in the supported workflow.

Closing a busy account window asks to stop its tasks. Cancelling leaves its window and tasks open. There is no implicit continue-in-background close option in the MVP. Losing focus, minimizing, or switching conversations does not stop work.

## Planning baseline

The initial 15 October inspection found an empty `codex-mini` directory and .NET SDK 6.0.424. That tool inventory is superseded by the 16 October results below: SDK 10.0.402 and Desktop Runtime 10.0.13 are now installed. Node and npm are not application-stack requirements for the WPF design. Tool inventory is not a successful build or protocol check.

The two resolved Codex executables belong to plugin and desktop installations; both report 0.162.0-alpha.17.2. Neither is the selected public stable 0.162.1 baseline. Do not make either private installation path a distributable application dependency.

.NET 10 is an LTS release. Follow the current supported patch policy when implementation begins; do not build against the inspected .NET 6 SDK. See the [official .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

Official documentation, public release metadata, and source at the selected CLI tag were reviewed. No App Server was launched, no profiles were authenticated, no session compatibility was exercised, and no resource measurements were taken. Source-reviewed capabilities and runtime-verified behavior remain distinct.

## Milestone order

| Phase | Deliverable | Gate before proceeding |
|---|---|---|
| 0 | Chosen prerequisites, Codex baseline, and verification contracts | No unknown protocol or credential assumption is presented as supported. |
| 1 | Minimal native account manager and independent windows | Profile, storage, and window bindings behave correctly. |
| 2 | Per-profile authentication and existing-history access | Two identities persist independently and compatible history can be read. |
| 3 | Concurrent chat, approvals, interruption, and recovery | Both accounts perform work without routing, approval, or lifecycle violations. |
| 4 | Usability, bounded memory, and a documented Windows package | MVP scenarios pass and resource use and limitations are recorded. |

Keep Phase 1 small. Do not spend time on visual polish before the authentication and compatibility gates. Use the real application path rather than throwaway demos that could be mistaken for completed features.

## Phase 0: prerequisites and integration contract

Historical Phase 0 areas: development-environment decisions and updates to these two documents. The later UI authorization is recorded above.

Assessment scope, with results recorded below:

- Select a supported .NET 10 SDK and Windows desktop toolchain. Record installation requirements without installing them until requested.
- Choose the official Codex distribution, a source-reviewed baseline version, and an explicit executable-resolution policy. Runtime verification occurs later; do not label an unexecuted baseline as tested. Use a user-installed CLI rather than silently bundling a plugin's binary.
- Define the protocol compatibility table: required account/thread/turn methods, approval request shapes, optional features, and handling of incompatible versions.
- Decide the supported credential-store policy for new homes and how imported storage modes will be verified. Do not assume separate directories establish independent authentication in every mode.
- Specify a private verification workspace and dedicated profiles for future protocol/task checks. Do not experiment on the user's existing home or project by default.
- Specify queue, frame, output, and timeout limits before coding their consumers. Treat initial values as tunable limits rather than benchmark results.
- Recheck experimental API requirements. Keep experiments disabled unless a required feature has a documented reason to enable them.

Acceptance gate:

- Supported prerequisites, the Codex baseline, and required method shapes are named. An unresolved requirement remains a gate; marking it blocked does not permit dependent work to proceed.
- Credential isolation has a concrete verification procedure and an owner.
- Required runtime operations and failure states have defined observable outcomes.
- No validation, migration, or history-repair experiment has modified an existing user profile.

Schema generation, if later requested, must use the chosen CLI version and remain scoped to its protocol contracts. Do not generate schemas as part of this documentation task. The [App Server reference](https://learn.chatgpt.com/docs/app-server) is the contract source, not a guarantee about an arbitrary installation.

### Prerequisite results

| Item | Observed on 16 October 2026 | Decision |
|---|---|---|
| OS | Windows 11 Home, 25H2, x64; build 26200.9711 | Matches the Windows-first target. The registry's legacy Windows 10 product label was superseded by OS inventory. |
| SDK | .NET SDK 10.0.402 | Selected development SDK. No installation needed for this prerequisite. |
| MSBuild | 18.9.15, supplied by the SDK | Available for future authorized builds. |
| Runtime | .NET and Windows Desktop Runtime 10.0.13 | Present; the WPF runtime is available. |
| WPF compilation components | Windows Desktop SDK and reference pack 10.0.13 | Present. No separate `dotnet workload install` is required for this managed WPF design. |
| IDE | No Visual Studio or `vswhere` found in standard installation locations | Optional for the SDK/CLI workflow. This is not an exhaustive machine inventory. |
| SDK pin | No applicable `global.json` reported | When scaffolding is requested, pin 10.0.402 with patch-only roll-forward and prereleases disabled. No pin file was created now. |

Microsoft's [release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json) identifies SDK 10.0.402 and runtime 10.0.13 as the active 13 October release. The [Desktop SDK](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props-desktop) supplies WPF build integration. For a Visual Studio/XAML-designer workflow, use Visual Studio 2026 18.0 or newer with .NET desktop development and .NET 10 components; see [Windows installation guidance](https://learn.microsoft.com/en-us/dotnet/core/install/windows). No IDE, Windows App SDK, C++ toolchain, or Node service is being added as an application prerequisite.

The initial sandboxed `dotnet --info` could not read Windows service metadata. A read-only run outside the sandbox completed successfully. SDK/runtime list commands and WPF pack inspection agreed with that inventory. No build, template generation, restore, or workload installation was run.

### CLI selection and installation state

Selected integration target: official Codex CLI **0.162.1**, tag `rust-v0.162.1`, public stable release published on 9 October 2026. OpenAI's [release metadata](https://api.github.com/repos/openai/codex/releases/latest) reported a non-draft, non-prerelease release; [npm's published package metadata](https://registry.npmjs.org/@openai/codex/latest) agreed on the version. Pin the [specific release](https://github.com/openai/codex/releases/tag/rust-v0.162.1), not a moving latest tag.

Choose the complete official Windows x64 distribution. The release includes `codex-package-x86_64-pc-windows-msvc.tar.gz` and native executable assets; preserve the package's companions and supported layout when it is installed. The alternative is a version-pinned official npm installation with its native platform package intact. Mini Codex should invoke the resolved native CLI directly by absolute path, with structured arguments, rather than run an arbitrary wrapper command.

| Candidate | Finding | Eligibility |
|---|---|---|
| Plugin-managed PATH executable | 0.162.0-alpha.17.2 | Rejected for the initial stable baseline. |
| Desktop-managed PATH executable | 0.162.0-alpha.17.2 | Rejected for the initial stable baseline. |
| Official 0.162.1 Windows x64 distribution | Complete package installed under `tools/codex/0.162.1`; archive SHA-256 matches release metadata | Selected and installed; live runtime verification outstanding. |
| Other CLI versions | Not reviewed | No automatic compatibility claim; review before adding them. |

The version queries attempted Codex's normal temporary alias-directory housekeeping in the existing home; the sandbox denied those writes. Do not repeat diagnostic launches against that home. Future CLI inspection or server verification must use a dedicated, explicitly created verification home. No credentials were read, copied, or changed in this assessment.

The installed archive's SHA-256 is `3e993a82ff393f4530558dcaadb36bb4f0fd1ea7243e621348e40b469f900f6a`. The launcher preserves the package layout, resolves the native executable, and checks its version at runtime. Installation and a successful WPF build do not establish live App Server compatibility or a production-support guarantee.

### Protocol baseline

The following is the required client subset, not a generated schema or a promise that every optional field is supported. Route declarations were checked in the selected tag's [protocol definitions](https://raw.githubusercontent.com/openai/codex/rust-v0.162.1/codex-rs/app-server-protocol/src/protocol/common.rs). Parameter spelling uses the wire's camelCase form; the selected tag remains authoritative when contracts are implemented.

| Request group | Required client subset | Required handling |
|---|---|---|
| `initialize`, `initialized` | Distinct client identity; experimental API disabled | Wait for initialization success. |
| `account/read` | Optional `refreshToken` | Read account summary; do not display tokens. |
| `account/login/start` | `type: chatgpt` or `chatgptDeviceCode` | Track the returned login ID and completion. |
| `account/login/cancel`, `account/logout` | `loginId` for cancellation | Cancel or sign out explicitly. |
| `model/list` | `cursor`, `limit` | Page models and supported reasoning options. |
| `thread/list` | Paging, source filters, `cwd`, recent sorting | Page compatible thread summaries. |
| `thread/read` | `threadId` | Read metadata without starting work. |
| `thread/turns/list` | `threadId`, paging, `itemsView` | Page stored turns. |
| `thread/items/list` | `threadId`, optional `turnId`, paging | Page items where the backing store supports it. |
| `thread/start`, `thread/resume` | Selected working directory/settings; saved ID for resume | Preserve returned thread identity. |
| `turn/start` | `threadId`, text `input` | Start once; never blindly replay. |
| `turn/interrupt` | `threadId`, `turnId` | Await terminal state after acknowledgment. |
| `thread/unsubscribe` | `threadId` | Release an idle subscription. |

Tagged field references: [account](https://raw.githubusercontent.com/openai/codex/rust-v0.162.1/codex-rs/app-server-protocol/src/protocol/v2/account.rs), [models](https://raw.githubusercontent.com/openai/codex/rust-v0.162.1/codex-rs/app-server-protocol/src/protocol/v2/model.rs), [threads](https://raw.githubusercontent.com/openai/codex/rust-v0.162.1/codex-rs/app-server-protocol/src/protocol/v2/thread.rs), and [turns](https://raw.githubusercontent.com/openai/codex/rust-v0.162.1/codex-rs/app-server-protocol/src/protocol/v2/turn.rs). Verify complete response unions and optional/nullable fields before writing DTOs; no DTOs or schemas were generated here.

Use explicit source filters including `cli`, `vscode`, and `appServer`. Additional sources can be exposed deliberately; do not invent a Desktop source kind or make subagent history part of the default top-level list. If item pagination is unavailable for a legacy store, attempt only a documented bounded turn-page route; otherwise report that conversation as unsupported. Never fall back to reading private storage directly.

Implement command/file approval requests, permission requests, and user-input requests before enabling task execution. Bind replies to the original connection and request, respect available decisions, and handle resolution races. Unknown MCP elicitation forms must be explicitly declined or reported unsupported. External-token login, dynamic host tools, remote transports, experimental permission fields, and account-session switching within a single home are outside this baseline. The selected multi-profile/process architecture remains unchanged.

### Credential-policy outcome

New homes use Codex-managed `keyring`, fail sign-in if protected storage is unavailable, and do not fall back to plaintext. Imported homes keep their effective mode and feature configuration. The full mode table and tagged source rationale are in [the architecture policy](ARCHITECTURE.md#credential-storage-policy).

This selects policy without changing the current account's configuration or credentials. Do not inspect `auth.json`, enumerate credential values, or infer authentication from a file's presence. Protect home paths from movement and alias duplication. Treat a conflicting managed requirement as an explicit incompatibility instead of overriding it.

Separate state storage too: remove inherited `CODEX_SQLITE_HOME` for a new child and constrain its effective `sqlite_home` to the new home. Preserve imported configuration, establish its effective state path, and reject state directories shared by two Mini profiles. The environment probe found `CODEX_HOME`, `CODEX_SQLITE_HOME`, `OPENAI_API_KEY`, and `CODEX_API_KEY` unset in this inspection process; the launcher must enforce isolation even when they are set on another launch.

### Credential isolation verification procedure

Owner: the implementer, with the user operating two account sign-ins. This is mandatory before Phase 2 acceptance and was not run in Phase 0.

1. After verification is requested, create `%LOCALAPPDATA%\MiniCodex\verification\<run-id>\` with distinct homes A/B and separate scratch project directories. Do not reuse the current home or this repository for tool-write checks.
2. Create and canonicalize the homes before launch. Apply the selected keyring policy to new-home processes, establish separate state paths, and check effective managed requirements without logging secret values.
3. Sign profile A into identity A and profile B into identity B. Verify identities through account summaries; never inspect stored token contents. Serialize browser sign-in attempts if callbacks conflict and use the supported device-code alternative when needed.
4. Restart the children and then the application. Confirm each identity persists in its own profile and that changing B does not switch A.
5. Logout A through its own server. Confirm B stays authenticated and that A's persisted conversations are not deleted. Reauthenticate A through the supported flow.
6. Exercise authentication expiry/refresh and protected-store failure through separately authorized controlled cases. Confirm failures stay local, do not select plaintext fallback, and do not trigger repeated login or mutation replay.
7. Verify supported imported storage modes in dedicated fixture homes, including `auto` fallback semantics and nonpersistent `ephemeral` behavior. Do not migrate a real imported home to make a check pass.
8. Record CLI version, effective non-secret policy, source/backend configuration, and pass/fail outcomes. Include duplicate-home aliases and shared-state-path rejection. Redact identity details from any shareable report.

Source review supports the isolation design; it does not replace these account and policy checks. File permissions and the OS store protect data within the Windows user's security context, not against every process running as that same user.

### Initial limits and failure behavior

These are the initial implementation limits, not measured performance results. The transport fails visibly rather than dropping lifecycle events or silently retrying a mutation. The 8 MiB frame limit and 16 MiB serialized event budget bound retained payloads before richer rendering is added.

| Control | Initial value | On limit or timeout |
|---|---|---|
| Inbound UTF-8 frame | 8 MiB | Report an unsupported oversized response, invalidate the connection, and preserve uncertain task state. Never partially parse it. |
| Pending client requests | 64 per runtime, reserving 8 for stop/control operations | Delay new ordinary requests; do not evict an in-flight mutation. |
| Server requests awaiting a user | 32 per runtime | Reject further requests using the supported response shape; never auto-approve. |
| Runtime event queue | 512 envelopes, at most 16 MiB serialized UTF-8 per runtime | Dispatch in order; process responses before UI dispatch. Overflow invalidates the connection and preserves uncertain work. |
| History page requests | 50 thread summaries or 100 items | Fetch additional pages on demand. Turn-page fallback is not implemented. |
| Retained conversation display | Six idle/current states per profile plus protected active states; 500 items / 2,097,152 characters per state | Evict older inactive states and mark transcript truncation. Server subscription eviction remains outstanding. |
| Displayed text/output | 131,072 UTF-16 characters per item | Mark display truncation; do not alter Codex's stored output. |
| CLI version check | 15 seconds | Report startup failure; do not restart in an unbounded loop. |
| Handshake and ordinary RPC | 120 seconds | Requests are not automatically retried. A turn-start timeout preserves an unknown outcome. |
| Stop grace period | 30 seconds after interrupt acknowledgment | Keep the window open if terminal state is unconfirmed. Stop-after-connection-loss requires explicit confirmation to end the old process. |
| Login, user approval, complete turn | Server-defined expiry/cancellation; no generic 30-second user deadline | Preserve user control; do not convert waiting into approval or task replay. |

Apply queue management independently from WPF rendering so a blocked UI does not stop processing an interrupt acknowledgment or an approval resolution. Reserve outgoing control capacity for replies as well as stop requests. These limits do not set a fixed number of stored profiles or kill work to reduce RAM.

### Phase 0 disposition

The original prerequisite inspection, baseline selection, and policy resolution are complete. SDK/WPF prerequisites are present. The later integration delivery installed Codex 0.162.1 and implemented the client. Authentication isolation, actual RPC compatibility, Windows sandbox/process-tree behavior, and performance remain unverified later-phase gates.

At the end of the original Phase 0 task, only these two Markdown files had changed. The separately requested UI and integration deliveries are recorded above. No schema generation, test/lint run, git operation, assistant-driven application launch, or account login was performed for the integration delivery.

## Phase 1: minimal WPF application and profile windows

Planned areas: the future `src/MiniCodex` application project; `Accounts`, `Windows`, `Settings`, `Views`, and `ViewModels` folders. No Codex task execution yet.

Future work:

- Create the WPF application targeting `net10.0-windows` with nullable reference types enabled and async command handling.
- Add the account manager and one account window per profile. Bind windows to immutable profile IDs in application code; opening an existing account focuses its window.
- Implement add, rename, link existing home, and remove-from-app operations. Default names remain Account 1, Account 2, and so on, without fixed account variables.
- Persist a versioned registry and UI settings through atomic replacement and recovery from a last-known-good copy.
- Create fresh profile directories only for an explicit Add action. Link existing homes in place, validate directory identity, and prevent duplicate canonical homes.
- Implement explicit application shutdown and a single-instance guard. Restore valid window bounds and handle disconnected monitors.
- Expose truthful status such as not connected or authentication not checked. Do not present a placeholder login or chat feature as functional.
- When measurement is authorized, capture an early manager/one-window/three-window memory baseline before adding rich conversation content. Investigate unexpected window overhead before expanding the UI.

Acceptance gate:

- Several profiles survive restart with unchanged IDs and names.
- Two windows remain independently movable, resizable, and closable, with the profile name in each title.
- No registry entry can accidentally reuse another entry's canonical home.
- Removing a profile preserves its Codex data and credentials.
- A second application launch cannot create a second writer to the profile registry.
- No App Server starts merely because profiles exist in the registry.

## Phase 2: supervised App Server, authentication, and history

Planned areas: `Codex/Processes`, `Codex/Transport`, `Codex/Protocol`, `Codex/Sessions`, account runtimes, and account/history UI.

Future work:

- Resolve a validated executable without accepting arbitrary shell command strings. Report missing and incompatible installations with an actionable error.
- Start one supervised child per opened profile with its own environment. Keep stderr out of protocol parsing and redact authentication details from diagnostics.
- Implement initialization, request correlation, inbound server-request dispatch, bounded buffering, process-exit handling, and connection generations.
- Add supported login, login cancellation, account status, logout, and reauthentication. Serialize conflicting operations within a profile.
- Enforce the Phase 0 keyring policy for new homes and preserve imported policies. Execute the credential-isolation procedure before declaring isolation; confirm logging out of one profile does not affect a distinct profile.
- List conversations, read paged history, and expose supported title/project filtering. Include the required conversation sources and scope queries to the selected home.
- Show missing working directories, inaccessible homes, unsupported methods, and incompatible conversations as explicit states. Do not scan private Desktop storage to fill gaps.
- Extend the authorized memory baseline with the real per-profile App Servers so Codex overhead is visible before the chat milestone.

Acceptance gate:

- Different ChatGPT identities remain authenticated after application and child-process restart.
- A login, logout, or refresh in profile A does not change distinct profile B.
- Cancelling a login cannot later mark the wrong profile authenticated.
- Compatible existing conversations can be listed and read, including App Server-created conversations.
- A new blank profile is distinguishable from an unavailable home or a failed listing request.
- Stderr, malformed output, process exit, and startup timeout cannot hang the UI or route events to another profile.
- Profile removal and logout are separate operations, including for linked homes.

Stop here if credential-store scoping or the selected CLI's history contract cannot meet the requirements. Do not hide a failing gate behind manual credential copying or direct transcript parsing.

## Phase 3: complete task workflow and lifecycle

Planned areas: runtime/thread state, `ApprovalCoordinator`, native conversation items, composer commands, window close coordination, and process-tree supervision.

Future work:

- Implement start/resume, model and supported reasoning selection, working-directory selection, sending, streaming, completion, and interruption.
- Display commands, file-change summaries, tool progress, and errors. Provide approval and user-input handling before allowing turns that could require those interactions.
- Ensure all decisions are bound to their originating profile, thread, connection generation, and pending request. Reject stale callbacks and duplicate submissions.
- Support concurrent threads across accounts and within one account, while preventing two in-flight turns in the same thread. Provide access to every active or approval-blocked thread.
- Warn before Mini starts concurrent work in a project directory already in use. Explain the one-client-per-conversation requirement for imported homes without claiming external locking.
- Implement Stop tasks and close versus Cancel for a busy window, and the equivalent all-profile gate for application exit. Pending approval and login states must also have deliberate close handling.
- Stop and release unused runtimes only after their work and pending operations finish. Dispose window observers without changing another account's lifetime.
- Add crash recovery: invalidate pending operations, show uncertain task outcome, reinitialize on deliberate reconnect, reread identity/history, and never replay an uncertain mutation automatically.
- Evaluate Windows Job Objects and shutdown behavior against the selected Codex sandbox. Reap owned processes on normal shutdown and handle application crashes without claiming that external side effects can be undone.

Acceptance gate:

- Two accounts execute concurrent tasks without mixed messages, identities, approvals, or completion states.
- A blocked approval in account A does not prevent reading or answering account B.
- Stopping one thread leaves unrelated threads running.
- Cancelling a busy-window close preserves its window and all work. Confirming stops its tasks before the window closes.
- A failed stop leaves a visible recovery path; it does not silently force-close the window or terminate work.
- Closing an idle account window does not affect another account's task.
- A lost `turn/start` response cannot cause an automatic duplicate task after reconnect.
- Delayed messages and approval clicks from a previous connection cannot affect the replacement connection.
- Exit and crash scenarios have verified process-tree outcomes, including tool children and sandbox interactions.

## Phase 4: native rendering, memory, and distribution

Planned areas: `Rendering`, presentation preferences, history retention, diagnostics, release packaging, and user documentation. Core protocol and isolation behavior remain the release gate.

Future work:

- Complete the agreed Markdown subset with native selectable content and copy buttons. Keep unsupported formatting readable as plain text.
- Batch live updates, avoid reparsing whole conversations, virtualize visible items, and bound the data retained behind them. Cap displayed command output and indicate truncation explicitly.
- Add light/dark themes, session/project navigation, recent ordering, search within supported semantics, and supported model/reasoning preferences.
- Verify window restoration and release subscriptions, queued updates, history pages, and child processes when they are no longer needed.
- Measure the resource scenarios below before setting final budgets. Address measured problems without terminating active tasks.
- Decide framework-dependent versus self-contained distribution and record the .NET Desktop Runtime and Codex prerequisites. Verify the chosen package on a clean Windows 11 environment.
- Choose an open-source license before release. Write an accurate README and extend documentation with protocol compatibility, security limits, performance results, setup, and known limitations.

Acceptance gate:

- The interface remains usable while streaming and navigating a representative long conversation.
- Repeated window opening and closing has no unexplained sustained growth in processes, subscriptions, pending requests, or retained history.
- The package starts on the documented Windows environment with the stated prerequisites and handles missing Codex cleanly.
- A measured resource report and agreed budgets exist. No placeholder benchmark numbers are published.
- The package identifies Mini Codex as unofficial and makes no promise of universal Desktop-session compatibility.

## Verification strategy for later authorization

Prioritize verification of state boundaries and failure cases. Do not add tests for trivial view properties or tests that merely repeat the implementation. Avoid using the user's real accounts or repository for destructive cases.

| Area | Meaningful verification |
|---|---|
| Profile registry | Duplicate paths and junction aliases, partial writes, corrupted settings, unsupported settings versions, and removal without data deletion. |
| Protocol transport | Fragmented/coalesced frames, interleaved requests and notifications, server request ID collisions, EOF, malformed or oversized messages, timeout, and reconnect generations. |
| Authentication | Two distinct identities, restart persistence, isolated logout, failed/cancelled login, and expired authentication under the selected storage mode. |
| Task state | Duplicate send, late completion, approval resolution races, interrupt during approval, and connection loss after a mutation may have been accepted. |
| Windows and processes | Stop-or-cancel closing, exit during work, last-window behavior, repeated opening, process crash, application crash, and descendant cleanup. |
| History | Blank versus unavailable homes, supported source filters, pagination boundaries, read versus resume, moved working directories, and incompatible items. |
| Native rendering | Select/copy correctness, readable fallback, long output, streaming batch behavior, and UI responsiveness with bounded retained data. |

Use focused unit checks for state and parsing, an explicitly authorized simulated transport for failure injection, and real App Server integration checks for authentication, persistence, approvals, and process behavior. A simulation cannot establish real credential isolation or Desktop compatibility. Manual multi-window scenarios and clean-machine package checks complement automated checks.

The integration request authorizes application code, its official CLI dependency, and a rebuild. Schema generation, tests, interactive account checks, and performance runs still require authorization under the workspace rules.

## MVP completion scenarios

| Scenario | Required observable result |
|---|---|
| A: independent accounts | Add two profiles, sign into different identities, open both windows, restart, and confirm both identities remain independent. Stored profiles are dynamic rather than a hardcoded pair. |
| B: concurrent work | Run a task in each account, switch focus and selected chats, and confirm correct routing and independent completion. |
| C: existing conversation | Link an existing home, list server-exposed history, read a compatible conversation, resume it, restart, and find it again. Explain normal server writes and the one-client workflow. |
| D: busy-window close | Start work, request close, cancel and observe continued work; request close again, confirm stopping, and close only after work stops. Other accounts continue. |
| E: approvals | Show the correct command/file/input request, submit one explicit supported decision, and prevent stale or cross-profile decisions. |
| F: recovery | Lose a connection during work, retain configuration, reconcile persisted history after reconnect, and avoid automatic resubmission. |
| G: removal and logout | Remove a profile without deleting its home or logging it out. Verify separate explicit logout affects only the intended distinct profile. |
| H: shutdown | Exit through the stop-or-cancel flow and verify no unintended owned child processes remain. Record limitations found in crash cases. |
| I: performance | Measure the matrix below and meet budgets agreed from the baseline; explain irreducible Codex/tool overhead. |

Release readiness requires evidence for all applicable scenarios. Record a blocked scenario as a limitation or unresolved release gate rather than describing it as passed.

## Resource measurement plan

| Workload | Measurements |
|---|---|
| Account manager only | App memory, CPU after settling, startup time, and zero unnecessarily started App Servers. |
| One idle account window | App and server memory separately, process count, and idle CPU. |
| Three idle account windows | Total private memory and incremental cost, with comparable history loaded in each window. |
| Two concurrent tasks | App, servers, and tool descendants separately; workload description and UI responsiveness. |
| Long conversation | Retained history size, native visual count where available, scrolling, and streaming behavior. |
| Repeated open/close | Memory/process recovery after settling and unexpected monotonic resource growth. |

Record hardware, Windows version, .NET and CLI versions, history size, warm/cold startup conditions, and sampling method. Report private commit and private working set where available; state exclusions such as the external sign-in browser. Installer/disk size is secondary and must distinguish bundled runtimes from prerequisites.

Numeric budgets are intentionally unset. The user and implementer must agree them after a representative baseline and before performance acceptance. A smaller UI process alone does not establish a lower total footprint.

## Risks and release decisions

| Risk or unresolved choice | Treatment | Owner and deadline |
|---|---|---|
| CLI/protocol changes or experimental surface | 0.162.1 is selected; install the complete distribution, verify its runtime contract, and review any version change deliberately. | Implementer, before Phase 2 acceptance. |
| Credential-store isolation differs from directory isolation | New-home keyring policy is selected; prove two-profile persistence/logout and imported-mode behavior. Do not silently change protection if a check fails. | Implementer with user sign-in, before Phase 2 acceptance. |
| Expected Desktop history is unavailable | Limit compatibility to server-exposed conversations; do not add an unofficial storage parser. | Implementer, Phase 2. |
| External clients or agents touch the same files | Document one-client-per-conversation use and warn on Mini's known project collisions. | Implementer, Phase 3. |
| Process crash leaves uncertain effects or descendants | Never replay uncertain mutations; verify supervision and sandbox compatibility. | Implementer, Phase 3. |
| Native text rendering retains excessive objects | Bound history and visuals, batch updates, and measure real workloads. | Implementer, Phase 4. |
| No agreed memory/startup threshold | Measure first, agree limits, then evaluate readiness. | User and implementer, before Phase 4 acceptance. |
| Packaging inflates disk size or adds prerequisites | Choose framework-dependent or self-contained deployment explicitly. | User and implementer, before packaging. |
| License and release naming are undecided | Confirm the license and unofficial identity before public distribution. | User, before release. |

Cloud sync, remote execution, plugin marketplaces, editor/terminal embedding, advanced Git tooling, automatic updates, cross-platform UI, data deletion, and history merging remain outside the MVP. Do not add them while resolving a failed core gate.
