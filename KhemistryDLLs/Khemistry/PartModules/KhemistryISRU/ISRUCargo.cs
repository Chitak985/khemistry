using System;
using System.Collections.Generic;

namespace Khemistry
{
    public partial class KhemistryISRU
    {
        private bool TryPlanCargo(KhemistryISRUBiomeConfig biome,
            IDictionary<string, KhemistryMaterialInstance> inputs,
            IDictionary<string, KhemistryISRURecipe.ResourceOutputMaterial> outputs,
            out CargoInventoryTransaction transaction)
        {
            transaction = null;
            if (_activeRecipe._inputCargoParts.Count == 0 && _activeRecipe._outputCargoParts.Count == 0)
                return true;
            ModuleInventoryPart inventory = GetPowerfailContextPart()?.FindModuleImplementing<ModuleInventoryPart>();
            if (inventory == null)
            { _lastBatchFailureStatus = "No cargo inventory on this part"; return false; }
            try
            {
                transaction = new CargoInventoryTransaction(inventory,
                    moduleType == "partEVA" ? _inventoryStoredPart : null);
                foreach (bool output in new[] { false, true })
                    foreach (var entry in output ? _activeRecipe._outputCargoParts : _activeRecipe._inputCargoParts)
                    {
                        string error = null;
                        if (!TryResolveMaterialReferences(entry.amountExpression, inputs, outputs, true,
                                "cargo amount", out string expression)
                            || !KMathExpr.TryEvaluate(expression, out double amount, out error))
                        { _lastBatchFailureStatus = "Invalid cargo amount: " + error; return false; }
                        amount *= entry.scale * (output ? biome.outputMultiplier : biome.inputMultiplier);
                        if (!KShared.IsFinite(amount) || amount < 0 || amount > int.MaxValue
                            || Math.Abs(amount - Math.Round(amount)) > 1e-9)
                        { _lastBatchFailureStatus = "Cargo amount must be a nonnegative whole number"; return false; }
                        if (!(output ? transaction.Produce(entry.name, (int)Math.Round(amount), out error)
                            : transaction.Consume(entry.name, (int)Math.Round(amount), out error)))
                        { _lastBatchFailureStatus = error; return false; }
                    }
                if (_activeRecipe._outputCargoParts.Count > 0
                    && !transaction.HasCapacity(out string capacityError))
                { _lastBatchFailureStatus = capacityError; return false; }
                return true;
            }
            catch (Exception error)
            {
                _lastBatchFailureStatus = "Cargo inventory error (see log)";
                KShared.LogError(error.ToString(), "KhemistryISRU/TryPlanCargo");
                return false;
            }
        }
    }
}
