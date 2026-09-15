using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Khemistry
{
    /// <summary>Stock converter adapter. Engines and arbitrary Part.RequestResource callers
    /// do not use IResourceBroker and are deliberately outside this integration.</summary>
    public sealed class KhemistryStockResourceBroker : IResourceBroker
    {
        private readonly IResourceBroker stock;
        private readonly BaseConverter converter;
        private readonly Dictionary<string, Dictionary<KhemistryAdvancedStorage, double>> inputLimits =
            new Dictionary<string, Dictionary<KhemistryAdvancedStorage, double>>();
        private readonly Dictionary<string, Dictionary<KhemistryAdvancedStorage, double>> outputLimits =
            new Dictionary<string, Dictionary<KhemistryAdvancedStorage, double>>();
        public KhemistryStockResourceBroker(IResourceBroker original, BaseConverter owner)
        {
            stock = original;
            converter = owner;
        }
        private static string Name(int id) => PartResourceLibrary.Instance.GetDefinition(id)?.name;

        public double AmountAvailable(Part part, string resName, double deltaTime, ResourceFlowMode flowMode)
        {
            var limits = KhemistryResourceNetwork.ConnectedStorage(part, resName, flowMode)
                .ToDictionary(s => s, s => s.GetAvailableAmount(resName) / InputCount(s));
            inputLimits[resName] = limits;
            return stock.AmountAvailable(part, resName, deltaTime, flowMode) + limits.Values.Sum();
        }
        public double AmountAvailable(Part part, int resID, double deltaTime, ResourceFlowMode flowMode)
            => AmountAvailable(part, Name(resID), deltaTime, flowMode);

        // Share each container's per-tick budget across this converter's inputs/outputs.
        // Stock probes each resource separately before committing the complete recipe.
        private int InputCount(KhemistryAdvancedStorage storage)
            => storage.maxOutputRate < 0f ? 1 : Math.Max(1, converter.inputList
                .Select(r => r.ResourceName).Distinct()
                .Count(n => storage.GetAvailableAmount(n) > 0.0));
        private int OutputCount(KhemistryAdvancedStorage storage)
            => Math.Max(1, converter.outputList.Select(r => r.ResourceName).Distinct()
                .Count(n => storage.GetAvailableSpace(n) > 0.0));

        public double StorageAvailable(Part part, string resName, double deltaTime,
            ResourceFlowMode flowMode, double FillAmount)
        {
            var limits = KhemistryResourceNetwork.ConnectedStorage(part, resName, flowMode)
                .ToDictionary(s => s, s => s.GetAvailableSpace(resName, FillAmount) / OutputCount(s));
            outputLimits[resName] = limits;
            return stock.StorageAvailable(part, resName, deltaTime, flowMode, FillAmount)
                + limits.Values.Sum();
        }
        public double StorageAvailable(Part part, int resID, double deltaTime,
            ResourceFlowMode flowMode, double FillAmount)
            => StorageAvailable(part, Name(resID), deltaTime, flowMode, FillAmount);
        public double RequestResource(Part part, string resName, double resAmount,
            double deltaTime, ResourceFlowMode flowMode)
        {
            if (!KShared.IsFinite(resAmount) || resAmount <= 0.0) return 0.0;
            double moved = MoveQuoted(part, resName, resAmount, flowMode, inputLimits);
            return moved + (moved < resAmount
                ? stock.RequestResource(part, resName, resAmount - moved, deltaTime, flowMode) : 0.0);
        }
        public double RequestResource(Part part, int resID, double resAmount,
            double deltaTime, ResourceFlowMode flowMode)
            => RequestResource(part, Name(resID), resAmount, deltaTime, flowMode);
        public double StoreResource(Part part, string resName, double resAmount,
            double deltaTime, ResourceFlowMode flowMode)
        {
            if (!KShared.IsFinite(resAmount) || resAmount <= 0.0) return 0.0;
            double moved = MoveQuoted(part, resName, -resAmount, flowMode, outputLimits);
            return moved + (-moved < resAmount
                ? stock.StoreResource(part, resName, resAmount + moved, deltaTime, flowMode) : 0.0);
        }
        public double StoreResource(Part part, int resID, double resAmount,
            double deltaTime, ResourceFlowMode flowMode)
            => StoreResource(part, Name(resID), resAmount, deltaTime, flowMode);

        private double MoveQuoted(Part part, string name, double amount, ResourceFlowMode flow,
            Dictionary<string, Dictionary<KhemistryAdvancedStorage, double>> quotes)
        {
            double moved = 0.0;
            quotes.TryGetValue(name, out var limits);
            foreach (var storage in KhemistryResourceNetwork.ConnectedStorage(part, name, flow))
            {
                double limit = 0.0;
                if (limits != null) limits.TryGetValue(storage, out limit);
                // Dumpable outputs are not probed by stock. Give them only unreserved space.
                else if (amount < 0.0)
                {
                    double reserved = quotes.Values.Sum(q =>
                        q.TryGetValue(storage, out double value) ? value : 0.0);
                    limit = Math.Max(0.0, storage.GetAvailableSpace(name) - reserved);
                }
                double step = Math.Min(Math.Abs(amount - moved), limit) * Math.Sign(amount);
                moved += storage.RequestStoredResource(name, step);
                if (Math.Abs(moved) >= Math.Abs(amount)) break;
            }
            quotes.Remove(name);
            return moved;
        }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    [DefaultExecutionOrder(-10000)]
    public sealed class KhemistryStockStorageBridge : MonoBehaviour
    {
        private static readonly FieldInfo BrokerField = typeof(BaseConverter).GetField(
            "_resBroker", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo ProcessorBrokerField = typeof(ResourceConverter).GetField(
            "_broker", BindingFlags.NonPublic | BindingFlags.Instance);
        private bool failed;

        public void FixedUpdate()
        {
            if (failed || FlightGlobals.VesselsLoaded == null) return;
            try
            {
                if (BrokerField == null || ProcessorBrokerField == null)
                    throw new MissingFieldException("KSP converter broker fields are unavailable.");
                foreach (Vessel vessel in FlightGlobals.VesselsLoaded)
                {
                    if (vessel == null || !vessel.loaded
                        || !vessel.parts.Any(p => p.Modules.OfType<KhemistryAdvancedStorage>().Any()))
                        continue;
                    foreach (BaseConverter converter in vessel.parts
                        .SelectMany(p => p.Modules.OfType<BaseConverter>()))
                    {
                        IResourceBroker original = converter.ResBroker;
                        if (original == null || original.GetType() != typeof(ResourceBroker)) continue;
                        ResourceConverter processor = converter.ResConverter;
                        if (processor == null || !ReferenceEquals(
                                ProcessorBrokerField.GetValue(processor), original)) continue;
                        var adapter = new KhemistryStockResourceBroker(original, converter);
                        ProcessorBrokerField.SetValue(processor, adapter);
                        BrokerField.SetValue(converter, adapter);
                    }
                }
            }
            catch (Exception exception)
            {
                failed = true;
                KShared.LogError("Stock AdvancedStorage bridge disabled: " + exception,
                    "KhemistryStockStorageBridge");
            }
        }
    }
}
