# Independent Casual map preferences - 2026-11-24

Player note: 4v4 and 5v5 Casual now save separate map selections. A map can be
checked for one size and unchecked for the other. Ranked remains separate.

Prepared source change; not deployed. Requires the matching updated client.
This branch also includes the pending one-cube map protection repair.

Preference slots are 0=Casual 4v4, 1=Ranked (both unchanged), and 2=Casual 5v5.
Public `map_preferences_version` is now 2, advertising support for the third
slot. Command 36's wire version remains 1; framing and record format are unchanged.
New clients send slot 2 only when the server advertises capability >=2.
Old clients can still send slots 0/1. They abstain in 5v5 rather than causing a
4v4 preference to apply there. Normal server selection supplies missing votes.

`MatchmakerInitiator` snapshots the slot for its actual `playersPerTeam` override,
falling back to the card's team size. Automatic 5v5 uses the 5v5 preferences even
though it runs under the four-player Casual card. Pop snapshots still deep-copy
only the matched roster; later edits and backfills cannot alter those votes.
Pool eligibility, weighting, majority/tie rules and minimum three choices are
unchanged. No new queues are introduced.

The client retains existing per-account storage slots for 4v4/Ranked and adds
slot 2 for 5v5. If the slot is absent it copies the old Casual exclusions once
and saves the copy. Later edits are independent, including after account reload.
Each tab displays, saves and publishes its own list. The tile cache includes
the slot, so switching tabs with identical map lists rebuilds the callbacks.
Checkboxes continue to use the existing native style and remain locked while
queued. Every queue entry republishes all supported lists before entering.

Validation: `dotnet run --project tests/BNLReloadedServer.MapPreferencesFixture`
passes 1,496 checks, including differing 4v4/5v5 majority outcomes across 100
seeds, the automatic 5v5 override, pop immutability, old-client abstention,
invalid slots and all three coalesced frames before queue entry.
The matching Unity Editor fixture passes 36 checks for account migration,
separate saved choices, native checkboxes, slot capture, capability gating,
eligible 5v5 payloads and publication ordering. A live match was not tested.
