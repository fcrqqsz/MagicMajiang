# Remote Added-Kong Visual Design

## Problem

When another player completes an added kong, the local opponent view renders the added tile as a separate meld. The authoritative server state and `ClientGameState` already upgrade the matching pon correctly; only the incremental Unity presentation path is wrong.

`OpponentViewController.ExecuteMeld` currently removes concealed tiles and appends a new meld visual for every meld action. It retains no model of previously rendered melds, so it cannot upgrade an existing pon.

## Design

Add a pure C# `OpponentMeldState` that owns the melds currently represented by an opponent view.

- Snapshot recovery replaces the state with cloned authoritative melds.
- Chi, pon, exposed kong, and concealed kong append a new meld.
- Added kong finds a pon with the same suit and value, changes it to `Kan_Added`, and appends exactly one tile.
- If no matching pon exists, the update fails without creating an orphan meld.
- Clearing an opponent view also clears its retained meld state.

`OpponentViewController` will apply every meld action to this state and rebuild its small set of meld visuals from the resulting model. It will still remove the correct number of concealed tile backs. A failed added-kong upgrade will emit a warning so a projection/view desynchronization is visible without displaying a false extra meld.

## Testing

Extend the standalone network regression suite with a pure-state test that starts with a pon, applies an added kong, and verifies:

- the meld count remains unchanged;
- the matching meld becomes `Kan_Added`;
- the meld contains four tiles;
- a missing matching pon is rejected without changing state.

Run the test once before implementation to observe the expected failure, then rerun the complete network regression suite after the production change.

## Scope

This change affects only client-side opponent presentation state. It does not change server validation, network messages, authoritative snapshots, scoring, or the local player's `HandController` behavior.
