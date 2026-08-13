# Task 10 Report — deterministic AI talent strategy

## Recovery inventory

- Recovered the interrupted Task 10 work on branch `codex/talent-actions-ui-unified` at prerequisite HEAD `204e4cc` (`fix: compile result presentation policies in regression`).
- The original task base is `5a2c459539120d5a3f08ca4563e4cc197e88b999`; `204e4cc` intentionally adds the Task 9 result presentation policy to the regression project before this task.
- Preserved every inherited working-tree change. No reset, checkout, destructive cleanup, or edit to ignored Unity-generated assembly projects was performed.
- The pre-existing untracked `.superpowers/brainstorm/` directory was inspected only to identify its boundary and is excluded from staging.

## Scope delivered

- Added `AiTalentLoadoutFactory` with three deterministic archetypes: burst, control, and information/value. It starts from the standard 34-tile deck, uses registry-backed talent IDs, fills only tier-compatible slots, validates every addition through the real `PlayerLoadoutCodec`, and leaves unaffordable slots empty.
- AI room fill now decodes the factory message against the room preset and locks a real preset-legal loadout. A codec failure falls back to a standard deck with an empty strict 6+3 talent configuration.
- Added `AiTalentDecisionPolicy`, which consumes only authoritative `TalentActionOption` values. It prefers an armed `sheathed_edge` finisher, then `interception` against the highest `TargetPublicCharge`, with target-seat, target-talent, and source-talent deterministic tie breaks.
- Permanent and temporary AI invoke the policy once at each main-turn discard boundary: after draw, or after an accepted chi/pon. The policy snapshots the current main decision and routes the chosen action through `GameServer.SubmitNetworkTalentAction`; rejection does not retry or loop.
- Added `TargetPublicCharge` to the authoritative option model. `InterceptionTalent` populates it from already public charge targets. The value travels only through the requesting seat's private talent-state message/recovery snapshot, then through deep-copying client and panel projections. Other seats receive an empty private option set.
- AI sideboarding runs synchronously when the room opens the phase. It sees only carried nine IDs plus revealed public opponent talent snapshots, retains `MainOnlyLocked` talents, promotes `interception`/`composure` against a revealed charged large threat, then applies archetype priority. Over-budget candidates remove flexible lowest-priority entries first, with highest cost as the deterministic secondary removal key.
- Every sideboard result is validated by `SideboardLoadoutPolicy` before tracker/runtime mutation. Any policy, validation, or tracker failure explicitly locks the original active set. AI seats do not consume the human 45-second timer.
- No `GameServer`/`Room` execution branch was added for a concrete `talentId` or `effectId`. Concrete configuration IDs occur only in `AiTalentLoadoutFactory` and `AiTalentDecisionPolicy`.

## TDD evidence

### Inherited RED → GREEN cycles

- Loadout RED established all three presets × four seats through the real codec, exact 6+3 arrays, no duplicate carried IDs, deterministic same-input output, tier compatibility, and locked-active `starting_capital`. GREEN introduced `AiTalentLoadoutFactory` and room admission.
- Active-policy RED established finisher precedence, interception charge/seat/talent ordering, null/empty behavior, one submission with the current 64-bit decision ID, and no retry after rejection. GREEN introduced `AiTalentDecisionPolicy` plus the `SimpleAIClient` main-turn hook.
- Privacy RED established that target charge survives private snapshot/recovery/client cloning while mutations to source, wire snapshot, and first reads do not affect later reads; a different seat receives no option or target data. GREEN added the option field through the authoritative private projection chain.
- Sideboard RED established locked retention, charged-large counters, authoritative validation, deterministic original fallback, and immediate Room AI locking. GREEN added policy selection and Room integration.
- The recovered working tree was already GREEN on the focused filter. No new production behavior was added during recovery without an observed RED. Existing Room sideboard integration already proves that three AI seats lock immediately while the human seat retains a 44–45 second deadline, so no duplicate or source-shape test was added.

### 100 seeded sequences

- Seeds `0..99` rotate all presets and seats, validate factory output through the codec, check carried uniqueness and budget, require a validated completed sideboard choice, and exercise real `TalentMatchRuntime` options/activation.
- Each runtime sequence checks a real public charged target, rejects a stale decision ID, accepts the current decision, and verifies non-negative interception uses and target charge.
- This is deliberately a policy/runtime invariant suite, not a claim of full asynchronous `GameServer` E2E coverage.

## Authority, privacy, and lifecycle review

- Active AI policy input is only `GetAvailableTalentActionsSnapshot(aiSeat)`; it has no hand, Peek-wall, opponent-private-talent, or raw runtime-entry parameter.
- Sideboard policy input is the AI's own trusted carried loadout, original active IDs, and `SnapshotKnownTalent[]` constructed from opponent entries that are already `IsRevealed`. It never receives concealed hands or Peek results.
- `TargetPublicCharge` is derived from `IPublicChargeTalent.GetCurrentCharge` only after runtime public-target filtering (`other owner`, active, revealed, positive charge).
- The supplemental talent action does not mark or replace the base action. `SimpleAIClient` continues its existing discard/hu flow after the one-shot attempt.
- Chi/pon continuations execute after the response decision is closed and the next loop opens a new main decision for the claiming player, so the same admission path validates the current decision.
- AI sideboarding is executed inside `BeginSideboard` before public progress broadcast; only online humans receive `SideboardStarted` and wait on the server deadline.

## Verification

- Focused `ai-talents`: exit 0 in 5.8 seconds, `Network regression tests passed.`
- Focused `sideboard`: exit 0 in 5.0 seconds, `Network regression tests passed.`
- Focused `talent-actions`: exit 0 in 4.6 seconds, `Network regression tests passed.`
- Full `NetworkRegression`: exit 0 in 9.5 seconds, `Network regression tests passed.`
- `GameServer.cs` + `Room.cs` concrete `talentId`/`effectId` equality-or-switch guard: `NO_MATCH`.
- Configuration-ID location scan: all nine archetype IDs occur only in `AiTalentLoadoutFactory.cs` and `AiTalentDecisionPolicy.cs` within the inspected Agent/Room/GameServer scope.
- Privacy-field scan confirms the production chain is limited to the option model, interception producer, Room/private snapshot, client deep copies, and panel deep copy; regression fixtures/assertions cover owner-only projection.
- Both new script `.meta` files exist; GUIDs are unique across `Assets`.
- `NetworkRegression.csproj` is not ignored. No ignored Assembly project was edited.
- `git diff --check`: exit 0; only repository line-ending conversion notices.

## Self-review

- No Critical or Important issue found against the Task 10 brief.
- The requested independent reviewer could not be spawned because all four collaboration slots were already occupied. A line-by-line self-review was performed using the same plan-alignment, architecture, testing, privacy, and production-readiness checklist.
- Confirmed the sideboard removal comparator: lowest archetype priority is removed first and highest cost breaks equal priority; public counters are protected by the highest priority value.
- Confirmed the inherited `RoomSessionTests` adjustments are baseline assembly corrections for the newly non-empty AI talent loadouts: assertions now isolate the human `starting_capital`/`draw_reward` owner instead of assuming all AI seats have zero score/events.

## Commit

- Message: `feat: add deterministic AI talent strategy`

## Concerns

- Unity/Tuanjie Editor compilation and batch execution remain deferred to Task 12, per the phase plan. This task is verified through the real pure-C# regression assembly, runtime policy tests, Room integration tests, static authority/privacy guards, and Unity meta validation.
- The 100-seed suite is intentionally policy/runtime coverage and does not simulate the production asynchronous `GameServer` loop end to end.
- The pre-existing `.superpowers/brainstorm/` directory remains untouched and excluded from the commit.

## Fix R1 — inactive sideboard threats

- Fixed the reviewed mismatch in `Room.BuildPublicKnownOpponentTalents`: AI sideboard threat input now requires `entry.IsActive` as well as `entry.IsRevealed`. This matches the existing runtime public-charge targeting rule and does not alter the sticky public HUD snapshot, which still retains the revealed event/value for inactive carried talents.
- RED → GREEN evidence: the focused `ai-talents` command first failed with `Room AI threat input ignores a sideboarded revealed charged talent while retaining active public opponents only`. The new closest-production fixture uses a real `TalentMatchRuntime` to charge three `sheathed_edge` entries to public value 3, then uses `ReplaceActiveSet` to swap one out. It confirms an active revealed large target promotes `interception` and `composure`, then confirms the swapped-out entry remains revealed with value 3 but no longer promotes either counter. It also excludes the requesting seat and an owner-only hidden opponent.
- GREEN verification: focused `ai-talents`, focused `sideboard`, and the full `NetworkRegression` assembly all exit 0. The no concrete talent/effect-ID branch guard over `Room.cs`/`GameServer.cs` returned `NO_MATCH`; `git diff --check` exited 0.
- Reviewer minors recorded only, intentionally outside this fix: no asynchronous production `GameServer` AI lifecycle E2E test was added; the existing 100-seed policy/runtime depth was not expanded; exact score effects for AI seats were not broadened.

## Fix R2 — isolate the active positive fixture

- No production change. Review found that the Fix R1 active-positive phase still left seat 1's charged `sheathed_edge` active, so counter promotion could not be attributed solely to the reactivated seat 2 target.
- RED → GREEN evidence: the test first required the Room public-known input to contain exactly one entry for seat 2; focused `ai-talents` failed at `active revealed charged large threat promotes AI counter talents`, proving the fixture leaked seat 1. The fixture now deactivates seat 1 before activating seat 2. It asserts the active input contains exactly seat 2, verifies counter promotion, then deactivates seat 2 and verifies its sticky revealed value remains 3 while no counter is promoted. Self and owner-only hidden exclusions remain covered.
- Verification: focused `ai-talents`, focused `sideboard`, and full `NetworkRegression` exit 0; Room/GameServer concrete talent/effect-ID guard reports `NO_MATCH`; `git diff --check` exits 0.
