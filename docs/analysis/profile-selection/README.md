# Collector profile selection and removal

On 2026-09-27 the Collector exited while clicking profiles in the left sidebar.
Windows recorded exception `0xc00000fd` (stack overflow). The managed crash stack
repeated `CollectionPanelView.ProfileSelection_Changed`,
`MainWindow.ProfileSelection_Changed`, `ProfileRuntime.RefreshProfile`, and WPF
binding transfer. Selecting a profile rebinds the market editor; the duplicate
market validation treated this as a user edit, restored its previous value, and
refreshed the same binding recursively. Existing configuration contained two
Gamma profiles.

The host suppresses editor callbacks during profile rebinding and synchronous
profile refresh. Field callbacks also verify that their editor belongs to the
currently selected runtime. Existing duplicate market settings remain editable
without rewriting profile fields on selection. Explicit duplicate market edits
remain rejected.

The sidebar now exposes Delete profile. Removal uses the existing reader/client
cleanup, persists the remaining profiles, and selects the adjacent profile. Busy
profiles and the last remaining profile cannot be removed.

`tests/CollectorUi.Smoke/ProfileSelectionChecks.cs` uses isolated settings and
real WPF bindings. It switches four distinct profiles and a configuration with
duplicate markets, verifies unchanged fields and persisted edits, rejects a
duplicate market edit, and checks removal, selection, and busy/last-profile
guards. The duplicate fixture reproduced the original stack overflow before
the fix (exit `-1073741571`); both configurations pass after the fix. The smoke
also verifies that production profile/template files remain unchanged.

## Server profiles and shared UI (2026-09-28)

The `+` dialog offers only the four verified servers: Gamma, Black, White,
and Carmine. Their game server IDs come from the shared catalog. Profiles
remain the unit of account, template assignment, client ownership, and market;
the Launcher permits multiple profiles for the same server. Autologin passes
the selected profile's `LoginServerId` to the existing guarded native
`SelectGameServer` call; no native guard or proxy/HWID setting changed.

Both desktop apps use the same sidebar, add-server dialog, launch editor, and
template editor from `PriceCheck.Launching.UI`. Profile-specific Launch and Log
tabs appear above the content; the Collector also has Collection. Templates are
shared across profiles and opened from the sidebar. Each Log filters to the
selected profile; there is no combined log view. Selecting a different profile
does not rewrite its fields during WPF rebinding.

The shared templates entry is now pinned to the bottom of the left sidebar.
Add and delete sit together in the profile-list header; delete is
a red icon disabled for a busy or last remaining profile. Visible UI labels,
template controls, and common runtime states are Russian in both apps. The
presentation translator leaves protocol values and guard behavior unchanged.

The UI smoke also switches profiles and verifies isolated settings. The
previous live build was
`352b9e35b5e34fa5a8775bb44b01f249`. Gamma started and collected with
the new build; its 05:50:07 UTC broker epoch had 1,835 trader operations,
all 1,835 HTTP-acknowledged, and zero local outbox. The latest-broker delivery
counter persists in SQLite and increments only on a successful receipt for
the current epoch. A late older receipt and duplicate cannot inflate it.
`LocalCycle.Smoke` passed these cases and restart persistence.

Black game-server ID 2 is a candidate. Three autologin attempts reached
the server selection stage, but the client displayed "You have been
disconnected from the server" before its character list. A solo attempt
after stopping Gamma had the same outcome, so parallel-client contention is
disproved. This is not yet proof that ID 2 is the Black protocol ID; the
highlighted button may be saved UI state. See `../black-server-login/README.md`.
