# Broker changes before the first exact price

2026-09-26. User screenshots: sukro, `INVENTORY_CHANGED`, `Never checked`;
Aelet, `INVENTORY_CHANGED`, price age0.1h.

## Confirmed cause and examples

The broker already supplies item identity, side, listing count and quantity
before an individual shop price has ever been read. The server previously
computed `changed = !old || ...` and labeled every true result
`INVENTORY_CHANGED`. A first observation therefore falsely implied an inventory
comparison. Reopening, movement and kiosk-type changes also had that label.

- sukro first seen13:29:24, broker started13:28:54, items5312/5537. No previous
  price existed at the screenshot. The next route7895a683 read it and the
  server completed it13:32:10 (2exact rows).
- Aelet had1920/1918 in exact snapshot13:26:40. Broker6e9648f then added4968
  Recipe: Kris (60%) and4973 Recipe: Earring of Black Ore (70%). Same position,
  same ObjectID; exact snapshot13:31:33 confirmed all four. This was a real
  addition after a recent price, not a quantity-only false recheck.

## Server correction

`cycle-change.policy.ts` distinguishes INITIAL_IMPORT, REOPENED,
KIOSK_TYPE_CHANGED, MOVED and NEW_LISTINGS, preserving the previous scheduling
predicate, priorities, revisions and deadlines. Quantity alone and removals do
not enqueue another visit. `state` gives readable labels; `claim` retains raw
reason codes. Historical generic INVENTORY_CHANGED is shown as
`Shop changed (broker)`, without inventing a specific past cause.

4policy tests, API typecheck/build and2market-cycle +1distributed integration
tests passed on dedicated pricecheck_cycle_test_* databases. Integration
asserts initial reason, unchanged quantity-only revision and reopened reason.
The first test attempt expected COMPLETED after an unleased legacy fixture
price; corrected assertion to the unchanged revision (that fixture does not
claim a queue lease). Production API dev watcher loaded the new UI labels.
Live verification: V43 broker13:43:37 created Poolardas with INITIAL_IMPORT,
UI state First price check/Never checked, exact accepted13:44:09. Movement and
reopened jobs showed Shop moved/Shop reopened. BabyCryst was first seen20.09
but never checked; a reopened reason with Never checked is legitimate for that
older shop. No historical reason relabeling was guessed in PostgreSQL.
