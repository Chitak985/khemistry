# ISRU maintenance

Add `MAINTENANCE` directly inside a `KHEMISTRYISRU_RECIPE` or local `RECIPE`.
It can also be placed directly inside a `KhemistryISRU` `MODULE`, which applies
it to that module's recipes using the normal recipe override rules. A module
entry replaces a recipe entry with the same maintenance `name`.

Multiple maintenance types can coexist. Each type has one current stage and
independent timers. Its name identifies its saved state on the converter, so
switching recipes or crossing biomes does not reset its wear. Use different
names for unrelated maintenance types in different recipes.

```cfg
MAINTENANCE
{
    name = Screws
    statusOptimal = Like New
    STAGE
    {
        order = 2
        status = Loose
        time = 500
        timeIsRuntime = true
        ignoreOrder = false
        chance = 0.9

        speedMul = 1.2
        passiveMul = 1.1

        RESOURCE_TO_FIX
        {
            name = Tape3000
            amount = 1
        }
        RESOURCE_TO_FIX_NC
        {
            name = RepairTool
            amount = 1
        }
        fixTime = 10
        autoFix = true
        fixersEngineers = 1
        fixersPilots = 0
        fixersScientists = 0
        fixersCrewEngineers = 0
        fixersCrewPilots = 0
        fixersCrewScientists = 0
        fixersCrewSamePart = false
        canFix = true
    }
    STAGE
    {
        order = 1
        status = Badly worn
        time = 1000
        timeIsRuntime = true
        speedMul = 2
        outMul = 0.5
        canFix = false
    }
}
```

`Tape3000` and `RepairTool` are illustrative resource names. Replace them with
resource definitions installed in your game. Unknown resource names invalidate
the recipe.

## Stages and timing

- `name` and `statusOptimal` are required on the maintenance type.
- Each stage requires a unique integer `order`, a nonempty `status`, and finite
  positive `time` in seconds. Highest orders happen first; lowest happen last.
- Initially the type shows `statusOptimal` and applies no maintenance multipliers.
  When a stage activates, its `status` and multipliers replace the preceding stage's.
- Normal stage timers begin after their preceding stage activates. The first
  stage's timer begins in the optimal state.
- `ignoreOrder = true` starts that stage's timer in parallel from the optimal
  state. It may skip intervening stages. Once reached, earlier stages do not
  overtake it. The default is `false`.
- `timeIsRuntime = true` counts actual batch-processing seconds. Stopped,
  unready, or blocked processing does not advance that clock. The default is
  `false`, which uses game universal time, including elapsed time while unloaded;
  that elapsed time is applied when the converter is simulated again.
- `chance` is in `[0, 1]`, defaults to `1`, and is rolled when the stage timer
  expires. A failed roll resets that timer and starts another interval.
- Timers, the current stage, repair progress, pending manual repairs, and the
  automatic-fixing toggle are saved. Runtime is not simulated retroactively
  while unloaded. Repairs require a loaded converter and live requirements.

## Stage history conditions

Within a `STAGE`, these optional values gate its timer using the orders of stages
that have occurred within the same named `MAINTENANCE` type:

```cfg
requiredOrdersAND = 10,42,26
requiredOrdersOR = 8,9
requiredOrdersOR = 4,5
requiredOrderNOT = 99
requiredOrderNOT = 100
```

This requires orders 10, 42, and 26; at least one of 8 or 9; at least one of 4 or 5;
and neither 99 nor 100. Each repeated value adds another AND condition.
`requiredOrderNOT` accepts one integer per value. AND and OR accept nonempty
comma-separated lists of integers, with optional whitespace.

All conditions must pass before the timer starts or advances, including timers
with `ignoreOrder = true`. They do not bypass normal stage ordering. If a NOT
condition becomes false while a timer is running, that timer stops advancing
and the stage cannot trigger. An already active stage is not undone by a NOT
condition becoming false later.

History survives repairs, recipe switches, biome changes, and saves. Failed
chance rolls do not count as occurrences. Skipped stages are not recorded, but
a predecessor activated upon repair is recorded. Use `requiredOrderNOT` with
the stage's own order to make that stage occur at most once.

Duplicate orders within a maintenance type, malformed conditions, and references
to undefined orders reject the recipe and log an error. Other maintenance types
have independent order namespaces and histories. Older saves without history
can recover only their currently active stage; earlier occurrences cannot be
reconstructed reliably.

## Multipliers

Every stage accepts all 13 `BIOME_CONFIG` multipliers, each defaulting to `1`:

`passiveMul`, `inMul`, `outMul`, `speedMul`, `chargeRateMul`, `chargeDecayMul`,
`chargeConMul`, `passivePeriodMul`, `workersEngineersMul`, `workersPilotsMul`,
`workersScientistsMul`, `maxInteractionDistanceMul`, `maxDisplayDistanceMul`.

The effective value is the biome multiplier multiplied by the current stage's
multiplier for each maintenance type. For example, biome `speedMul = 2` and
maintenance `speedMul = 1.5` produce a recipe duration of `recipeTime * 3`.
Within one maintenance type, only its current stage applies. Shared recipe and
biome definitions are not modified.

Multipliers must be finite and nonnegative. `speedMul` and `passivePeriodMul`
must be strictly positive, just as in `BIOME_CONFIG`.

## Fixing

The part menu shows each type and its current status in a Maintenance field.
**Fix maintenance** starts a repair for every currently fixable issue. The
**Auto maintenance fixing: On/Off** button toggles automatic repairs and
defaults to Off. Automatic repair requires both this toggle and the active
stage's `autoFix = true`. The same controls and statuses are available in the
held `partEVA` converter menu.

- `fixTime` is the required repair time in seconds, defaulting to `0`.
- Multiple `RESOURCE_TO_FIX` entries are supported. All required quantities
  are consumed together when the repair finishes, using the converter's normal
  resource route (including AdvancedStorage, or the relevant EVA fluid cells).
- `RESOURCE_TO_FIX_NC` entries check resources present on the vessel without
  consuming them. If the same resource is both consumed and non-consumed, the
  non-consumed quantity must remain after the repair.
- Every fixer count defaults to `0` and must be a nonnegative integer.
  `fixersEngineers`, `fixersPilots`, and `fixersScientists` count nearby EVA
  Kerbals. `fixersCrewEngineers`, `fixersCrewPilots`, and `fixersCrewScientists`
  count seated crew on this vessel. Both sets of requirements must be met.
- EVA fixers must be within the converter's effective interaction distance.
  Suit and inventory converters use a physical range of 7 meters.
- `fixersCrewSamePart = true` limits seated crew to the converter's part;
  it defaults to `false`.
- Missing supplies, non-consumed resources, or fixers pause repair progress.
  Supplies are not reserved while waiting. Turning automatic fixing off pauses
  an automatic repair; a manually requested repair continues.
- Completing a repair returns to the preceding stage in descending order,
  even if a parallel timer had skipped that stage. Repairing the first stage
  returns to `statusOptimal`. The repaired stage's timer resets to zero;
  other parallel timers keep their age.
- Wear can continue during repairs. If a later stage activates, it replaces
  the old repair and its progress. No repair supplies have been consumed yet.
- `canFix = false` makes the stage permanent and ignores all its repair fields.
  `autoFix` defaults to `false`; `canFix` defaults to `true`.

The existing `PINPUT_RESOURCE` `powerfail = MAINT` and Engineer maintenance
button remain a separate failure mechanism.

## Automated checks

The standalone test project compiles the production maintenance code with a
small simulated game host, without loading Unity or KSP assemblies:

```powershell
dotnet restore Tests/Maintenance/Maintenance.Tests.csproj --configfile Tests/Maintenance/NuGet.Config
dotnet run --project Tests/Maintenance/Maintenance.Tests.csproj -c Release --no-restore
```

It covers ordering, parallel timers, chance, runtime/wall clocks, multiplier
composition, repair conditions and consumption, config validation, and saved
state. The actual in-game menus and KSP resource routing still require a flight
test.
