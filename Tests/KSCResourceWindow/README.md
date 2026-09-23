# KSC resource window checks

This isolated .NET 10 harness compiles the production data helpers without loading
Unity or KSP. It tests nested grouping, parameter-key identity, totals, text sorting,
mass units, sale validation, changing balances, and invalid/overflowing amounts.
Failures are caught and reported to the console instead of opening an exception dialog.

```powershell
dotnet restore Tests/KSCResourceWindow/KSCResourceWindow.Tests.csproj --configfile Tests/KSCResourceWindow/NuGet.Config
dotnet run --project Tests/KSCResourceWindow/KSCResourceWindow.Tests.csproj -c Release --no-restore
```

In-game checks (not simulated by this harness):

1. Open a save in Space Center, VAB/SPH, Tracking Station, and Flight. Check the
   green list toolbar icon, top-left title, top-right X, and toolbar toggle.
   No icon should appear on the main menu. Hide/show the game UI and switch scenes.
2. Expand Resources. Verify live KSC amounts, display names, abbreviations, and
   ton/kg/g density conversions. Check Info opens the correct KEI resource.
3. Click each header twice. Resources initially sort by Name; material tables
   initially sort by Total Amount. Sorting is text/alphanumeric, letters before
   digits, so "10" precedes "2". Each nested table keeps its own sort.
4. Expand Materials, a material name, a parameter combination, and a shape.
   Check totals at each level, sorted parameter headers, horizontal scrolling
   when many parameters exist, and nesting margins at different screen sizes.
5. Sell a partial resource amount in Career: check the debit and amount*unitCost
   funding transaction, then save/reload. Repeat in Science and Sandbox and verify
   removal without payment. Cancel/X must not change balances.
6. Try empty, negative, nonnumeric, infinite, excessive and extremely small amounts.
   Change the KSC balance while a popup is open and check that confirmation uses
   the current balance. Check no editor/flight controls activate through the window.

Material tables are read-only; only normal resources have Sell/Info actions.
