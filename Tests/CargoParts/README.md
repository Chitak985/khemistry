# Cargo recipe tests

Run with a modern .NET SDK (no KSP process needed):

```powershell
dotnet run --project Tests/CargoParts/CargoParts.Tests.csproj
dotnet run --project Tests/Expressions/Expressions.Tests.csproj -- --cargo
```

The first harness compiles the production cargo transaction and batch-planning
code against a minimal inventory host. It covers stacking, capacity, rollback,
protected held converters, independent snapshots, expression counts, and context
inventory selection. The second tests the real recipe parser and expression
engine, including the output-node alias and material/setting references.

The KSP host is a test double, not an in-game integration test. In KSP, verify a
batch on a normal converter, a kerbalEVA converter, and a held partEVA converter;
try missing inputs, insufficient slots/mass/volume, settings-based counts,
save/reload, and a batch whose consumed parts free space for its outputs.
