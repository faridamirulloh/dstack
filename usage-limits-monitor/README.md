# Usage Limits Monitor

A portable Windows 11 tray app for viewing Codex quotas across your ChatGPT accounts. Ordinary ChatGPT message limits and OpenAI API billing are outside its scope.

The current build passes 41 focused checks and a native UI smoke check, including tooltip colors in the selected dark/light theme. Both tooltip layouts have been visually inspected. Live Plus/Free account responses remain unverified.

## Run

1. Quit any running older version, then extract `UsageLimitsMonitor-portable.zip` into a folder you intend to keep.
2. Run `UsageLimitsMonitor.exe`. The unsigned executable may trigger a Windows reputation warning; inspect the source and build locally if preferred.
3. In **Settings**, choose a compatible native `codex.exe` if it was not detected. A `.cmd` npm launcher is not supported; select the native executable inside that installation instead. Codex is a separate prerequisite and is not downloaded by the monitor.
4. Choose **Add account**, give it a label, and complete the OpenAI sign-in in your browser. Check which account the browser selects. Repeat for each account you own.

The app uses the .NET Framework supplied with Windows 11. No Node.js runtime, NuGet packages, administrator account, or separate .NET download is required by the monitor itself.

**Start with Windows is enabled on the first actual launch.** Disable it in Settings if unwanted. Extracting the ZIP, building, and running the automated checks do not register startup. If you move the executable, run it once from the new location to update the registration.

## Use

- Hover over the tray icon to see a Windows-style tooltip, without opening or focusing the dashboard. Its background, text, and border follow the selected app theme, with Windows high contrast taking precedence. The compact system font, thin border, shadow, and nonactivating behavior remain; full lines are not cut off by the native tray component's text limit. It includes every account as `F01: 70% / 5d / 3-Sep 14:32`. Each line uses that account's lowest reported remaining percentage and the reset for the same window; equal percentages prefer the later reset. Days/hours are shortened to one whole unit, and dates use your local time. Saved readings include `(stale)`; missing fields stay unavailable. Click the tray icon to open the full dashboard. Windows may initially put the icon in its hidden-icons area.
- Dark theme is the default, including for existing settings that have no theme preference. Uncheck **Dark theme** in Settings to use light mode; the choice persists. Windows high-contrast settings take precedence. Native Windows message boxes and file pickers retain their system appearance.
- Each row is one reported bucket and quota window. Select a row and choose **Pin quota** to use its remaining percentage as the tray number. The rounded number badge uses neutral coloring normally, amber at 20%, and red at 5%. It rounds down to a whole percentage; the table shows more precision. A gray `?` means unknown or stale, not zero. Until you pin a row, the first available quota is shown.
- **Refresh now** updates immediately when no other operation is running. Normal refresh is every 60 seconds. Failed accounts retry after 1, 2, 4, 8, then 15 minutes, while other accounts continue. A snapshot older than 150 seconds, a failed refresh, or an unverified saved snapshot is stale.
- **Credits** is a separate service-reported balance, including when quota is exhausted. `Unavailable` does not mean zero. A plan name does not establish credit eligibility. No credits are purchased and no usage resets are redeemed.
- Windows notifications warn once at 20% and 5% for each account/window. The ledger survives restarts. An initial reading of 5% or less sends only the urgent warning. A reset alert requires a fresh response showing a later window and restored capacity after a zero reading. Without a reported reset, the app cannot reliably rearm alerts. Windows notification settings and Do not disturb can suppress delivery.
- **History** opens a daily-token bar chart for the selected account, with **Table** for exact counts. Choose **7 days**, **30 days** (default), or **All available**. The 7/30-day filters include today and use UTC calendar dates; bucket dates are displayed as OpenAI reports them. The app requests `account/usage/read` on opening History or pressing Refresh history. Missing days stay blank, explicit zero gets a baseline mark, and an unknown count gets an `x`. These are token counts, not historical remaining-quota percentages.
- History is provided only by OpenAI, when your Codex version and account support it. Missing or failed history stays unavailable, without estimates or a local fallback. Token history is held in memory while the window is open and is not saved to disk. Local quota-history recording and its UI have been removed.
- Closing the window hides it; **Quit** stops polling and exits. Launching the app again opens the existing instance.

The app does not impose an account-count or plan-name cap. Performance and live compatibility beyond a few accounts are unverified. Plus and Free are the initial intended accounts; availability of particular buckets, credits, and workspaces depends on Codex's response. Sign-in selects the workspace; this version has no separate workspace picker and rejects duplicate reported identities.

## Credentials and removal

Each account gets a separate app-owned Codex home under `%LOCALAPPDATA%\UsageLimitsMonitor\profiles`. Codex handles OAuth and token refresh. The monitor forces `cli_auth_credentials_store = "keyring"` with no plaintext fallback, scopes `CODEX_HOME` to its child process, and does not copy the default Codex login. Passwords are entered only into OpenAI's browser sign-in, never this app. This is not a usage-only authorization scope; protect access to your Windows account.

Settings, the latest quota snapshots, identity hashes, and alert state are local, unencrypted data under `%LOCALAPPDATA%\UsageLimitsMonitor`. Account labels may be visible in Windows notifications. The monitor stores no email addresses automatically, collects no analytics, discards Codex diagnostics, and never starts agent turns or model requests. Codex may create its own runtime files in each profile.

On normal launch, this version removes the previous recorder's dated `.jsonl` files and interrupted `.jsonl.tmp` writes from `%LOCALAPPDATA%\UsageLimitsMonitor\history`, then removes that empty folder. It validates every entry first and refuses redirected paths or unexpected files. Cleanup failures appear in the dashboard footer. Account profiles, settings, alert state, and the latest quota snapshot are retained. Quit the old version before upgrading so it cannot recreate history.

**Remove** signs out that monitor profile, deletes its local profile and saved account state, and leaves other profiles alone. It does not revoke every remote session or erase OpenAI's service history. Removal stops if logout or safe local cleanup fails; retry after fixing Codex or folder access. To uninstall, remove each account, disable Start with Windows, Quit, then delete the portable folder and remaining local settings directory. Do not delete the profiles first: Codex needs their paths to locate the corresponding credential-store entries.

## Build and checks

Quit any copy running from `artifacts` before rebuilding. From this folder in PowerShell:

```powershell
rtk proxy powershell.exe -NoProfile -File .\build.ps1 -Test
```

This uses Windows' existing Framework C# compiler, writes the executable and ZIP to `artifacts`, and runs the focused checks. If RTK is not installed on another PC, run the command without the `rtk proxy` prefix.

The opt-in integration check uses two new test profiles and invalid dummy keys, makes no model requests, and cleans up its own credentials. It requires access to Windows Credential Manager:

```powershell
rtk proxy powershell.exe -NoProfile -File .\build.ps1 -TestsOnly -ProbeCodex 'C:\path\to\codex.exe'
```

Native UI smoke check with synthetic accounts, no real sign-in, and no startup registration:

```powershell
rtk proxy powershell.exe -NoProfile -File .\build.ps1 -Smoke
```

See [DESIGN-REVIEW.md](DESIGN-REVIEW.md) for the verification record and remaining live-account checks.

## Integration references

The app uses the documented [Codex App Server account and rate-limit methods](https://learn.chatgpt.com/docs/app-server#auth-endpoints) and [Codex credential storage](https://learn.chatgpt.com/docs/auth#credential-storage). It displays optional fields without inventing missing balances or limits. These interfaces can change; select a compatible newer Codex executable in Settings if a future version stops working.

[OpenAI token usage history](https://learn.chatgpt.com/docs/app-server#7-token-usage-chatgpt) is a separate interface from remaining quota. Daily buckets are optional; live availability on the owner's Plus/Free accounts has not been verified.
