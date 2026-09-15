# AdvancedStorage regression tests

Run with the .NET 10 SDK:

```text
dotnet run --project Tests/AdvancedStorage/AdvancedStorage.Tests.csproj -c Release
```

The harness compiles the production storage dictionary, transfer network, and stock
broker adapter against small KSP/Unity doubles. It checks conservation, shared
capacity, transfer rates, save/load and legacy migration, stale selectors, exact
rollback, stock input/output routing, shared output reservations, and broker binding.
It does not substitute for a KSP flight test or simulate the complete stock converter.
