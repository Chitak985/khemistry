# Internal AdvancedStorage

`KhemistryAdvancedStorage` stores contents in a private resource-name/amount dictionary.
It does not create `PartResource` tanks or stock resource bars. Its existing Contents
display remains available; mass and cost include the stored resources.

## Configuration and saves

Keep the existing storage configuration, supported-resource list, charging and
consequence settings. To preload resources, put repeated nodes inside the module:

```cfg
STORED_RESOURCE
{
    name = Water
    amount = 10
}
```

Saves use the same nodes. Existing stock resource nodes belonging to the storage's
supported resources are migrated on startup: their amounts are added to the dictionary
and those stock tanks are removed. Unrelated tanks remain unchanged. Back up a save
before upgrading; older versions of Khemistry cannot read these dictionary contents.
Avoid combining AdvancedStorage with an independent stock tank for the same resource
on the same part, since migration treats it as the storage's legacy tank.

Legacy overfilled or mixed contents are preserved, not truncated. Excess contents
can be withdrawn, but filling and switching remain constrained. Unreadable saved
content nodes are preserved and block filling until corrected.

`single` and `multi` accept only the active resource; `multi` switches only while empty.
`multiShared` shares `maximumResources` across all stored resources. Rates
`maxInputRate` and `maxOutputRate` are aggregate units per second across all resource
names and callers, evaluated per physics tick; a negative rate means unlimited.

Off, Charging, and invalid configurations block transfers without clearing contents.
Explicitly configured `VOID`, boiloff, and other failure consequences still apply.
Turning a container off therefore preserves its contents only if its configured
unpowered consequence permits that.

## Khemistry access

KhemistryISRU vessel transfers and AdvancedStorage charging/passive consumption use
the internal network, with ordinary KSP tanks as fallback. Suit and fluid-cell nearby
selectors can directly transfer to/from AdvancedStorage. EVA converters retain their
existing suit/held-cell routing; this does not grant automatic access to nearby vessels.

Module API:

- `GetStoredResources()` returns a copy of the dictionary.
- `GetStoredAmount(name)` reads actual contents, even when blocked.
- `GetAvailableAmount(name)` and `GetAvailableSpace(name, fillAmount = 1)` include
  current state, rates and capacity restrictions.
- `TakeResource(name, amount)` and `PutResource(name, amount)` return positive units moved.
- `RequestStoredResource(name, signedAmount)` follows KSP's sign convention: positive
  consumes, negative produces; its return has the same sign as the actual transfer.

For vessel-wide access use
`KhemistryResourceNetwork.Request(part, name, signedAmount, flowMode)`. It checks
same-vessel connectivity, NO_FLOW/crossfeed restrictions, visits internal containers
in resource-priority order, then lets KSP handle any remainder in ordinary tanks.
Callers needing an immediate all-or-nothing transaction may supply a
`List<KhemistryResourceNetwork.Transfer>` and call `Rollback(list)` on failure.
Rollback returns each transfer to its exact source and refunds its rate allowance.
Do not retain rollback lists across frames or roll back a successful transaction.

## Stock ISRU compatibility

A flight addon wraps the default `IResourceBroker` used by stock `BaseConverter` /
`ResourceConverter`. Their normal recipe input and output operations can then read,
consume, and fill internal AdvancedStorage, subject to the same access restrictions.
No placeholder, hidden, or temporary stock tanks are created. Normal stock tanks
continue to work through the original broker.

Shared capacity/rate quotas are reserved across a converter's resource names to keep
one output from taking another's promised space. This is conservative: uneven
multi-output recipes may run more slowly than an optimally allocated container.

This is **not** a global patch of `Part.RequestResource`. Engines, stock transfer UI,
stock catalyst/requirement checks that read tank totals directly, unloaded/background
simulation, and mods using their own brokers are not automatically integrated. Other
mods can explicitly use the public APIs above. The bridge leaves custom brokers alone
and logs an error if the expected KSP broker fields are unavailable.

## In-game verification

1. Load an older craft with supported resources in AdvancedStorage. Confirm the amounts
   survive, stock bars disappear, and save/reload retains dictionary contents.
2. While On, transfer between a suit/fluid cell and storage. While Off, verify it is not
   offered as a usable source/destination; check that configured consequences still run.
3. Run a stock ISRU on the same connected vessel using an internally stored input and a
   supported output. Check amounts, mass, capacity and configured transfer limits.
4. Test full/shared containers, missing inputs, crossfeed barriers, resource selection
   while empty, and save/reload. Failed Khemistry transactions should not lose resources.
