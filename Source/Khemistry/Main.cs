using CommNet.Network;
using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using UnityEngine;
using static PartResource;

// NOTES:
// Deposit generation does not save biomes as they aren't needed anywhere other than placing the deposit at the correct position
/* Example deposit config:
KHEMISTRY_RESOURCE_DEPOSIT
{
    type = surface               // Can be surface (surface and underground deposit), surfaceOnly (only surface deposit), or underground (only underground deposit). Will fail to load if this is not defined.
    render = true                // Only checked for surface and surfaceOnly, this will render a model at where the deposit is if true. Defaults to false.
    maxAmount = 10               // Maximum amount of deposits allowed. Defaults to 10.
    minAmount = 5                // Minimum amount of deposits allowed. Defaults to 5.
    body = Kerbin                // What celestial body the deposit is located on. Will fail to load if this is not defined.
    biome = Shores               // If this is set, the deposit will only spawn in this biome.
    resource = H2O               // What resource is in this deposit. Will fail to load if this is not defined.
    resource2 = H2               // What resource is stored in the underground version of this deposit. This is only checked for surface deposits and the deposit will not load if it's not defined.
    depthSurface = 10            // For surface and surfaceOnly deposits, this is how deep the top layer of the deposit is in meters. Not checked for underground deposits. Defaults to 10.
    depthUndergroundStart = 100  // For underground deposits, this is the depth that the deposit starts at in meters. Not checked for surface and surfaceOnly deposits. Defaults to 100
    depthUnderground = 50        // How deep the underground part of the deposit is in meters. This is not checked for surfaceOnly. Defaults to 50
    minRadius = 10               // Minimum radius of the deposit in meters. Defaults to 10
    maxRadius = 20               // Maximum radius of the deposit in meters. Defaults to 20
}
*/
// KHEMSITRY_RECIPE can be put as a node at top level, and then a KhemistryAdvancedRecipeISRU can import all with a type of recipeType, as well as add INPUT_RESOURCE and OUTPUT_RESOURCE.
/* Example recipe config:
KHEMISTRY_RECIPE
{
    recipeType = electrolysis

    ConverterName = Collect Earth Air             // Converter name, must be unique
    StartActionName = Start collecting Earth Air  // Button name for starting the converter
    StopActionName = Stop collecting Earth Air    // Button name for stopping the converter
    planetCondition = Earth                       // Converter can only operate on this planet. Do not include if can work anywhere
    biomeCondition = Cool Deserts                 // Converter can only operate in this biome. Do not include if no planetCondition or can work anywhere on that planet
    altitudeMaxCondition = 10000                  // Maximum altitude from sea level this ISRU can operate at. Requires altitudeMinCondition, do not include if no altitude restrictions
    altitudeMinCondition = 0                      // Minimum altitude from sea level this ISRU can operate at. Requires altitudeMaxCondition, do not include if no altitude restrictions
    situationCondition = Landed                   // Converter can only operate in this situation. Possible values are Landed, Splashed, FlyingLow, FlyingHigh, SpaceLow, SpaceHigh, SubOrbital. Do not include the value to ignore this condition.
    depositCondition = GSOre                      // Converter can only operate when over this deposit. Should be the resource value of a surface deposit.
    powerfailResource = LVEnergy                  // If this resource runs out, the part will powerfail. Must be an INPUT_RESOURCE. Do not include to disable powerfails.
    powerfailResult = EXPLODE,10                  // The result if a powerfail occurs. Can be "EXPLODE,n", "MAINT", or "STOP". Requires powerfailResource to be set and valid.
                                                  // EXPLODE will explode the part with power n, MAINT will require an Engineer kerbal to come fix it, and STOP will just shut down the part.
    manualOperation = true                        // false by default; enables manual cycle mode
    manualRequiresStartup = false                 // true by default; if false, no Start/Stop, just Execute Cycle
    startStopShowRules = EVA+PAW                  // "PAW" default; controls Start/Stop button visibility
    manualShowRules = EVA                         // "PAW" default; controls Execute Cycle button visibility
    maxInteractionDistance = 5.0                  // 10.0 default; applies to all EVA-visible buttons
    recipeGroup = myGroup                         // null by default; enforces one-active-at-a-time per group. If null, the converter does not have a group.

    INPUT_RESOURCE
    {
        ResourceName = LVEnergy
        Ratio = 2
        FlowMode = STAGE_PRIORITY_FLOW
    }
    OUTPUT_RESOURCE
    {
        ResourceName = EarthAir
        Ratio = 1
        DumpExcess = false
    }
}
*/
/* Example KhemistryAdvancedRecipeISRU
MODULE
{
    name = KhemistryAdvancedRecipeISRU
    recipeType = electrolysis           // Imports all KHEMISTRY_RECIPE with this recipeType
    multiplier = 10                     // Multiplies all inputs and outputs by this value
    INPUT_RESOURCE                      // Additional inputs or outputs added to each recipe imported
    {
        ResourceName = IVEnergy
        Ratio = 2
        FlowMode = STAGE_PRIORITY_FLOW
    }
    RECIPES                             // List of recipes to import with type recipeType (optional)
    {
        name = Electrolyze Sea Water    // Use name values to specify which recipes are imported, checks based on ConverterName
        name = Electrolyze Ocean Water
    }
    RECIPE_MULTIPLIERS                  // Multiplies each imported recipe by an amount (optional, if included will require RECIPES)
    {                                   // Each amount value corresponds with each name value in RECIPES and multiplies that recipe.
        amount = 10                     // Even if you want to import all recipes of that type, you will have to put them all into RECIPES first.
        amount = 5
    }
}
*/
/* Sample config for advanced ISRU (normal/EVA):
MODULE
{
    name = KhemistryAdvancedISRU
    ConverterName = Collect Earth Air             // Converter name, must be unique
    StartActionName = Start collecting Earth Air  // Button name for starting the converter
    StopActionName = Stop collecting Earth Air    // Button name for stopping the converter
    planetCondition = Earth                       // Converter can only operate on this planet. Do not include if can work anywhere
    biomeCondition = Cool Deserts                 // Converter can only operate in this biome. Do not include if no planetCondition or can work anywhere on that planet
    altitudeMaxCondition = 10000                  // Maximum altitude from sea level this ISRU can operate at. Requires altitudeMinCondition, do not include if no altitude restrictions
    altitudeMinCondition = 0                      // Minimum altitude from sea level this ISRU can operate at. Requires altitudeMaxCondition, do not include if no altitude restrictions
    situationCondition = Landed                   // Converter can only operate in this situation. Possible values are Landed, Splashed, FlyingLow, FlyingHigh, SpaceLow, SpaceHigh, SubOrbital. Do not include the value to ignore this condition.
    depositCondition = GSOre                      // Converter can only operate when over this deposit. Should be the resource value of a surface deposit.
    powerfailResource = LVEnergy                  // If this resource runs out, the part will powerfail. Must be an INPUT_RESOURCE. Do not include to disable powerfails.
    powerfailResult = EXPLODE,10                  // The result if a powerfail occurs. Can be "EXPLODE,n", "MAINT", or "STOP". Requires powerfailResource to be set and valid.
                                                  // EXPLODE will explode the part with power n, MAINT will require an Engineer kerbal to come fix it, and STOP will just shut down the part.
    manualOperation = true                        // false by default; enables manual cycle mode
    manualRequiresStartup = false                 // true by default; if false, no Start/Stop, just Execute Cycle
    startStopShowRules = EVA+PAW                  // "PAW" default; controls Start/Stop button visibility
    manualShowRules = EVA                         // "PAW" default; controls Execute Cycle button visibility
    maxInteractionDistance = 5.0                  // 10.0 default; applies to all EVA-visible buttons
    recipeGroup = myGroup                         // null by default; enforces one-active-at-a-time per group. If null, the converter does not have a group.

    INPUT_RESOURCE
    {
        ResourceName = LVEnergy
        Ratio = 2
        FlowMode = STAGE_PRIORITY_FLOW
    }
    OUTPUT_RESOURCE
    {
        ResourceName = EarthAir
        Ratio = 1
        DumpExcess = false
    }

    chargingRequired = true    // Does the converter need to be charged to be used
	chargeRate = 50.0          // Percent per second to fill charge (50 = 2 seconds to full). Not required if charging is disabled
	chargeDecayRate = 5.0      // Percent per second to lose charge when storage can no longer charge. Not required if charging is disabled

	CHARGE_CON_NAMES           // Resources used for charge consumption. Not required if charging is disabled
	{
		name = ElectricCharge
	}
	CHARGE_CON_AMOUNTS         // Amount of each resource used for charge consumption (per second). Not required if charging is disabled
	{
		amount = 5.0
	}
}
*/

namespace Khemistry
{
    // Shared data
    public class KhemistryDeposit
    {
        // Shared variables
        public Vector2 position { get; set; }  // In latitude, longitude format
        public float depth { get; set; }  // In meters
        public string resource { get; set; }  // Internal name of a resource
        public string planet { get; set; }  // Planet the resource is on
        public float radius { get; set; }  // Radius of the deposit in meters

        // Deposit distance logic
        public float distanceFromDeposit(float lat, float lon)
        {
            return (float)KShared.LatLonDistanceMeters(position[0], position[1], lat, lon, planet);
        }
        public bool isInsideDeposit(float lat, float lon) => distanceFromDeposit(lat, lon) <= radius;
    }
    // Stores underground deposit data
    public class KhemistryUDeposit : KhemistryDeposit
    {
        public float depthStart { get; set; }  // In meters

        // Helper function to see if inside the deposit based on depth, depth is in meters
        public bool isDepthInsideDeposit(float depth2) => depth2 > depthStart && depth2 < depthStart + depth;

        public KhemistryUDeposit(KShared kinst, string planet, string requiredBiome, float depthStart, float depth, string resource, float minRadius, float maxRadius, float latOverride = -12345, float lonOverride = -12345)
        {
            // Set values to make sure everything works
            this.planet = planet;
            this.depthStart = depthStart;
            this.depth = depth;
            this.resource = resource;

            if (minRadius == maxRadius)
            {
                radius = minRadius;
            }
            else
            {
                float tmp = -1.0f;
                while (!(minRadius > tmp))
                    tmp = (float)(kinst.rand.NextDouble() * maxRadius);
                radius = tmp;
            }

            // Generate position
            if ((int)latOverride == -12345 || (int)lonOverride == -12345)  // If either of them are not set, calculate as normal
            {
                position = new Vector2((float)(kinst.rand.NextDouble() * 180) - 90, (float)(kinst.rand.NextDouble() * 360) - 180);
                if (requiredBiome != null)  // If it is null, any biome is supported
                {
                    // Just keep randomizing the deposit until it hits the right biome
                    while (KShared.getBiomeNameFromLatLon(planet, position) != requiredBiome)
                        position = new Vector2((float)(kinst.rand.NextDouble() * 180) - 90, (float)(kinst.rand.NextDouble() * 360) - 180);
                }
            }
            else  // If both are set, ignore requiredBiome and override the position
            {
                position = new Vector2(latOverride, lonOverride);
            }
        }
    }
    // Stores surface deposit data
    // Because a surface deposit extends into the ground, there is an underground deposit as well
    public class KhemistryGDeposit : KhemistryDeposit
    {
        public KhemistryUDeposit pairGDeposit { get; set; }  // Underground deposit right under the surface one

        // Helper function to see if inside the deposit based on depth, depth is in meters
        // Using -1 to make sure 0 works
        public bool isDepthInsideDeposit(float depth2) => depth2 > -1 && depth2 < depth;

        public KhemistryGDeposit(KShared kinst, string planet, string requiredBiome, float depth, string resource, float minRadius, float maxRadius, string resource2, float underDepth)
        {
            // Set values to make sure everything works
            this.planet = planet;
            this.depth = depth;
            this.resource = resource;

            // if it works, it works
            float tmp = -1.0f;
            while (!(minRadius > tmp))
                tmp = (float)(kinst.rand.NextDouble() * maxRadius);
            radius = tmp;

            // Generate position
            position = new Vector2((float)(kinst.rand.NextDouble() * 180) - 90, (float)(kinst.rand.NextDouble() * 360) - 180);
            if (requiredBiome != null)  // If it is null, any biome is supported
            {
                // Just keep randomizing the deposit until it hits the right biome
                while (KShared.getBiomeNameFromLatLon(planet, position) != requiredBiome)
                    position = new Vector2((float)(kinst.rand.NextDouble() * 180) - 90, (float)(kinst.rand.NextDouble() * 360) - 180);
            }

            // Create the underground pair of the surface deposit, giving it the counterpart resource and overriding the position to the surface deposit's position
            // The biome is not passed here because the override will ignore it anyway
            // If resource2 is null, the deposit is considered "surfaceOnly" and the underground deposit won't be created
            if (resource2 != null)
                pairGDeposit = new KhemistryUDeposit(kinst, planet, null, depth, underDepth, resource2, position[0], position[1]);
        }
    }

    // Recipes
    public class KhemistryRecipe
    {
        // ── Basic converter info ───────────────────────────────────────────────────

        [KSPField(isPersistant = false)] public string ConverterName = "Converter";
        [KSPField(isPersistant = false)] public string StartActionName = "Start Converter";
        [KSPField(isPersistant = false)] public string StopActionName = "Stop Converter";

        // ── Internal data structures ───────────────────────────────────────────────

        public struct ResourceInput
        {
            public string resourceName;
            public double ratio;
            public ResourceFlowMode flowMode;
        }

        public struct ResourceOutput
        {
            public string resourceName;
            public double ratio;
            public bool dumpExcess;
        }

        public enum SituationCondition
        {
            Any, Landed, Splashed, FlyingLow, FlyingHigh, SpaceLow, SpaceHigh, SubOrbital
        }

        public enum PowerfailResult { None, Stop, Explode, Maint }

        public List<ResourceInput> _inputs = new List<ResourceInput>();
        public List<ResourceOutput> _outputs = new List<ResourceOutput>();

        // Conditions
        public string _planetCondition = null;
        public string _biomeCondition = null;
        public double _altMin = double.MinValue;
        public double _altMax = double.MaxValue;
        public SituationCondition _situationCondition = SituationCondition.Any;
        public string _depositCondition = null;

        // Powerfail
        public string _powerfailResource = null;
        public PowerfailResult _powerfailResult = PowerfailResult.None;
        public float _powerfailExplosionPower = 0f;

        // Manual operation config
        public bool _manualOperation = false;
        public bool _manualRequiresStartup = true;

        // Show rules
        public bool _startStopShowPAW = true;
        public bool _startStopShowEVA = false;
        public bool _manualShowPAW = true;
        public bool _manualShowEVA = false;

        // Interaction distance
        public float _maxInteractionDistance = 10f;

        // Recipe group (currently unused by KhemistryAdvancedRecipeISRU — kept for future GUI use)
        public string _recipeGroup = null;

        // Charging (only meaningful if a KhemistryAdvancedRecipeISRU does not override these)
        public bool chargingRequired = false;
        public float chargeRate = 0f;
        public float chargeDecayRate = 0f;
        public readonly List<string> ChargeNames = new List<string>();
        public readonly List<float> ChargeAmounts = new List<float>();

        public KhemistryRecipe(ConfigNode node)
        {
            ConverterName = KShared.getStrValueFromCFG(node, "ConverterName", "Converter");
            StartActionName = KShared.getStrValueFromCFG(node, "StartActionName", null);
            StopActionName = KShared.getStrValueFromCFG(node, "StopActionName", null);
            _planetCondition = KShared.getStrValueFromCFG(node, "planetCondition", null);
            _biomeCondition = KShared.getStrValueFromCFG(node, "biomeCondition", null);
            _altMin = KShared.getFloatValueFromCFG(node, "altitudeMinCondition", (float)double.MinValue);
            _altMax = KShared.getFloatValueFromCFG(node, "altitudeMaxCondition", (float)double.MaxValue);

            // situationCondition
            _situationCondition = SituationCondition.Any;
            string sitStr = KShared.getStrValueFromCFG(node, "situationCondition", null);
            if (sitStr != null)
            {
                if (sitStr.Equals("FlyindHigh", StringComparison.OrdinalIgnoreCase))
                    sitStr = "FlyingHigh";
                SituationCondition parsed;
                if (Enum.TryParse(sitStr, true, out parsed))
                    _situationCondition = parsed;
                else
                    KShared.Instance?.LogError("Unknown situationCondition \"" + sitStr + "\" — condition ignored.", "KhemistryRecipe/constructor");
            }

            // depositCondition
            _depositCondition = KShared.getStrValueFromCFG(node, "depositCondition", null);

            // manualOperation / manualRequiresStartup
            _manualOperation = false;
            _manualRequiresStartup = true;
            bool tmpB;
            if (bool.TryParse(KShared.getStrValueFromCFG(node, "manualOperation", "false"), out tmpB))
                _manualOperation = tmpB;
            if (bool.TryParse(KShared.getStrValueFromCFG(node, "manualRequiresStartup", "true"), out tmpB))
                _manualRequiresStartup = tmpB;

            // startStopShowRules / manualShowRules
            KShared.ParseShowRule(
                KShared.getStrValueFromCFG(node, "startStopShowRules", "PAW"),
                out _startStopShowPAW, out _startStopShowEVA, "startStopShowRules");

            KShared.ParseShowRule(
                KShared.getStrValueFromCFG(node, "manualShowRules", "PAW"),
                out _manualShowPAW, out _manualShowEVA, "manualShowRules");

            // maxInteractionDistance
            _maxInteractionDistance = 10f;
            float distTmp;
            if (float.TryParse(node.GetValue("maxInteractionDistance"), out distTmp))
                _maxInteractionDistance = distTmp;

            // recipeGroup
            _recipeGroup = KShared.getStrValueFromCFG(node, "recipeGroup", null);

            // INPUT_RESOURCE nodes
            foreach (ConfigNode inputNode in node.GetNodes("INPUT_RESOURCE"))
            {
                string resName = inputNode.GetValue("ResourceName");
                if (string.IsNullOrEmpty(resName)) continue;

                double ratio = 0.0;
                double.TryParse(inputNode.GetValue("Ratio"), out ratio);

                ResourceFlowMode flowMode = ResourceFlowMode.ALL_VESSEL;
                string flowStr = inputNode.GetValue("FlowMode");
                if (!string.IsNullOrEmpty(flowStr))
                {
                    ResourceFlowMode parsed;
                    if (Enum.TryParse(flowStr.Trim(), true, out parsed))
                        flowMode = parsed;
                    else
                        KShared.Instance?.LogError(
                            "Recipe \"" + ConverterName + "\": Unknown FlowMode \"" + flowStr + "\" for " + resName + ", defaulting to ALL_VESSEL.",
                            "KhemistryRecipe/constructor");
                }

                _inputs.Add(new ResourceInput { resourceName = resName, ratio = ratio, flowMode = flowMode });
            }

            // OUTPUT_RESOURCE nodes
            foreach (ConfigNode outputNode in node.GetNodes("OUTPUT_RESOURCE"))
            {
                string resName = outputNode.GetValue("ResourceName");
                if (string.IsNullOrEmpty(resName)) continue;

                double ratio = 0.0;
                double.TryParse(outputNode.GetValue("Ratio"), out ratio);

                bool dumpExcess = false;
                bool.TryParse(outputNode.GetValue("DumpExcess"), out dumpExcess);

                _outputs.Add(new ResourceOutput { resourceName = resName, ratio = ratio, dumpExcess = dumpExcess });
            }

            if (_inputs.Count == 0 && _outputs.Count == 0)
                KShared.Instance?.LogError(
                    "Recipe \"" + ConverterName + "\" has no INPUT_RESOURCE or OUTPUT_RESOURCE nodes — it will do nothing.",
                    "KhemistryRecipe/constructor");

            // powerfailResource / powerfailResult
            _powerfailResource = null;
            _powerfailResult = PowerfailResult.None;
            _powerfailExplosionPower = 0f;

            string pfRes = KShared.getStrValueFromCFG(node, "powerfailResource", null);
            string pfResultRaw = KShared.getStrValueFromCFG(node, "powerfailResult", null);

            if (pfRes != null)
            {
                bool found = false;
                foreach (ResourceInput inp in _inputs)
                    if (inp.resourceName.Equals(pfRes, StringComparison.OrdinalIgnoreCase)) { found = true; break; }

                if (!found)
                {
                    KShared.Instance?.LogError("powerfailResource \"" + pfRes + "\" is not a defined INPUT_RESOURCE — powerfail disabled.", "KhemistryRecipe/constructor");
                }
                else
                {
                    _powerfailResource = pfRes;
                    if (pfResultRaw != null)
                    {
                        string pfResult = pfResultRaw.Trim().Trim('"').ToUpper();
                        if (pfResult == "STOP")
                        {
                            _powerfailResult = PowerfailResult.Stop;
                        }
                        else if (pfResult == "MAINT")
                        {
                            _powerfailResult = PowerfailResult.Maint;
                        }
                        else if (pfResult.StartsWith("EXPLODE,"))
                        {
                            float power;
                            if (float.TryParse(pfResult.Substring(8), out power))
                            {
                                _powerfailResult = PowerfailResult.Explode;
                                _powerfailExplosionPower = power;
                            }
                            else
                            {
                                KShared.Instance?.LogError("Could not parse EXPLODE power \"" + pfResultRaw + "\" — defaulting to STOP.", "KhemistryRecipe/constructor");
                                _powerfailResult = PowerfailResult.Stop;
                            }
                        }
                        else
                        {
                            KShared.Instance?.LogError("Unknown powerfailResult \"" + pfResultRaw + "\" — defaulting to STOP.", "KhemistryRecipe/constructor");
                            _powerfailResult = PowerfailResult.Stop;
                        }
                    }
                }
            }
            else if (pfResultRaw != null)
            {
                KShared.Instance?.LogError("powerfailResult set without powerfailResource — powerfailResult ignored.", "KhemistryRecipe/constructor");
            }

            // Charging (usually overridden wholesale by the owning KhemistryAdvancedRecipeISRU,
            // but a recipe may define its own charge resource needs which get merged in)
            if (bool.TryParse(KShared.getStrValueFromCFG(node, "chargingRequired", "false"), out tmpB))
                chargingRequired = tmpB;

            float chgTmp;
            if (float.TryParse(node.GetValue("chargeRate"), out chgTmp)) chargeRate = chgTmp;
            if (float.TryParse(node.GetValue("chargeDecayRate"), out chgTmp)) chargeDecayRate = chgTmp;

            if (node.HasNode("CHARGE_CON_NAMES"))
                foreach (string n in node.GetNode("CHARGE_CON_NAMES").GetValues("name"))
                    ChargeNames.Add(n.Trim());
            if (node.HasNode("CHARGE_CON_AMOUNTS"))
                foreach (string a in node.GetNode("CHARGE_CON_AMOUNTS").GetValues("amount"))
                { float amtTmp; if (float.TryParse(a, out amtTmp)) ChargeAmounts.Add(amtTmp); }
            if (ChargeNames.Count != ChargeAmounts.Count)
                KShared.Instance?.LogError(
                    "Recipe \"" + ConverterName + "\": CHARGE_CON_NAMES and CHARGE_CON_AMOUNTS length mismatch.",
                    "KhemistryRecipe/constructor");
        }
    }

    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KSharedMainMenu : MonoBehaviour
    {
        private static KSharedMainMenu _instance;
        public static KSharedMainMenu Instance => _instance;

        public KShared kinst;

        public void Awake()
        {
            // Get the KShared instance
            kinst = KShared.Instance;
            if (kinst == null)
            {
                Debug.Log("Khemistry (KSharedMainMenu/Awake): No KShared.Instance and Khemistry is about to have a bad time");
            }

            // Get instance of itself and check for duplicates
            if (_instance != null)
            {
                kinst.LogError("Another instance of KSharedMainMenu was found, self destructing...", "KSharedMainMenu/Awake");
                Destroy(gameObject);
                return;
            }
            _instance = this;

            // Remotely populate the CBody list with biomes once in the main menu
            kinst.celestialBodies = FlightGlobals.Bodies.Select(b => b.bodyName).ToList();

            // Fetch all resource deposits
            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("KHEMISTRY_RESOURCE_DEPOSIT");

            // Load all resource deposits
            foreach (ConfigNode node in nodes)
            {
                // Fatal error checking
                if (!node.HasValue("resource"))  // no resource, fatal error
                {
                    kinst.LogError("A KHEMISTRY_RESOURCE_DEPOSIT does not define a resource it contains and was not loaded.", "KSharedMainMenu/Awake");
                    continue;
                }
                if (!node.HasValue("type"))  // no type, fatal error
                {
                    kinst.LogError("A KHEMISTRY_RESOURCE_DEPOSIT with resource \"" + node.GetValue("resource") + "\" does not have a type and was not loaded.", "KSharedMainMenu/Awake");
                    continue;
                }
                if (!node.HasValue("body"))  // no body, fatal error
                {
                    kinst.LogError("A KHEMISTRY_RESOURCE_DEPOSIT with resource \"" + node.GetValue("resource") + "\" does not define a body to be placed on and was not loaded.", "KSharedMainMenu/Awake");
                    continue;
                }
                if (node.GetValue("type") == "surface" && !node.HasValue("resource2"))  // no underground resource for a surface+underground deposit, fatal error
                {
                    kinst.LogError("A KHEMISTRY_RESOURCE_DEPOSIT with resource \"" + node.GetValue("resource") + "\" is a surface type deposit without a resource2 value. It was not loaded.", "KSharedMainMenu/Awake");
                    continue;
                }

                // Rendering logic (soon)
                if (node.GetValue("type") != "underground" && node.GetValue("render") == "true")
                {
                    kinst.LogError("A KHEMISTRY_RESOURCE_DEPOSIT with resource \"" + node.GetValue("resource") + "\" attempts to render but that is not implemented yet.", "KSharedMainMenu/Awake");
                    continue;
                }

                // Load values shared between all deposits
                int maxAmount = KShared.getIntValueFromCFG(node, "maxAmount", 10) + 1;  // +1 accounts for non-inclusive behavior of rand.Next
                int minAmount = KShared.getIntValueFromCFG(node, "minAmount", 5);
                int maxRadius = KShared.getIntValueFromCFG(node, "maxRadius", 20) + 1;  // +1 accounts for non-inclusive behavior of rand.Next
                int minRadius = KShared.getIntValueFromCFG(node, "minRadius", 10);
                string body = node.GetValue("body");
                string resource = node.GetValue("resource");
                string biome = KShared.getStrValueFromCFG(node, "biome", null);
                float depthUnderground = KShared.getFloatValueFromCFG(node, "depthUnderground", 50);

                // Load deposits
                if (node.GetValue("type") == "surface")  // Creates a deposit with both a surface and an underground deposit
                {
                    for (int i = 0; i < kinst.rand.Next(minAmount, maxAmount); i++)
                        kinst.surfaceDeposits.Add(new KhemistryGDeposit(kinst, body, biome, KShared.getFloatValueFromCFG(node, "depthSurface", 10), resource, minRadius, maxRadius, node.GetValue("resource2"), KShared.getFloatValueFromCFG(node, "depthUndergroundStart", 100)));
                }
                else if (node.GetValue("type") == "surfaceOnly")  // Creates a deposit exculsively on the surface
                {
                    for (int i = 0; i < kinst.rand.Next(minAmount, maxAmount); i++)
                        kinst.surfaceDeposits.Add(new KhemistryGDeposit(kinst, body, biome, KShared.getFloatValueFromCFG(node, "depthSurface", 10), resource, minRadius, maxRadius, null, 0));  // null makes it a surfaceOnly deposit, passing 0 as resource2 will be ignored in this case
                }
                else if (node.GetValue("type") == "underground")  // Creates an underground deposit
                {
                    for (int i = 0; i < kinst.rand.Next(minAmount, maxAmount); i++)
                        kinst.undergroundDeposits.Add(new KhemistryUDeposit(kinst, body, biome, KShared.getFloatValueFromCFG(node, "depthUndergroundStart", 100), depthUnderground, resource, minRadius, maxRadius));
                }
                else  // invalid type, fatal error
                {
                    kinst.LogError("A KHEMISTRY_RESOURCE_DEPOSIT with resource \"" + node.GetValue("resource") + "\" does not have a valid type and was not loaded. The type was \"" + node.GetValue("type") + "\".", "KSharedMainMenu/Awake");
                }
            }

            // Log what was created
            kinst.Log("Created " + kinst.undergroundDeposits.Count().ToString() + " underground deposits.", "KSharedMainMenu/Awake");
            kinst.Log("Created " + kinst.surfaceDeposits.Count().ToString() + " surface deposits.", "KSharedMainMenu/Awake");

            // Load all recipes
            ConfigNode[] nodes2 = GameDatabase.Instance.GetConfigNodes("KHEMISTRY_RECIPE");
            foreach (ConfigNode node in nodes2)
            {
                // Create recipe type
                if (!node.HasValue("recipeType"))
                {
                    kinst.LogError("A KHEMISTRY_RECIPE has no recipeType!", "KSharedMainMenu/Awake");
                    continue;
                }
                string recipeT = node.GetValue("recipeType");
                if (!kinst.recipeDict.ContainsKey(recipeT))
                    kinst.recipeDict.Add(recipeT, new List<KhemistryRecipe>());

                // Add recipe object
                kinst.recipeDict[recipeT].Add(new KhemistryRecipe(node));
            }

            kinst.Log("Created " + kinst.recipeDict.Keys.Count().ToString() + " recipe types.", "KSharedMainMenu/Awake");
            foreach (string recipeType in kinst.recipeDict.Keys)
            {
                kinst.Log("Created " + kinst.recipeDict[recipeType].Count().ToString() + " recipes for recipe type " + recipeType, "KSharedMainMenu/Awake");
            }
        }
    }

    // Shared class for GUI things and deposits
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class KShared : MonoBehaviour
    {
        private static KShared _instance;
        public static KShared Instance => _instance;

        // values idk
        private bool _selectorVisible = false;
        private Vector2 _selectorScroll = Vector2.zero;
        private string _selectorTitle = "";
        private List<string> _selectorOptions;
        private Action<string> _selectorCallback;
        private Rect _windowRect = new Rect(0, 0, 320, 300);
        private int _windowId;

        private bool _amountVisible = false;
        private string _amountTitle = "";
        private float _amountValue = 0f;
        private float _amountMin = 0f;
        private float _amountMax = 1f;
        private Action<float> _amountCallback;
        private Rect _amountRect = new Rect(0, 0, 320, 130);
        private int _amountWindowId;

        // Deposit lists
        public List<KhemistryUDeposit> undergroundDeposits = new List<KhemistryUDeposit>();
        public List<KhemistryGDeposit> surfaceDeposits = new List<KhemistryGDeposit>();

        // Recipe dictionary
        public Dictionary<string, List<KhemistryRecipe>> recipeDict = new Dictionary<string, List<KhemistryRecipe>>();

        // Shared random class
        public System.Random rand = new System.Random();
        // Shared celestial body list, remotely populated from KSharedMainMenu when the game ends up at the main menu
        public List<string> celestialBodies = new List<string>();

        // Helper function to get a biome name from position on a body
        public static string getBiomeNameFromLatLon(string planet, Vector2 pos) => FlightGlobals.GetBodyByName(planet).BiomeMap.GetAtt(pos[0], pos[1]).name;

        // Helper functions to get values from configs with default values
        public static int getIntValueFromCFG(ConfigNode node, string value, int defaultValue)
        {
            if (node.HasValue(value))
                try { return int.Parse(node.GetValue(value)); } catch (Exception) { }
            return defaultValue;
        }
        public static float getFloatValueFromCFG(ConfigNode node, string value, float defaultValue)
        {
            if (node.HasValue(value))
                try { return float.Parse(node.GetValue(value)); } catch (Exception) { }
            return defaultValue;
        }
        public static string getStrValueFromCFG(ConfigNode node, string value, string defaultValue) => node.HasValue(value) ? node.GetValue(value) : defaultValue;

        // Planet distance calculator
        public static double LatLonDistanceMeters(
            double lat1Deg,
            double lon1Deg,
            double lat2Deg,
            double lon2Deg,
            string body)
        {
            // Convert degrees to radians
            double lat1 = DegreesToRadians(lat1Deg);
            double lon1 = DegreesToRadians(lon1Deg);
            double lat2 = DegreesToRadians(lat2Deg);
            double lon2 = DegreesToRadians(lon2Deg);

            double dLat = lat2 - lat1;
            double dLon = lon2 - lon1;

            double a =
                Math.Pow(Math.Sin(dLat / 2), 2) +
                Math.Cos(lat1) * Math.Cos(lat2) *
                Math.Pow(Math.Sin(dLon / 2), 2);

            double c = 2 * Math.Atan2(
                Math.Sqrt(a),
                Math.Sqrt(1 - a));

            return FlightGlobals.GetBodyByName(body).Radius * c;
        }

        public static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        // Helper functions to get all deposits at a point, depth is in meters
        public List<string> surfaceDepositsAtPoint(float lat, float lon, string body, float depth)
        {
            List<string> tmp = new List<string>();
            foreach (KhemistryGDeposit deposit in surfaceDeposits)
            {
                if (body == deposit.planet && deposit.isInsideDeposit(lat, lon) && deposit.isDepthInsideDeposit(depth))
                {
                    tmp.Add(deposit.resource);
                }
            }
            return tmp;
        }
        public List<string> undergroundDepositsAtPoint(float lat, float lon, string body, float depth)
        {
            List<string> tmp = new List<string>();
            foreach (KhemistryUDeposit deposit in undergroundDeposits)
            {
                if (body == deposit.planet && deposit.isInsideDeposit(lat, lon) && deposit.isDepthInsideDeposit(depth))
                {
                    tmp.Add(deposit.resource);
                }
            }
            return tmp;
        }

        // Show rules for converters and recipe
        public static void ParseShowRule(string raw, out bool showPAW, out bool showEVA,
            string fieldName, string moduleName = null)
        {
            string val = raw.Trim().Trim('"').ToUpper();
            switch (val)
            {
                case "PAW":
                    showPAW = true; showEVA = false; break;
                case "EVA":
                    showPAW = false; showEVA = true; break;
                case "EVA+PAW":
                case "PAW+EVA":
                    showPAW = true; showEVA = true; break;
                default:
                    if (moduleName == null)
                        Instance?.LogError("Unknown " + fieldName + " value \"" + raw + "\" — defaulting to PAW.", "KShared/ParseShowRule");
                    else
                        Instance?.LogError("Converter \"" + moduleName + "\": Unknown " + fieldName + " value \"" + raw + "\" — defaulting to PAW.", "KShared/ParseShowRule");
                    showPAW = true; showEVA = false; break;
            }
        }

        // Main code
        public void Awake()
        {
            // Initialization
            if (_instance != null)
            {
                LogError("Another instance of KShared was found, self destructing...", "KShared/Awake");
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            _windowId = GUIUtility.GetControlID(FocusType.Passive);
            _amountWindowId = GUIUtility.GetControlID(FocusType.Passive);
            Log("KShared initialized.", "KShared/Awake");
        }

        public void ShowSelector(string title, List<string> options, Action<string> onSelect)
        {
            _selectorTitle = title;
            _selectorOptions = options;
            _selectorCallback = onSelect;
            _selectorScroll = Vector2.zero;
            _windowRect = new Rect(
                (Screen.width - _windowRect.width) / 2f,
                (Screen.height - _windowRect.height) / 2f,
                _windowRect.width,
                _windowRect.height
            );
            _selectorVisible = true;
        }

        private void OnGUI()
        {
            if (_selectorVisible)
                _windowRect = GUILayout.Window(
                    _windowId,
                    _windowRect,
                    DrawSelectorWindow,
                    _selectorTitle,
                    HighLogic.Skin.window);

            if (_amountVisible)
                _amountRect = GUILayout.Window(
                    _amountWindowId,
                    _amountRect,
                    DrawAmountWindow,
                    _amountTitle,
                    HighLogic.Skin.window);
        }

        public void ShowAmountSelector(string title, float min, float max, float initial, Action<float> onConfirm)
        {
            _amountTitle = title;
            _amountMin = min;
            _amountMax = max;
            _amountValue = Mathf.Clamp(initial, min, max);
            _amountCallback = onConfirm;
            _amountRect = new Rect(
                (Screen.width - _amountRect.width) / 2f,
                (Screen.height - _amountRect.height) / 2f,
                _amountRect.width, _amountRect.height);
            _amountVisible = true;
        }

        private void DrawAmountWindow(int windowId)
        {
            GUILayout.Label(
                string.Format("{0:F3}  /  {1:F3}", _amountValue, _amountMax),
                HighLogic.Skin.label);
            _amountValue = GUILayout.HorizontalSlider(
                _amountValue, _amountMin, _amountMax,
                HighLogic.Skin.horizontalSlider,
                HighLogic.Skin.horizontalSliderThumb);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Confirm", HighLogic.Skin.button))
            {
                _amountVisible = false;
                _amountCallback(_amountValue);
            }
            if (GUILayout.Button("Cancel", HighLogic.Skin.button))
                _amountVisible = false;
            GUILayout.EndHorizontal();
            GUI.DragWindow();
        }

        private void DrawSelectorWindow(int windowId)
        {
            _selectorScroll = GUILayout.BeginScrollView(
                _selectorScroll,
                HighLogic.Skin.scrollView,
                GUILayout.Height(220f)
            );
            foreach (string option in _selectorOptions)
            {
                if (GUILayout.Button(option, HighLogic.Skin.button))
                {
                    _selectorVisible = false;
                    _selectorCallback(option);
                }
            }
            GUILayout.EndScrollView();

            if (GUILayout.Button("Cancel", HighLogic.Skin.button))
                _selectorVisible = false;

            GUI.DragWindow();
        }

        public void Log(string message, string func = null)
        {
            if (func != null)
                Debug.Log("Khemistry (" + func + "): " + message);
            else
                Debug.Log("Khemistry: " + message);
        }

        public void LogError(string message, string func = null)
        {
            if (func != null)
                Debug.LogError("Khemistry (" + func + "): " + message);
            else
                Debug.LogError("Khemistry: " + message);
        }
    }

    public class KhemistryFluidCell : PartModule
    {
        [KSPField(isPersistant = false)]
        public float ResourceMaxAmount = 100.0f;

        [KSPField(isPersistant = false)]
        public float TransferDistance = 10.0f;

        [KSPField(isPersistant = true)]
        public float ResourceAmount = 0.0f;
        [KSPField(isPersistant = true)]
        public string ResourceName = "";

        public HashSet<string> AllowedResources = new HashSet<string>();

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Contents")]
        public string ContentsDisplay = "Empty";

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            AllowedResources.Clear();
            if (node.HasNode("ALLOWED_RESOURCES"))
            {
                foreach (string name in node.GetNode("ALLOWED_RESOURCES").GetValues("name"))
                    AllowedResources.Add(name.Trim());
                KShared.Instance?.Log(
                    "Loaded " + AllowedResources.Count + " allowed resources.",
                    "KhemistryFluidCell/OnLoad");
            }
            else
            {
                KShared.Instance?.LogError(
                    "Part \"" + part.name + "\" has KhemistryFluidCell but no ALLOWED_RESOURCES node. This part is now capable of storing anything.",
                    "KhemistryFluidCell/OnLoad");
            }
        }

        public override void OnUpdate()
        {
            ContentsDisplay = string.IsNullOrEmpty(ResourceName)
                ? "Empty"
                : string.Format("{0}: {1:F2} / {2:F2} kg", ResourceName, ResourceAmount, ResourceMaxAmount);
        }
    }

    public class KhemistryDegradingBattery : PartModule
    {
        [KSPField(isPersistant = false)]
        public string ResourceName = "ElectricCharge";

        [KSPField(isPersistant = false)]
        public double DegradeTime = -1.0;

        [KSPField(isPersistant = true)]
        public double OriginalMaxAmount = -1.0;

        [KSPField(isPersistant = true)]
        public double StartTime = -1.0;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Battery Health",
         groupName = "batterydeg", groupDisplayName = "Battery Health", groupStartCollapsed = false)]
        public string HealthDisplay = "Battery Life: 100%";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Time Remaining",
         groupName = "batterydeg")]
        public string HealthTimeDisplay = "Time until 0% battery life: Battery cannot degrade.";

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            PartResource resource = part.Resources.Get(ResourceName);
            if (resource == null)
            {
                KShared.Instance?.LogError(
                    "Part \"" + part.name + "\" has KhemistryDegradingBattery but no resource node for " + ResourceName,
                    "KhemistryDegradingBattery/OnStart");
                return;
            }

            if (OriginalMaxAmount < 0) OriginalMaxAmount = resource.maxAmount;
            if (StartTime < 0) StartTime = Planetarium.GetUniversalTime();

            ApplyDegradation(resource);
        }

        public override void OnUpdate()
        {
            PartResource resource = part.Resources.Get(ResourceName);
            if (resource == null) return;
            ApplyDegradation(resource);
        }

        private void ApplyDegradation(PartResource resource)
        {
            if (DegradeTime <= 0) return;

            double elapsedSeconds = Planetarium.GetUniversalTime() - StartTime;
            double degradeSeconds = DegradeTime * 60.0;
            double fraction = Math.Max(0.0, 1.0 - (elapsedSeconds / degradeSeconds));
            double newMax = OriginalMaxAmount * fraction;

            resource.maxAmount = newMax;
            if (resource.amount > resource.maxAmount)
                resource.amount = resource.maxAmount;

            HealthDisplay = string.Format("Battery Life: {0:F1}%", fraction * 100.0);
            double remaining = Math.Max(0, degradeSeconds - elapsedSeconds);
            HealthTimeDisplay = string.Format("Time until 0%: {0:F0}s", remaining);
        }
    }

    public class KhemistryResourceInfo
    {
        public string name;
        public string displayName;
        public string abbreviation;
        public float unitCost;
        public float density;
        public float volume;
        public string flowMode;
        public string transfer;
        public bool isTweakable;
        public bool isVisible;
        public string description;
    }

    public class KhemistryRecipeIO
    {
        public string resourceName;
        public double ratio;
    }

    public class KhemistryRecipeInfo
    {
        public string converterName;
        public bool generatesHeat;
        public string partTitle;
        public List<KhemistryRecipeIO> inputs = new List<KhemistryRecipeIO>();
        public List<KhemistryRecipeIO> outputs = new List<KhemistryRecipeIO>();
    }

    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KhemistryLibraryLoader : MonoBehaviour
    {
        public static List<KhemistryResourceInfo> Resources { get; private set; }
        public static List<KhemistryRecipeInfo> Recipes { get; private set; }
        public static bool IsLoaded { get; private set; } = false;

        public void Awake()
        {
            DontDestroyOnLoad(gameObject);
            LoadData();
        }

        private void LoadData()
        {
            KShared.Instance?.Log("Loading resource and recipe library...", "KhemistryLibraryLoader/LoadData");

            var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("RESOURCE_DEFINITION"))
            {
                string resName = node.GetValue("name");
                string desc = node.GetValue("khemistryDescription");
                if (!string.IsNullOrEmpty(resName) && !string.IsNullOrEmpty(desc))
                    descriptions[resName] = desc;
            }

            Resources = new List<KhemistryResourceInfo>();
            foreach (PartResourceDefinition def in PartResourceLibrary.Instance.resourceDefinitions)
            {
                descriptions.TryGetValue(def.name, out string description);
                Resources.Add(new KhemistryResourceInfo
                {
                    name = def.name,
                    displayName = string.IsNullOrEmpty(def.displayName) ? def.name : def.displayName,
                    abbreviation = def.abbreviation,
                    unitCost = def.unitCost,
                    density = def.density,
                    volume = def.volume,
                    flowMode = def.resourceFlowMode.ToString(),
                    transfer = def.resourceTransferMode.ToString(),
                    isTweakable = def.isTweakable,
                    isVisible = def.isVisible,
                    description = description
                });
            }
            Resources.Sort((a, b) =>
                string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase));

            Recipes = new List<KhemistryRecipeInfo>();
            foreach (ConfigNode partNode in GameDatabase.Instance.GetConfigNodes("PART"))
            {
                string partTitle = partNode.GetValue("title") ?? partNode.GetValue("name") ?? "Unknown Part";
                foreach (ConfigNode moduleNode in partNode.GetNodes("MODULE"))
                {
                    if (moduleNode.GetValue("name") != "ModuleResourceConverter") continue;

                    var recipe = new KhemistryRecipeInfo
                    {
                        converterName = moduleNode.GetValue("ConverterName") ?? "Unnamed Converter",
                        generatesHeat = string.Equals(moduleNode.GetValue("GeneratesHeat"), "true",
                                            StringComparison.OrdinalIgnoreCase),
                        partTitle = partTitle
                    };

                    foreach (ConfigNode inputNode in moduleNode.GetNodes("INPUT_RESOURCE"))
                    {
                        string resName = inputNode.GetValue("ResourceName");
                        if (string.IsNullOrEmpty(resName)) continue;
                        double.TryParse(inputNode.GetValue("Ratio"), out double ratio);
                        recipe.inputs.Add(new KhemistryRecipeIO { resourceName = resName, ratio = ratio });
                    }
                    foreach (ConfigNode outputNode in moduleNode.GetNodes("OUTPUT_RESOURCE"))
                    {
                        string resName = outputNode.GetValue("ResourceName");
                        if (string.IsNullOrEmpty(resName)) continue;
                        double.TryParse(outputNode.GetValue("Ratio"), out double ratio);
                        recipe.outputs.Add(new KhemistryRecipeIO { resourceName = resName, ratio = ratio });
                    }

                    Recipes.Add(recipe);
                }
            }

            IsLoaded = true;
            KShared.Instance?.Log(
                string.Format("Library loaded: {0} resources, {1} recipes.", Resources.Count, Recipes.Count),
                "KhemistryLibraryLoader/LoadData");
        }
    }

    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KhemistryLibraryGUI : MonoBehaviour
    {
        private const int MainWindowId = 856201;
        private const int DetailWindowId = 856202;
        private const int RecipeWindowId = 856203;

        private bool _mainVisible = false;
        private bool _detailVisible = false;
        private bool _recipeVisible = false;

        private Rect _mainRect;
        private Rect _detailRect;
        private Rect _recipeRect;

        private string _searchText = "";
        private Vector2 _mainScroll = Vector2.zero;

        private KhemistryResourceInfo _selectedResource;
        private Vector2 _detailScroll = Vector2.zero;

        private List<KhemistryRecipeInfo> _filteredRecipes;
        private string _recipeTitle = "";
        private Vector2 _recipeScroll = Vector2.zero;

        private ApplicationLauncherButton _toolbarButton;
        private Texture2D _buttonTexture;

        private GUIStyle _wrapLabel;
        private GUIStyle _centeredLabel;
        private GUIStyle _boldLabel;
        private bool _stylesReady = false;

        public void Awake()
        {
            DontDestroyOnLoad(gameObject);

            float sw = Screen.width;
            float sh = Screen.height;
            float detailW = sw / 3f;
            _mainRect = new Rect(sw * 0.05f, sh * 0.1f, 700f, 500f);
            _detailRect = new Rect(sw * 0.63f, sh * 0.1f, detailW, 560f);
            _recipeRect = new Rect(sw * 0.05f, sh * 0.1f, 900f, 500f);

            _buttonTexture = new Texture2D(38, 38, TextureFormat.RGBA32, false);
            Color icon = new Color(0.25f, 0.60f, 0.90f, 1f);
            Color[] pixels = new Color[38 * 38];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = icon;
            _buttonTexture.SetPixels(pixels);
            _buttonTexture.Apply();

            GameEvents.onGUIApplicationLauncherReady.Add(OnLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnLauncherDestroyed);
        }

        public void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnLauncherDestroyed);
            if (_toolbarButton != null && ApplicationLauncher.Instance != null)
                ApplicationLauncher.Instance.RemoveModApplication(_toolbarButton);
        }

        private void OnLauncherReady()
        {
            if (_toolbarButton != null) return;
            _toolbarButton = ApplicationLauncher.Instance.AddModApplication(
                () => _mainVisible = true,
                () => _mainVisible = false,
                null, null, null, null,
                ApplicationLauncher.AppScenes.ALWAYS,
                _buttonTexture
            );
        }

        private void OnLauncherDestroyed() { _toolbarButton = null; }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _wrapLabel = new GUIStyle(HighLogic.Skin.label) { wordWrap = true };
            _centeredLabel = new GUIStyle(HighLogic.Skin.label) { wordWrap = true, alignment = TextAnchor.MiddleCenter };
            _boldLabel = new GUIStyle(HighLogic.Skin.label) { fontStyle = FontStyle.Bold, wordWrap = true };
            _stylesReady = true;
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (_mainVisible)
                _mainRect = GUILayout.Window(MainWindowId, _mainRect, DrawMainWindow, "Khemistry Resource Library", HighLogic.Skin.window);
            if (_detailVisible && _selectedResource != null)
                _detailRect = GUILayout.Window(DetailWindowId, _detailRect, DrawDetailWindow, "", HighLogic.Skin.window);
            if (_recipeVisible && _filteredRecipes != null)
                _recipeRect = GUILayout.Window(RecipeWindowId, _recipeRect, DrawRecipeWindow, _recipeTitle, HighLogic.Skin.window);
        }

        private void DrawMainWindow(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", HighLogic.Skin.button, GUILayout.Width(28)))
                _mainVisible = false;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", HighLogic.Skin.label, GUILayout.Width(55));
            _searchText = GUILayout.TextField(_searchText, HighLogic.Skin.textField);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _boldLabel, GUILayout.Width(230));
            GUILayout.Label("Abbreviation", _boldLabel, GUILayout.Width(120));
            GUILayout.Label("Cost per KG", _boldLabel, GUILayout.Width(100));
            GUILayout.EndHorizontal();

            _mainScroll = GUILayout.BeginScrollView(_mainScroll, HighLogic.Skin.scrollView);

            if (!KhemistryLibraryLoader.IsLoaded)
            {
                GUILayout.Label("Resources and recipes are still loading.", _wrapLabel);
            }
            else
            {
                string filter = _searchText.Trim().ToLower();
                foreach (KhemistryResourceInfo res in KhemistryLibraryLoader.Resources)
                {
                    if (!string.IsNullOrEmpty(filter) &&
                        !res.displayName.ToLower().Contains(filter) &&
                        !res.name.ToLower().Contains(filter))
                        continue;

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(res.displayName, HighLogic.Skin.button, GUILayout.Width(230)))
                        OpenDetailWindow(res);
                    GUILayout.Label(res.abbreviation ?? "-", HighLogic.Skin.label, GUILayout.Width(120));
                    GUILayout.Label(res.unitCost.ToString("F2"), HighLogic.Skin.label, GUILayout.Width(100));
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        private void OpenDetailWindow(KhemistryResourceInfo res)
        {
            _selectedResource = res;
            _detailVisible = true;
            _detailScroll = Vector2.zero;
        }

        private void DrawDetailWindow(int id)
        {
            KhemistryResourceInfo res = _selectedResource;
            float labelW = Screen.width / 3f - 60f;

            GUILayout.BeginHorizontal();
            GUILayout.Label(res.displayName, _boldLabel, GUILayout.Width(labelW - 35f));
            if (GUILayout.Button("X", HighLogic.Skin.button, GUILayout.Width(28)))
            {
                _detailVisible = false;
                _recipeVisible = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            string desc = string.IsNullOrEmpty(res.description) ? "No description available." : res.description;
            GUILayout.Label(desc, _centeredLabel, GUILayout.Width(labelW));

            GUILayout.Space(8);

            _detailScroll = GUILayout.BeginScrollView(_detailScroll, HighLogic.Skin.scrollView);

            DrawRow("Internal Name", res.name);
            DrawRow("Abbreviation", res.abbreviation ?? "-");
            DrawRow("Cost per KG", res.unitCost.ToString("F4"));
            DrawRow("Can be adjusted in VAB?", res.isTweakable ? "Yes" : "No");
            DrawRow("Hidden resource?", res.isVisible ? "No" : "Yes");
            DrawRow("Flow mode", res.flowMode ?? "-");
            DrawRow("Transfer method", res.transfer ?? "-");

            GUILayout.Space(6);

            string densityLine;
            if (Approx(res.density, 0.001f) && Approx(res.volume, 1f)) densityLine = "1 unit = 1 kilogram";
            else if (Approx(res.density, 1f) && Approx(res.volume, 1f)) densityLine = "1 unit = 1 ton";
            else if (Approx(res.density, 0.000001f) && Approx(res.volume, 1f)) densityLine = "1 unit = 1 gram";
            else densityLine = string.Format(
                    "This resource has special density and volume parameters. " +
                    "Every unit of this resource weighs {0:F6} kilograms and each internal " +
                    "volume unit is filled by {1} of this resource.",
                    res.density * 1000.0, res.volume);

            GUILayout.Label(densityLine, _wrapLabel, GUILayout.Width(labelW));
            GUILayout.EndScrollView();

            GUILayout.Space(6);

            if (GUILayout.Button("Recipes that use this resource", HighLogic.Skin.button))
                OpenRecipeWindow(res.name, isInput: true);
            if (GUILayout.Button("Recipes that produce this resource", HighLogic.Skin.button))
                OpenRecipeWindow(res.name, isInput: false);

            GUI.DragWindow();
        }

        private void DrawRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", _boldLabel, GUILayout.Width(180));
            GUILayout.Label(value, _wrapLabel);
            GUILayout.EndHorizontal();
        }

        private static bool Approx(float a, float b)
            => Math.Abs(a - b) < Math.Abs(b) * 0.01f + 1e-9f;

        private void OpenRecipeWindow(string resourceName, bool isInput)
        {
            _filteredRecipes = KhemistryLibraryLoader.Recipes.Where(r =>
                isInput
                    ? r.inputs.Any(i => i.resourceName == resourceName)
                    : r.outputs.Any(o => o.resourceName == resourceName)
            ).ToList();

            _recipeTitle = isInput
                ? "Recipes that use " + resourceName
                : "Recipes that produce " + resourceName;
            _recipeScroll = Vector2.zero;
            _recipeVisible = true;
        }

        private void DrawRecipeWindow(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", HighLogic.Skin.button, GUILayout.Width(28)))
                _recipeVisible = false;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _boldLabel, GUILayout.Width(200));
            GUILayout.Label("Produces heat?", _boldLabel, GUILayout.Width(100));
            GUILayout.Label("Inputs", _boldLabel, GUILayout.Width(270));
            GUILayout.Label("Outputs", _boldLabel, GUILayout.Width(270));
            GUILayout.EndHorizontal();

            _recipeScroll = GUILayout.BeginScrollView(_recipeScroll, HighLogic.Skin.scrollView);

            if (_filteredRecipes == null || _filteredRecipes.Count == 0)
            {
                GUILayout.Label("No recipes found.", _wrapLabel);
            }
            else
            {
                foreach (KhemistryRecipeInfo recipe in _filteredRecipes)
                {
                    GUILayout.BeginHorizontal();

                    GUILayout.BeginVertical(GUILayout.Width(200));
                    GUILayout.Label(recipe.converterName, _boldLabel);
                    GUILayout.Label("(" + recipe.partTitle + ")", _wrapLabel);
                    GUILayout.EndVertical();

                    GUILayout.Label(recipe.generatesHeat ? "Yes" : "No", HighLogic.Skin.label, GUILayout.Width(100));

                    GUILayout.BeginVertical(GUILayout.Width(270));
                    if (recipe.inputs.Count == 0) GUILayout.Label("-", _wrapLabel);
                    else foreach (KhemistryRecipeIO input in recipe.inputs)
                        {
                            string btnLabel = string.Format("{0:G4}x {1}/sec", input.ratio, input.resourceName);
                            KhemistryResourceInfo inputRes = FindResource(input.resourceName);
                            if (inputRes != null) { if (GUILayout.Button(btnLabel, HighLogic.Skin.button)) OpenDetailWindow(inputRes); }
                            else GUILayout.Label(btnLabel, _wrapLabel);
                        }
                    GUILayout.EndVertical();

                    GUILayout.BeginVertical(GUILayout.Width(270));
                    if (recipe.outputs.Count == 0) GUILayout.Label("-", _wrapLabel);
                    else foreach (KhemistryRecipeIO output in recipe.outputs)
                        {
                            string btnLabel = string.Format("{0:G4}x {1}/sec", output.ratio, output.resourceName);
                            KhemistryResourceInfo outputRes = FindResource(output.resourceName);
                            if (outputRes != null) { if (GUILayout.Button(btnLabel, HighLogic.Skin.button)) OpenDetailWindow(outputRes); }
                            else GUILayout.Label(btnLabel, _wrapLabel);
                        }
                    GUILayout.EndVertical();

                    GUILayout.EndHorizontal();
                    GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
                }
            }

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        private KhemistryResourceInfo FindResource(string name)
        {
            if (!KhemistryLibraryLoader.IsLoaded) return null;
            return KhemistryLibraryLoader.Resources.FirstOrDefault(r => r.name == name);
        }
    }

    /* Example config node
MODULE
{
	name = KhemistryAdvancedStorage
    storageType = multiShared  // Can be type or storageType. "single" stores one resource, "multi" stores multiple and can be configured, "multiShared" stores multiple all at the same time
	maximumResources = 1000.0  // Maximum resources it can hold in total. This will be shared for multiShare types but per-resource for others.
	chargingRequired = true    // Does the container need to be charged to be used
	passiveConsumption = true  // Does the container have a passive consumption
	maxInputRate = 10.0        // Maximum transfer rate to the container. Do not include if you want it to be unlimited
	maxOutputRate = 10.0       // Maximum transfer rate from the container. Do not include if you want it to be unlimited
	chargeRate = 50.0          // Percent per second to fill charge (50 = 2 seconds to full). Not required if charging is disabled
	chargeDecayRate = 5.0      // Percent per second to lose charge when storage can no longer charge. Not required if charging is disabled
    filledUnpoweredResult = boiloff,1         // What will happen if the storage is not on but has a resource. Possible options are listed below
    passiveUnsatisfiedResult = destroy,500    // What will happen if the storage cannot consume resources as part of passive consumption. Possible options are listed below
                                              // off = The container will turn off
                                              // void = All resources will be voided
                                              // destroy,50 = The part will blow up with the specified power
                                              // boiloff,1 = All resources will slowly (or not) disappear at the specified amount per second.
                                              // Note that boiloff can only be applied to filledUnpoweredResult because passiveUnsatisfiedResult is only checked once and the container turns off.
                                              // Also the fields can have double quotes (") around them but that is not recommended to do.

	SUPPORTED_RESOURCES        // Resource the container supports and can hold at the same time.
	{                          // More than one entry with single type will error out and remove all after the first one.
		name = LiquidFuel      // If it isn't present, the part will error out and the storage will not show up.
		name = Oxidizer
		name = MonoPropellant
	}

	PASSIVE_CON_NAMES          // Resources used for passive consumption. Not required if passive consumption is disabled
	{
		name = ElectricCharge
	}
	PASSIVE_CON_AMOUNTS        // Amount of each resource used for passive consumption (per second). Not required if passive consumption is disabled
	{
		amount = 0.5
	}

	CHARGE_CON_NAMES           // Resources used for charge consumption. Not required if charging is disabled
	{
		name = ElectricCharge
	}
	CHARGE_CON_AMOUNTS         // Amount of each resource used for charge consumption (per second). Not required if charging is disabled
	{
		amount = 5.0
	}
}
    */
    public class KhemistryAdvancedStorage : PartModule
    {
        // ── Config fields (KSPFields) ────────────────────────────────────────────────

        [KSPField(isPersistant = false)]
        public string storageType = "single";   // "single", "multi", "multiShared"

        [KSPField(isPersistant = false)]
        public float maximumResources = 1000f;

        [KSPField(isPersistant = false)]
        public bool chargingRequired = false;

        [KSPField(isPersistant = false)]
        public bool passiveConsumption = false;

        [KSPField(isPersistant = false)]
        public float maxInputRate = -1f;        // units per second, -1 = unlimited (not enforced yet)

        [KSPField(isPersistant = false)]
        public float maxOutputRate = -1f;       // units per second, -1 = unlimited (not enforced yet)

        [KSPField(isPersistant = false)]
        public float chargeRate = 0f;           // percent per second

        [KSPField(isPersistant = false)]
        public float chargeDecayRate = 0f;      // percent per second

        // ── Persistent runtime state ────────────────────────────────────────────────

        public enum StorageState { Off, Charging, On }

        [KSPField(isPersistant = true)]
        public float chargePercent = 0f;

        [KSPField(isPersistant = true)]
        public StorageState state = StorageState.Off;

        [KSPField(isPersistant = true)]
        public string activeResource = "";

        // ── Display fields ─────────────────────────────────────────────────────────

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true,
                  guiName = "Contents", groupName = "khemistryadvstorage",
                  groupDisplayName = "Khemistry Container", groupStartCollapsed = false)]
        public string contentsDisplay = "Empty";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true,
                  guiName = "Volume Used", groupName = "khemistryadvstorage")]
        public string volumeDisplay = "0 / 0";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "Charge", groupName = "khemistryadvstorage")]
        public string chargeDisplay = "Charge: N/A";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "State", groupName = "khemistryadvstorage")]
        public string stateDisplay = "State: Off";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "Active Resource", groupName = "khemistryadvstorage")]
        public string activeResourceDisplay = "Active: (none)";

        // ── Internal config data ───────────────────────────────────────────────────

        private readonly List<string> _supportedResources = new List<string>();

        private readonly List<string> _passiveNames = new List<string>();
        private readonly List<float> _passiveAmounts = new List<float>();

        private readonly List<string> _chargeNames = new List<string>();
        private readonly List<float> _chargeAmounts = new List<float>();

        // Parsed consequence configs
        private enum ConsequenceType { Off, Void, Destroy, Boiloff }

        private struct ConsequenceConfig
        {
            public ConsequenceType type;
            public float value;   // explosion power for Destroy; rate/sec for Boiloff
        }

        private ConsequenceConfig _passiveUnsatisfiedResult;  // fires once per unsatisfied run
        private ConsequenceConfig _filledUnpoweredResult;     // fires every 0.1 s when unpowered+filled

        // For blocking transfers while not On
        private readonly Dictionary<string, double> _frozenAmounts = new Dictionary<string, double>();

        // Consequence state
        private bool _passiveUnsatisfiedFired = false;   // re-arms when consumption succeeds
        private double _filledUnpoweredAccum = 0.0;     // time accumulator for 0.1 s ticks

        private bool _fatalConfigError = false;

        // ── UI Events ──────────────────────────────────────────────────────────────

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Enable Charging",
                  groupName = "khemistryadvstorage")]
        public void EnableCharging()
        {
            if (!chargingRequired) return;
            if (state == StorageState.On) return;
            state = StorageState.Charging;
            KShared.Instance?.Log("Charging enabled.", "KhemistryAdvancedStorage/EnableCharging");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Disable Charging",
                  groupName = "khemistryadvstorage", active = false)]
        public void DisableCharging()
        {
            if (!chargingRequired) return;
            if (state != StorageState.Charging) return;
            state = StorageState.Off;
            KShared.Instance?.Log("Charging disabled.", "KhemistryAdvancedStorage/DisableCharging");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Turn on container",
                  groupName = "khemistryadvstorage", active = false)]
        public void TurnOnContainer()
        {
            if (chargingRequired && chargePercent < 100f)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Container must be fully charged before turning on.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            state = StorageState.On;
            _passiveUnsatisfiedFired = false;
            KShared.Instance?.Log("Container turned ON.", "KhemistryAdvancedStorage/TurnOnContainer");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Turn off container",
                  groupName = "khemistryadvstorage", active = false)]
        public void TurnOffContainer()
        {
            state = StorageState.Off;
            KShared.Instance?.Log("Container turned OFF.", "KhemistryAdvancedStorage/TurnOffContainer");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Select resource",
                  groupName = "khemistryadvstorage", active = false)]
        public void SelectResource()
        {
            if (storageType != "multi") return;

            if (!string.IsNullOrEmpty(activeResource))
            {
                var def = PartResourceLibrary.Instance.GetDefinition(activeResource);
                if (def != null)
                {
                    PartResource pr = part.Resources.Get(def.id);
                    if (pr != null && pr.amount >= 1.0)
                    {
                        ScreenMessages.PostScreenMessage(new ScreenMessage(
                            "Container must be nearly empty to switch resource.", 5f, ScreenMessageStyle.UPPER_CENTER));
                        return;
                    }
                    if (pr != null) pr.amount = 0.0;
                }
            }

            if (_supportedResources.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No supported resources configured.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            var shared = KShared.Instance;
            if (shared == null)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "KShared not available.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            shared.ShowSelector("Select active resource", new List<string>(_supportedResources), label =>
            {
                activeResource = label;
                KShared.Instance?.Log("Active resource set to " + activeResource,
                    "KhemistryAdvancedStorage/SelectResource");
                ZeroNonActiveResources();
            });
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────────

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            _fatalConfigError = false;
            LoadConfigFromPartInfo();

            if (_fatalConfigError)
            {
                foreach (BaseEvent e in Events) e.active = false;
                contentsDisplay = "ERROR: see log";
                return;
            }

            EnsureResourcesExistOnPart();
            SnapshotFrozenAmounts();
            _passiveUnsatisfiedFired = false;
            _filledUnpoweredAccum = 0.0;
            UpdateUI();
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (vessel == null || part == null) return;
            if (_fatalConfigError) return;

            double dt = TimeWarp.fixedDeltaTime;

            HandleCharging(dt);
            HandlePassiveConsumption(dt);
            HandleTransferBlocking();
            HandleFilledUnpowered(dt);
            EnforceCapacity();
            UpdateUI();
        }

        public override void OnUpdate()
        {
            if (_fatalConfigError) return;
            UpdateUI();
        }

        // ── Config loading ─────────────────────────────────────────────────────────

        private void LoadConfigFromPartInfo()
        {
            if (part.partInfo?.partConfig == null)
            {
                KShared.Instance?.LogError("partInfo.partConfig is null!",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            ConfigNode moduleNode = null;
            foreach (ConfigNode n in part.partInfo.partConfig.GetNodes("MODULE"))
            {
                if (n.GetValue("name") == "KhemistryAdvancedStorage") { moduleNode = n; break; }
            }

            if (moduleNode == null)
            {
                KShared.Instance?.LogError("Could not find MODULE node in partConfig!",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            // SUPPORTED_RESOURCES — required
            _supportedResources.Clear();
            if (!moduleNode.HasNode("SUPPORTED_RESOURCES"))
            {
                KShared.Instance?.LogError(
                    "Part \"" + part.name + "\" has KhemistryAdvancedStorage but no SUPPORTED_RESOURCES node. This module will not load.",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }
            foreach (string n in moduleNode.GetNode("SUPPORTED_RESOURCES").GetValues("name"))
                _supportedResources.Add(n.Trim());
            if (_supportedResources.Count == 0)
            {
                KShared.Instance?.LogError(
                    "Part \"" + part.name + "\" has KhemistryAdvancedStorage with an empty SUPPORTED_RESOURCES node. This module will not load.",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            // Scalar fields
            storageType = moduleNode.GetValue("storageType") ?? moduleNode.GetValue("type") ?? "single";

            float tmp;
            if (float.TryParse(moduleNode.GetValue("maximumResources"), out tmp)) maximumResources = tmp;
            maxInputRate = float.TryParse(moduleNode.GetValue("maxInputRate"), out tmp) ? tmp : -1f;
            maxOutputRate = float.TryParse(moduleNode.GetValue("maxOutputRate"), out tmp) ? tmp : -1f;
            if (float.TryParse(moduleNode.GetValue("chargeRate"), out tmp)) chargeRate = tmp;
            if (float.TryParse(moduleNode.GetValue("chargeDecayRate"), out tmp)) chargeDecayRate = tmp;

            bool tmpB;
            if (bool.TryParse(moduleNode.GetValue("chargingRequired"), out tmpB)) chargingRequired = tmpB;
            if (bool.TryParse(moduleNode.GetValue("passiveConsumption"), out tmpB)) passiveConsumption = tmpB;

            // Consequence configs
            _passiveUnsatisfiedResult = ParseConsequence(
                moduleNode.GetValue("passiveUnsatisfiedResult"), allowBoiloff: false,
                "passiveUnsatisfiedResult", "off");

            _filledUnpoweredResult = ParseConsequence(
                moduleNode.GetValue("filledUnpoweredResult"), allowBoiloff: true,
                "filledUnpoweredResult", "off");

            // PASSIVE_CON_NAMES / PASSIVE_CON_AMOUNTS
            _passiveNames.Clear();
            _passiveAmounts.Clear();
            if (passiveConsumption)
            {
                if (moduleNode.HasNode("PASSIVE_CON_NAMES"))
                    foreach (string n in moduleNode.GetNode("PASSIVE_CON_NAMES").GetValues("name"))
                        _passiveNames.Add(n.Trim());
                if (moduleNode.HasNode("PASSIVE_CON_AMOUNTS"))
                    foreach (string a in moduleNode.GetNode("PASSIVE_CON_AMOUNTS").GetValues("amount"))
                    { if (float.TryParse(a, out tmp)) _passiveAmounts.Add(tmp); }
                if (_passiveNames.Count != _passiveAmounts.Count)
                    KShared.Instance?.LogError("PASSIVE_CON_NAMES and PASSIVE_CON_AMOUNTS length mismatch.",
                        "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
            }

            // CHARGE_CON_NAMES / CHARGE_CON_AMOUNTS
            _chargeNames.Clear();
            _chargeAmounts.Clear();
            if (chargingRequired)
            {
                if (moduleNode.HasNode("CHARGE_CON_NAMES"))
                    foreach (string n in moduleNode.GetNode("CHARGE_CON_NAMES").GetValues("name"))
                        _chargeNames.Add(n.Trim());
                if (moduleNode.HasNode("CHARGE_CON_AMOUNTS"))
                    foreach (string a in moduleNode.GetNode("CHARGE_CON_AMOUNTS").GetValues("amount"))
                    { if (float.TryParse(a, out tmp)) _chargeAmounts.Add(tmp); }
                if (_chargeNames.Count != _chargeAmounts.Count)
                    KShared.Instance?.LogError("CHARGE_CON_NAMES and CHARGE_CON_AMOUNTS length mismatch.",
                        "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
            }

            // Active resource defaults
            if ((storageType == "single" || storageType == "multi") && string.IsNullOrEmpty(activeResource))
                if (_supportedResources.Count > 0) activeResource = _supportedResources[0];

            if (storageType == "single" && _supportedResources.Count > 1)
            {
                KShared.Instance?.LogError(
                    "storageType=single but multiple SUPPORTED_RESOURCES defined; only first will be used.",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                string keep = _supportedResources[0];
                _supportedResources.Clear();
                _supportedResources.Add(keep);
                activeResource = keep;
            }

            KShared.Instance?.Log(
                string.Format("Config loaded. storageType={0}, max={1}, chargingRequired={2}, passiveConsumption={3}, passiveUnsatisfiedResult={4}, filledUnpoweredResult={5}",
                    storageType, maximumResources, chargingRequired, passiveConsumption,
                    _passiveUnsatisfiedResult.type, _filledUnpoweredResult.type),
                "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
        }

        /// <summary>
        /// Parses "off", "void", "destroy,10", "boiloff,1.5" into a ConsequenceConfig.
        /// Falls back to the specified default if the raw value is null/invalid.
        /// </summary>
        private ConsequenceConfig ParseConsequence(string raw, bool allowBoiloff, string fieldName, string fallback)
        {
            // KSP config values may include surrounding quotes when the cfg uses = "value" syntax; strip them.
            string src = string.IsNullOrEmpty(raw) ? fallback : raw.Trim().Trim('"').Trim().ToLower();

            if (src == "off") return new ConsequenceConfig { type = ConsequenceType.Off };
            if (src == "void") return new ConsequenceConfig { type = ConsequenceType.Void };

            if (src.StartsWith("destroy,"))
            {
                float v;
                if (float.TryParse(src.Substring(8), out v))
                    return new ConsequenceConfig { type = ConsequenceType.Destroy, value = v };
                KShared.Instance?.LogError("Could not parse destroy power in " + fieldName + "=\"" + raw + "\". Defaulting to off.",
                    "KhemistryAdvancedStorage/ParseConsequence");
                return new ConsequenceConfig { type = ConsequenceType.Off };
            }

            if (allowBoiloff && src.StartsWith("boiloff,"))
            {
                float v;
                if (float.TryParse(src.Substring(8), out v))
                    return new ConsequenceConfig { type = ConsequenceType.Boiloff, value = v };
                KShared.Instance?.LogError("Could not parse boiloff rate in " + fieldName + "=\"" + raw + "\". Defaulting to off.",
                    "KhemistryAdvancedStorage/ParseConsequence");
                return new ConsequenceConfig { type = ConsequenceType.Off };
            }

            KShared.Instance?.LogError("Unknown consequence value " + fieldName + "=\"" + raw + "\". Defaulting to off.",
                "KhemistryAdvancedStorage/ParseConsequence");
            return new ConsequenceConfig { type = ConsequenceType.Off };
        }

        // ── Resource setup ──────────────────────────────────────────────────────────

        private void EnsureResourcesExistOnPart()
        {
            foreach (string resName in _supportedResources)
            {
                var def = PartResourceLibrary.Instance.GetDefinition(resName);
                if (def == null)
                {
                    KShared.Instance?.LogError("Unknown resource \"" + resName + "\" in SUPPORTED_RESOURCES.",
                        "KhemistryAdvancedStorage/EnsureResourcesExistOnPart");
                    continue;
                }

                PartResource existing = part.Resources.Get(def.id);
                if (existing == null)
                {
                    ConfigNode node = new ConfigNode("RESOURCE");
                    node.AddValue("name", resName);
                    node.AddValue("amount", 0.0);
                    node.AddValue("maxAmount", maximumResources);
                    part.AddResource(node);
                }
                else
                {
                    existing.maxAmount = maximumResources;
                    if (existing.amount < 0.0) existing.amount = 0.0;
                }
            }

            if (storageType == "multi")
                ZeroNonActiveResources();
        }

        private void ZeroNonActiveResources()
        {
            foreach (PartResource pr in part.Resources)
            {
                if (!_supportedResources.Contains(pr.resourceName)) continue;
                if (!string.IsNullOrEmpty(activeResource) && pr.resourceName != activeResource)
                    pr.amount = 0.0;
            }
        }

        // ── Charging logic ─────────────────────────────────────────────────────────

        private void HandleCharging(double dt)
        {
            if (!chargingRequired) return;

            if (state == StorageState.Off)
            {
                // Container is off — charge decays passively
                if (chargeDecayRate > 0f)
                {
                    chargePercent -= chargeDecayRate * (float)dt;
                    if (chargePercent < 0f) chargePercent = 0f;
                }
                return;
            }

            if (state != StorageState.Charging) return;

            // Already full — flip to On
            if (chargePercent >= 100f)
            {
                chargePercent = 100f;
                state = StorageState.On;
                KShared.Instance?.Log("Container fully charged, now ON.",
                    "KhemistryAdvancedStorage/HandleCharging");
                return;
            }

            bool satisfied = ConsumeVesselResources(_chargeNames, _chargeAmounts, dt);
            if (satisfied)
            {
                chargePercent += chargeRate * (float)dt;
                if (chargePercent > 100f) chargePercent = 100f;
            }
            else
            {
                // Can't get charge resources — decay if configured
                if (chargeDecayRate > 0f)
                {
                    chargePercent -= chargeDecayRate * (float)dt;
                    if (chargePercent < 0f) chargePercent = 0f;
                }
            }
        }

        // ── Passive consumption ────────────────────────────────────────────────────

        private void HandlePassiveConsumption(double dt)
        {
            if (!passiveConsumption) return;

            // Only attempt to draw resources when the container is actually running.
            // We still check the consequence even after an "off" result has dropped state
            // out of On, so consumption and consequence are handled separately.
            if (state == StorageState.On)
            {
                bool satisfied = ConsumeVesselResources(_passiveNames, _passiveAmounts, dt);
                if (satisfied)
                {
                    // Re-arm so a future failure can trigger the consequence again.
                    _passiveUnsatisfiedFired = false;
                    return;
                }
            }

            // Not On, or consumption failed. Only apply consequence if the container
            // actually has something stored — empty containers don't need protecting.
            if (!HasAnyStoredResources()) return;

            if (!_passiveUnsatisfiedFired)
            {
                _passiveUnsatisfiedFired = true;
                ApplyConsequence(_passiveUnsatisfiedResult, "passiveUnsatisfiedResult");
            }
        }

        // ── Filled-unpowered consequence ───────────────────────────────────────────

        private void HandleFilledUnpowered(double dt)
        {
            // "Unpowered" means the container is not On
            if (state == StorageState.On) return;

            // Only applies when there is actually something stored
            if (!HasAnyStoredResources()) return;

            _filledUnpoweredAccum += dt;

            // Fire every 0.1 seconds
            while (_filledUnpoweredAccum >= 0.1)
            {
                _filledUnpoweredAccum -= 0.1;
                ApplyConsequence(_filledUnpoweredResult, "filledUnpoweredResult", tickDt: 0.1);
            }
        }

        // ── Consequence execution ──────────────────────────────────────────────────

        /// <summary>
        /// Executes a consequence. tickDt is only used by Boiloff (the value in config is per second;
        /// we receive a 0.1 s tick so we apply value * 0.1 per call).
        /// </summary>
        private void ApplyConsequence(ConsequenceConfig cfg, string source, double tickDt = 0.0)
        {
            switch (cfg.type)
            {
                case ConsequenceType.Off:
                    break;  // Do nothing

                case ConsequenceType.Void:
                    KShared.Instance?.Log("Voiding all stored resources (" + source + ").",
                        "KhemistryAdvancedStorage/ApplyConsequence");
                    foreach (PartResource pr in part.Resources)
                    {
                        if (_supportedResources.Contains(pr.resourceName))
                            pr.amount = 0.0;
                    }
                    break;

                case ConsequenceType.Destroy:
                    KShared.Instance?.Log(
                        string.Format("Destroying part with power {0:F1} ({1}).", cfg.value, source),
                        "KhemistryAdvancedStorage/ApplyConsequence");
                    part.explode();
                    break;

                case ConsequenceType.Boiloff:
                    // cfg.value is loss per second; tickDt is 0.1 s, so loss per tick = cfg.value * tickDt
                    ApplyBoiloff(cfg.value * (float)tickDt, source);
                    break;
            }
        }

        /// <summary>
        /// Reduces stored resources by a flat amount per tick, distributed proportionally
        /// across all resources that currently have any amount. Works correctly for all
        /// storage types:
        ///   single / multi   — only the active resource has any amount, so it drains alone.
        ///   multiShared      — all resources drain proportionally to their current fill.
        /// </summary>
        private void ApplyBoiloff(float amountPerTick, string source)
        {
            // Collect resources that currently hold something
            var filled = new List<PartResource>();
            double total = 0.0;
            foreach (PartResource pr in part.Resources)
            {
                if (!_supportedResources.Contains(pr.resourceName)) continue;
                if (pr.amount > 0.0) { filled.Add(pr); total += pr.amount; }
            }
            if (filled.Count == 0 || total <= 0.0) return;

            double toDrain = Math.Min(amountPerTick, total);

            foreach (PartResource pr in filled)
            {
                double share = (pr.amount / total) * toDrain;
                pr.amount = Math.Max(0.0, pr.amount - share);
                // Update the freeze snapshot so HandleTransferBlocking doesn't revert the drain next tick.
                _frozenAmounts[pr.resourceName] = pr.amount;
            }

            KShared.Instance?.Log(
                string.Format("Boiloff: drained {0:F4} units ({1}).", toDrain, source),
                "KhemistryAdvancedStorage/ApplyBoiloff");
        }

        // ── Vessel resource consumption ────────────────────────────────────────────

        /// <summary>
        /// Pulls the given resources from the vessel network. Returns true only if every
        /// resource was fully satisfied. Refunds all pulled resources if any fall short
        /// (all-or-nothing semantics).
        /// </summary>
        private bool ConsumeVesselResources(List<string> names, List<float> amounts, double dt)
        {
            if (names.Count == 0 || amounts.Count == 0) return true;
            if (names.Count != amounts.Count) return false;

            var pulled = new List<double>(names.Count);
            bool allSatisfied = true;

            for (int i = 0; i < names.Count; i++)
            {
                float rate = amounts[i];
                if (rate <= 0f) { pulled.Add(0.0); continue; }

                var def = PartResourceLibrary.Instance.GetDefinition(names[i]);
                if (def == null)
                {
                    KShared.Instance?.LogError("Unknown resource \"" + names[i] + "\" in consumption list.",
                        "KhemistryAdvancedStorage/ConsumeVesselResources");
                    pulled.Add(0.0);
                    allSatisfied = false;
                    continue;
                }

                double needed = rate * dt;
                double got = part.RequestResource(names[i], needed);
                pulled.Add(got);

                // Allow a small tolerance (0.1% of needed)
                if (got < needed * 0.999)
                    allSatisfied = false;
            }

            if (!allSatisfied)
            {
                // Refund everything that was pulled
                for (int i = 0; i < names.Count; i++)
                    if (pulled[i] > 0.0)
                        part.RequestResource(names[i], -pulled[i]);
                return false;
            }

            return true;
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private bool HasAnyStoredResources()
        {
            foreach (PartResource pr in part.Resources)
                if (_supportedResources.Contains(pr.resourceName) && pr.amount > 0.0)
                    return true;
            return false;
        }

        // ── Transfer blocking while not On ────────────────────────────────────────

        private void SnapshotFrozenAmounts()
        {
            _frozenAmounts.Clear();
            foreach (PartResource pr in part.Resources)
            {
                if (!_supportedResources.Contains(pr.resourceName)) continue;
                _frozenAmounts[pr.resourceName] = pr.amount;
            }
        }

        private void HandleTransferBlocking()
        {
            bool shouldFreeze =
                (chargingRequired && state != StorageState.On) ||
                (!chargingRequired && state == StorageState.Off);

            foreach (PartResource pr in part.Resources)
            {
                if (!_supportedResources.Contains(pr.resourceName)) continue;

                if (!shouldFreeze)
                {
                    _frozenAmounts[pr.resourceName] = pr.amount;
                }
                else
                {
                    double frozen;
                    if (_frozenAmounts.TryGetValue(pr.resourceName, out frozen))
                        pr.amount = frozen;
                    else
                        _frozenAmounts[pr.resourceName] = pr.amount;
                }
            }
        }

        // ── Capacity enforcement ───────────────────────────────────────────────────

        private void EnforceCapacity()
        {
            // Clamp negatives first
            foreach (PartResource pr in part.Resources)
                if (pr.amount < 0.0) pr.amount = 0.0;

            if (storageType == "multiShared")
            {
                double total = 0.0;
                var list = new List<PartResource>();
                foreach (PartResource pr in part.Resources)
                {
                    if (!_supportedResources.Contains(pr.resourceName)) continue;
                    list.Add(pr);
                    total += pr.amount;
                }

                if (total > maximumResources && total > 0.0)
                {
                    double scale = maximumResources / total;
                    foreach (PartResource pr in list) pr.amount *= scale;
                }

                foreach (PartResource pr in list) pr.maxAmount = maximumResources;
            }
            else
            {
                foreach (PartResource pr in part.Resources)
                {
                    if (!_supportedResources.Contains(pr.resourceName)) continue;
                    pr.amount = Math.Min(pr.amount, maximumResources);
                    pr.amount = Math.Max(pr.amount, 0.0);
                    pr.maxAmount = maximumResources;
                }

                if (storageType == "multi")
                    ZeroNonActiveResources();
            }
        }

        // ── UI updates ─────────────────────────────────────────────────────────────

        private void UpdateUI()
        {
            double total = 0.0;
            var parts = new List<string>();

            foreach (PartResource pr in part.Resources)
            {
                if (!_supportedResources.Contains(pr.resourceName)) continue;
                if (pr.amount > 0.0)
                {
                    parts.Add(string.Format("{0}: {1:F2}", pr.resourceName, pr.amount));
                    total += pr.amount;
                }
            }

            contentsDisplay = parts.Count == 0 ? "Empty" : string.Join(", ", parts.ToArray());
            volumeDisplay = string.Format("{0:F2} / {1:F2}", total, maximumResources);

            chargeDisplay = chargingRequired
                ? string.Format("Charge: {0:F1}%", chargePercent)
                : "Charge: N/A";

            stateDisplay = "State: " + state.ToString();

            activeResourceDisplay = (storageType == "single" || storageType == "multi")
                ? (string.IsNullOrEmpty(activeResource) ? "Active: (none)" : "Active: " + activeResource)
                : "Active: (multiShared)";

            Events["EnableCharging"].active = chargingRequired && state != StorageState.Charging && state != StorageState.On;
            Events["DisableCharging"].active = chargingRequired && state == StorageState.Charging;
            Events["TurnOnContainer"].active = state != StorageState.On;
            Events["TurnOffContainer"].active = state == StorageState.On;
            Events["SelectResource"].active = storageType == "multi";
        }
    }
    // Advanced ISRU module with a variety of features
    // ── Base class ─────────────────────────────────────────────────────────────────
    // Contains all shared config, cycle logic, and helpers.
    // KhemistryAdvancedISRU and KhemistryEVAAdvancedISRU both extend this.
    // ──────────────────────────────────────────────────────────────────────────────

    public abstract class KhemistryAdvancedISRUBase : PartModule
    {
        // ── Basic converter info ───────────────────────────────────────────────────

        [KSPField(isPersistant = false)] public string ConverterName = "Converter";
        [KSPField(isPersistant = false)] public string StartActionName = "Start Converter";
        [KSPField(isPersistant = false)] public string StopActionName = "Stop Converter";

        // -- Charging stuff --

        [KSPField(isPersistant = false)]
        public bool chargingRequired = false;

        [KSPField(isPersistant = false)]
        public float chargeRate = 0f;           // percent per second

        [KSPField(isPersistant = false)]
        public float chargeDecayRate = 0f;      // percent per second

        protected readonly List<string> _chargeNames = new List<string>();
        protected readonly List<float> _chargeAmounts = new List<float>();

        public enum ConverterState { Off, Charging, On }

        [KSPField(isPersistant = true)]
        public float chargePercent = 0f;

        [KSPField(isPersistant = true)]
        public ConverterState state = ConverterState.Off;

        // ── UI Events ──────────────────────────────────────────────────────────────

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Enable Charging",
                  groupName = "khemistryisru")]
        public void EnableCharging()
        {
            if (!chargingRequired) return;
            if (state == ConverterState.On) return;
            state = ConverterState.Charging;
            KShared.Instance?.Log("Charging enabled.", "KhemistryAdvancedISRUBase/EnableCharging");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Disable Charging",
                  groupName = "khemistryisru", active = false)]
        public void DisableCharging()
        {
            if (!chargingRequired) return;
            if (state != ConverterState.Charging) return;
            state = ConverterState.Off;
            KShared.Instance?.Log("Charging disabled.", "KhemistryAdvancedISRUBase/DisableCharging");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Prepare converter",
                  groupName = "khemistryisru", active = false)]
        public void TurnOnContainer()
        {
            if (chargingRequired && chargePercent < 100f)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter must be fully charged before turning on.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            state = ConverterState.On;
            KShared.Instance?.Log("Converter turned ON.", "KhemistryAdvancedISRUBase/TurnOnContainer");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Turn off converter",
                  groupName = "khemistryadvstorage", active = false)]
        public void TurnOffContainer()
        {
            state = ConverterState.Off;
            KShared.Instance?.Log("Converter turned OFF.", "KhemistryAdvancedISRUBase/TurnOffContainer");
        }

        // ── Persistent state ───────────────────────────────────────────────────────

        [KSPField(isPersistant = true)] public bool isRunning = false;
        [KSPField(isPersistant = true)] public bool needsMaintenance = false;

        // ── Status display ─────────────────────────────────────────────────────────

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "Status", groupName = "khemistryisru",
                  groupDisplayName = "Khemistry ISRU", groupStartCollapsed = false)]
        public string statusDisplay = "Stopped";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "Charge", groupName = "khemistryisru")]
        public string chargeDisplay = "Charge: N/A";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "State", groupName = "khemistryisru")]
        public string stateDisplay = "State: Off";

        // ── Internal data structures ───────────────────────────────────────────────

        // True only on ticks where the converter actually completed a cycle (consumed
        // inputs and produced outputs). Used by subclasses to drive visual feedback
        // such as part animations.
        public bool IsCurrentlyActive { get; protected set; } = false;

        protected struct ResourceInput
        {
            public string resourceName;
            public double ratio;
            public ResourceFlowMode flowMode;
        }

        protected struct ResourceOutput
        {
            public string resourceName;
            public double ratio;
            public bool dumpExcess;
        }

        protected enum SituationCondition
        {
            Any, Landed, Splashed, FlyingLow, FlyingHigh, SpaceLow, SpaceHigh, SubOrbital
        }

        protected enum PowerfailResult { None, Stop, Explode, Maint }

        protected readonly List<ResourceInput> _inputs = new List<ResourceInput>();
        protected readonly List<ResourceOutput> _outputs = new List<ResourceOutput>();

        // Conditions
        protected string _planetCondition = null;
        protected string _biomeCondition = null;
        protected double _altMin = double.MinValue;
        protected double _altMax = double.MaxValue;
        protected SituationCondition _situationCondition = SituationCondition.Any;
        protected string _depositCondition = null;

        // Powerfail
        protected string _powerfailResource = null;
        protected PowerfailResult _powerfailResult = PowerfailResult.None;
        protected float _powerfailExplosionPower = 0f;

        // Manual operation config
        protected bool _manualOperation = false;
        protected bool _manualRequiresStartup = true;

        // Show rules
        protected bool _startStopShowPAW = true;
        protected bool _startStopShowEVA = false;
        protected bool _manualShowPAW = true;
        protected bool _manualShowEVA = false;

        // Interaction distance
        protected float _maxInteractionDistance = 10f;

        // Recipe group
        protected string _recipeGroup = null;

        // Computed display name
        protected string _displayName = "Converter";

        // Runtime
        protected bool _fatalConfigError = false;
        protected double _outputWarnCooldown = 0.0;

        /// <summary>
        /// Runs one converter cycle against the kerbal's suit cell instead of the vessel
        /// network. The dictionary maps resource name → amount (at most one entry, since
        /// the suit cell holds one resource type at a time). Mutates the dictionary in place.
        /// Returns true if the cycle ran.
        /// </summary>
        public bool RunOneCycleSuitCell(Part contextPart, Dictionary<string, double> suitCell,
    float suitMaxAmount, double dt)
        {
            // Conditions first — identical ordering to RunOneCycle
            string conditionReason;
            if (!CheckConditions(contextPart.vessel, out conditionReason))
            {
                statusDisplay = "Inactive: " + conditionReason;
                return false;
            }

            // Total-capacity check: (current − inputs + non-dump outputs) must fit
            double currentTotal = DictTotal(suitCell);
            double inputConsumed = 0.0;
            foreach (ResourceInput inp in _inputs) inputConsumed += inp.ratio * dt;
            double outputProduced = 0.0;
            foreach (ResourceOutput o in _outputs)
                if (!o.dumpExcess) outputProduced += o.ratio * dt;

            if (currentTotal - inputConsumed + outputProduced > suitMaxAmount + 1e-9)
            {
                if (_outputWarnCooldown <= 0.0)
                {
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        string.Format("Converter \"{0}\": Not enough space in suit cell, converter paused!",
                            _displayName),
                        5f, ScreenMessageStyle.UPPER_CENTER));
                    _outputWarnCooldown = 5.0;
                }
                statusDisplay = "Paused: suit cell full";
                return false;
            }

            // Powerfail check
            bool powerfailShort = false;
            if (_powerfailResource != null)
            {
                double pfNeeded;
                suitCell.TryGetValue(_powerfailResource, out pfNeeded);
                double pfAvailable = pfNeeded;   // reuse variable — get actual available
                suitCell.TryGetValue(_powerfailResource, out pfAvailable);
                if (pfAvailable < GetInputRatio(_powerfailResource) * dt * 0.999)
                    powerfailShort = true;
            }

            // Input availability
            bool allSatisfied = true;
            foreach (ResourceInput inp in _inputs)
            {
                if (inp.ratio <= 0.0) continue;
                double available;
                suitCell.TryGetValue(inp.resourceName, out available);
                if (available < inp.ratio * dt * 0.999) { allSatisfied = false; break; }
            }

            if (!allSatisfied)
            {
                if (powerfailShort)
                {
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        string.Format("Converter \"{0}\": Powerfailed due to lack of {1}!",
                            _displayName, _powerfailResource),
                        8f, ScreenMessageStyle.UPPER_CENTER));
                    TriggerPowerfail(contextPart);
                }
                else
                {
                    statusDisplay = "Insufficient resources";
                }
                return false;
            }

            // Consume inputs
            foreach (ResourceInput inp in _inputs)
            {
                if (inp.ratio <= 0.0) continue;
                double current;
                suitCell.TryGetValue(inp.resourceName, out current);
                double remaining = current - inp.ratio * dt;
                if (remaining < 1e-9) suitCell.Remove(inp.resourceName);
                else suitCell[inp.resourceName] = remaining;
            }

            // Produce outputs
            foreach (ResourceOutput output in _outputs)
            {
                if (output.ratio <= 0.0) continue;
                double toAdd = output.ratio * dt;
                if (output.dumpExcess)
                {
                    double spaceLeft = suitMaxAmount - DictTotal(suitCell);
                    toAdd = Math.Min(toAdd, Math.Max(0.0, spaceLeft));
                }
                if (toAdd <= 1e-12) continue;
                double existing;
                suitCell.TryGetValue(output.resourceName, out existing);
                suitCell[output.resourceName] = existing + toAdd;
            }

            statusDisplay = _manualOperation ? "Waiting for manual cycle" : "Running";
            return true;
        }

        private static double DictTotal(Dictionary<string, double> dict)
        {
            double total = 0.0;
            foreach (var kvp in dict) total += kvp.Value;
            return total;
        }

        // ── Config loading ─────────────────────────────────────────────────────────

        /// <summary>
        /// Finds and returns the MODULE config node for this converter from partConfig.
        /// Matches on both module class name and ConverterName to support multiple
        /// converters per part. Pass the expected module name (e.g. "KhemistryAdvancedISRU"
        /// or "KhemistryEVAAdvancedISRU").
        /// </summary>
        protected ConfigNode FindModuleConfigNode(string moduleName)
        {
            ConfigNode result = null;

            // Try partInfo first (works for normal parts)
            if (part.partInfo?.partConfig != null)
            {
                foreach (ConfigNode n in part.partInfo.partConfig.GetNodes("MODULE"))
                {
                    if (n.GetValue("name") != moduleName) continue;
                    if (n.GetValue("ConverterName") == ConverterName) { result = n; break; }
                }
            }

            if (result != null) return result;

            // Fallback: search GameDatabase directly (required for kerbalEVA parts
            // whose partInfo.partConfig is null or does not contain the expected node)
            string targetPartName = part.partInfo?.name ?? part.name;
            foreach (ConfigNode partNode in GameDatabase.Instance.GetConfigNodes("PART"))
            {
                string nodeName = partNode.GetValue("name") ?? "";
                int slash = nodeName.LastIndexOf('/');
                if (slash >= 0) nodeName = nodeName.Substring(slash + 1);
                if (!nodeName.Equals(targetPartName, StringComparison.OrdinalIgnoreCase)) continue;

                foreach (ConfigNode n in partNode.GetNodes("MODULE"))
                {
                    if (n.GetValue("name") != moduleName) continue;
                    if (n.GetValue("ConverterName") == ConverterName) { result = n; break; }
                }
                if (result != null) break;
            }

            if (result == null)
                KShared.Instance?.LogError(
                    "Could not find MODULE " + moduleName + " with ConverterName=\"" + ConverterName
                    + "\" in partConfig or GameDatabase!",
                    moduleName + "/FindModuleConfigNode");

            return result;
        }

        /// <summary>
        /// Populates all shared fields from a MODULE config node.
        /// Called by subclasses in their own LoadConfigFromPartInfo after finding the node.
        /// </summary>
        protected void LoadSharedConfig(ConfigNode moduleNode, string moduleName)
        {
            // INPUT_RESOURCE nodes
            _inputs.Clear();
            foreach (ConfigNode inputNode in moduleNode.GetNodes("INPUT_RESOURCE"))
            {
                string resName = inputNode.GetValue("ResourceName");
                if (string.IsNullOrEmpty(resName)) continue;

                double ratio = 0.0;
                double.TryParse(inputNode.GetValue("Ratio"), out ratio);

                ResourceFlowMode flowMode = ResourceFlowMode.ALL_VESSEL;
                string flowStr = inputNode.GetValue("FlowMode");
                if (!string.IsNullOrEmpty(flowStr))
                {
                    ResourceFlowMode parsed;
                    if (Enum.TryParse(flowStr.Trim(), true, out parsed))
                        flowMode = parsed;
                    else
                        KShared.Instance?.LogError(
                            "Converter \"" + ConverterName + "\": Unknown FlowMode \"" + flowStr + "\" for " + resName + ", defaulting to ALL_VESSEL.",
                            moduleName + "/LoadSharedConfig");
                }

                _inputs.Add(new ResourceInput { resourceName = resName, ratio = ratio, flowMode = flowMode });
            }

            // OUTPUT_RESOURCE nodes
            _outputs.Clear();
            foreach (ConfigNode outputNode in moduleNode.GetNodes("OUTPUT_RESOURCE"))
            {
                string resName = outputNode.GetValue("ResourceName");
                if (string.IsNullOrEmpty(resName)) continue;

                double ratio = 0.0;
                double.TryParse(outputNode.GetValue("Ratio"), out ratio);

                bool dumpExcess = false;
                bool.TryParse(outputNode.GetValue("DumpExcess"), out dumpExcess);

                _outputs.Add(new ResourceOutput { resourceName = resName, ratio = ratio, dumpExcess = dumpExcess });
            }

            if (_inputs.Count == 0 && _outputs.Count == 0)
                KShared.Instance?.LogError(
                    "Converter \"" + ConverterName + "\" has no INPUT_RESOURCE or OUTPUT_RESOURCE nodes — it will do nothing.",
                    moduleName + "/LoadSharedConfig");

            // planetCondition / biomeCondition
            _planetCondition = NullIfEmpty(moduleNode.GetValue("planetCondition"));
            _biomeCondition = NullIfEmpty(moduleNode.GetValue("biomeCondition"));
            if (_biomeCondition != null && _planetCondition == null)
            {
                KShared.Instance?.LogError(
                    "Converter \"" + ConverterName + "\": biomeCondition set without planetCondition — biomeCondition ignored.",
                    moduleName + "/LoadSharedConfig");
                _biomeCondition = null;
            }

            // altitudeMinCondition / altitudeMaxCondition
            _altMin = double.MinValue;
            _altMax = double.MaxValue;
            double altTmp;
            if (double.TryParse(moduleNode.GetValue("altitudeMinCondition"), out altTmp)) _altMin = altTmp;
            if (double.TryParse(moduleNode.GetValue("altitudeMaxCondition"), out altTmp)) _altMax = altTmp;

            // situationCondition
            _situationCondition = SituationCondition.Any;
            string sitStr = NullIfEmpty(moduleNode.GetValue("situationCondition"));
            if (sitStr != null)
            {
                if (sitStr.Equals("FlyindHigh", StringComparison.OrdinalIgnoreCase))
                    sitStr = "FlyingHigh";
                SituationCondition parsed;
                if (Enum.TryParse(sitStr, true, out parsed))
                    _situationCondition = parsed;
                else
                    KShared.Instance?.LogError(
                        "Converter \"" + ConverterName + "\": Unknown situationCondition \"" + sitStr + "\" — condition ignored.",
                        moduleName + "/LoadSharedConfig");
            }

            // depositCondition
            _depositCondition = NullIfEmpty(moduleNode.GetValue("depositCondition"));

            // charging
            float tmp;
            if (float.TryParse(moduleNode.GetValue("chargeRate"), out tmp)) chargeRate = tmp;
            if (float.TryParse(moduleNode.GetValue("chargeDecayRate"), out tmp)) chargeDecayRate = tmp;

            bool tmp2;
            if (bool.TryParse(moduleNode.GetValue("chargingRequired"), out tmp2)) chargingRequired = tmp2;

            // CHARGE_CON_NAMES / CHARGE_CON_AMOUNTS
            _chargeNames.Clear();
            _chargeAmounts.Clear();
            if (chargingRequired)
            {
                if (moduleNode.HasNode("CHARGE_CON_NAMES"))
                    foreach (string n in moduleNode.GetNode("CHARGE_CON_NAMES").GetValues("name"))
                        _chargeNames.Add(n.Trim());
                if (moduleNode.HasNode("CHARGE_CON_AMOUNTS"))
                    foreach (string a in moduleNode.GetNode("CHARGE_CON_AMOUNTS").GetValues("amount"))
                    { if (float.TryParse(a, out tmp)) _chargeAmounts.Add(tmp); }
                if (_chargeNames.Count != _chargeAmounts.Count)
                    KShared.Instance?.LogError("CHARGE_CON_NAMES and CHARGE_CON_AMOUNTS length mismatch.",
                        "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
            }

            // powerfailResource / powerfailResult
            _powerfailResource = null;
            _powerfailResult = PowerfailResult.None;
            _powerfailExplosionPower = 0f;

            string pfRes = NullIfEmpty(moduleNode.GetValue("powerfailResource"));
            string pfResultRaw = NullIfEmpty(moduleNode.GetValue("powerfailResult"));

            if (pfRes != null)
            {
                bool found = false;
                foreach (ResourceInput inp in _inputs)
                    if (inp.resourceName.Equals(pfRes, StringComparison.OrdinalIgnoreCase)) { found = true; break; }

                if (!found)
                {
                    KShared.Instance?.LogError(
                        "Converter \"" + ConverterName + "\": powerfailResource \"" + pfRes + "\" is not a defined INPUT_RESOURCE — powerfail disabled.",
                        moduleName + "/LoadSharedConfig");
                }
                else
                {
                    _powerfailResource = pfRes;
                    if (pfResultRaw != null)
                    {
                        string pfResult = pfResultRaw.Trim().Trim('"').ToUpper();
                        if (pfResult == "STOP")
                        {
                            _powerfailResult = PowerfailResult.Stop;
                        }
                        else if (pfResult == "MAINT")
                        {
                            _powerfailResult = PowerfailResult.Maint;
                        }
                        else if (pfResult.StartsWith("EXPLODE,"))
                        {
                            float power;
                            if (float.TryParse(pfResult.Substring(8), out power))
                            {
                                _powerfailResult = PowerfailResult.Explode;
                                _powerfailExplosionPower = power;
                            }
                            else
                            {
                                KShared.Instance?.LogError(
                                    "Converter \"" + ConverterName + "\": Could not parse EXPLODE power \"" + pfResultRaw + "\" — defaulting to STOP.",
                                    moduleName + "/LoadSharedConfig");
                                _powerfailResult = PowerfailResult.Stop;
                            }
                        }
                        else
                        {
                            KShared.Instance?.LogError(
                                "Converter \"" + ConverterName + "\": Unknown powerfailResult \"" + pfResultRaw + "\" — defaulting to STOP.",
                                moduleName + "/LoadSharedConfig");
                            _powerfailResult = PowerfailResult.Stop;
                        }
                    }
                }
            }
            else if (pfResultRaw != null)
            {
                KShared.Instance?.LogError(
                    "Converter \"" + ConverterName + "\": powerfailResult set without powerfailResource — powerfailResult ignored.",
                    moduleName + "/LoadSharedConfig");
            }

            // manualOperation / manualRequiresStartup
            _manualOperation = false;
            _manualRequiresStartup = true;
            bool tmpB;
            if (bool.TryParse(moduleNode.GetValue("manualOperation"), out tmpB)) _manualOperation = tmpB;
            if (bool.TryParse(moduleNode.GetValue("manualRequiresStartup"), out tmpB)) _manualRequiresStartup = tmpB;

            // startStopShowRules / manualShowRules
            KShared.ParseShowRule(
                NullIfEmpty(moduleNode.GetValue("startStopShowRules")) ?? "PAW",
                out _startStopShowPAW, out _startStopShowEVA,
                "startStopShowRules", moduleName);

            KShared.ParseShowRule(
                NullIfEmpty(moduleNode.GetValue("manualShowRules")) ?? "PAW",
                out _manualShowPAW, out _manualShowEVA,
                "manualShowRules", moduleName);

            // maxInteractionDistance
            _maxInteractionDistance = 10f;
            float distTmp;
            if (float.TryParse(moduleNode.GetValue("maxInteractionDistance"), out distTmp))
                _maxInteractionDistance = distTmp;

            // recipeGroup
            _recipeGroup = NullIfEmpty(moduleNode.GetValue("recipeGroup"));

            KShared.Instance?.Log(
                string.Format("Converter \"{0}\" loaded: {1} inputs, {2} outputs, manual={3}, requiresStartup={4}, group={5}",
                    ConverterName, _inputs.Count, _outputs.Count,
                    _manualOperation, _manualRequiresStartup, _recipeGroup ?? "none"),
                moduleName + "/LoadSharedConfig");
        }

        // Charging helper
        /// <summary>
        /// Pulls the given resources from the vessel network. Returns true only if every
        /// resource was fully satisfied. Refunds all pulled resources if any fall short
        /// (all-or-nothing semantics).
        /// </summary>
        private bool ConsumeVesselResources(List<string> names, List<float> amounts, double dt)
        {
            if (names.Count == 0 || amounts.Count == 0) return true;
            if (names.Count != amounts.Count) return false;

            var pulled = new List<double>(names.Count);
            bool allSatisfied = true;

            for (int i = 0; i < names.Count; i++)
            {
                float rate = amounts[i];
                if (rate <= 0f) { pulled.Add(0.0); continue; }

                var def = PartResourceLibrary.Instance.GetDefinition(names[i]);
                if (def == null)
                {
                    KShared.Instance?.LogError("Unknown resource \"" + names[i] + "\" in consumption list.",
                        "KhemistryAdvancedStorage/ConsumeVesselResources");
                    pulled.Add(0.0);
                    allSatisfied = false;
                    continue;
                }

                double needed = rate * dt;
                double got = part.RequestResource(names[i], needed);
                pulled.Add(got);

                // Allow a small tolerance (0.1% of needed)
                if (got < needed * 0.999)
                    allSatisfied = false;
            }

            if (!allSatisfied)
            {
                // Refund everything that was pulled
                for (int i = 0; i < names.Count; i++)
                    if (pulled[i] > 0.0)
                        part.RequestResource(names[i], -pulled[i]);
                return false;
            }

            return true;
        }

        // ── UI updates ─────────────────────────────────────────────────────────────

        public void UpdateUI()
        {
            chargeDisplay = chargingRequired
                ? string.Format("Charge: {0:F1}%", chargePercent)
                : "Charge: N/A";

            if (state == ConverterState.On)
                stateDisplay = "State: Ready";
            else
                stateDisplay = "State: " + state.ToString();

            Events["EnableCharging"].active = chargingRequired && state != ConverterState.Charging && state != ConverterState.On;
            Events["DisableCharging"].active = chargingRequired && state == ConverterState.Charging;
            Events["TurnOnContainer"].active = state != ConverterState.On;
            Events["TurnOffContainer"].active = state == ConverterState.On;
        }

        // Charging
        public void HandleCharging(double dt)
        {
            if (!chargingRequired) return;

            if (state == ConverterState.Off)
            {
                // Container is off — charge decays passively
                if (chargeDecayRate > 0f)
                {
                    chargePercent -= chargeDecayRate * (float)dt;
                    if (chargePercent < 0f) chargePercent = 0f;
                }
                return;
            }

            if (state != ConverterState.Charging) return;

            // Already full — flip to On
            if (chargePercent >= 100f)
            {
                chargePercent = 100f;
                state = ConverterState.On;
                KShared.Instance?.Log("Container fully charged, now ON.",
                    "KhemistryAdvancedStorage/HandleCharging");
                return;
            }

            bool satisfied = ConsumeVesselResources(_chargeNames, _chargeAmounts, dt);
            if (satisfied)
            {
                chargePercent += chargeRate * (float)dt;
                if (chargePercent > 100f) chargePercent = 100f;
            }
            else
            {
                // Can't get charge resources — decay if configured
                if (chargeDecayRate > 0f)
                {
                    chargePercent -= chargeDecayRate * (float)dt;
                    if (chargePercent < 0f) chargePercent = 0f;
                }
            }
        }

        // ── Shared cycle logic ─────────────────────────────────────────────────────

        /// <summary>
        /// Runs one converter cycle against the given contextPart's vessel.
        /// For KhemistryAdvancedISRU this is always `this.part`.
        /// For KhemistryEVAAdvancedISRU (called from KhemistryKerbal) this is the
        /// Kerbal's part, so resources are drawn from/pushed to the Kerbal's vessel.
        /// </summary>
        public void RunOneCycle(Part contextPart, double dt)
        {
            IsCurrentlyActive = false;

            string conditionReason;
            if (!CheckConditions(contextPart.vessel, out conditionReason))
            {
                statusDisplay = "Inactive: " + conditionReason;
                return;
            }

            string blockedResource = CheckOutputSpace(contextPart.vessel, dt);
            if (blockedResource != null)
            {
                if (_outputWarnCooldown <= 0.0)
                {
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        string.Format("Converter \"{0}\": No output space for {1}, converter paused!",
                            _displayName, blockedResource),
                        5f, ScreenMessageStyle.UPPER_CENTER));
                    _outputWarnCooldown = 5.0;
                }
                statusDisplay = "Paused: " + blockedResource + " full";
                return;
            }

            bool powerfailShort = false;
            if (_powerfailResource != null)
            {
                double pfNeeded = GetInputRatio(_powerfailResource) * dt;
                double pfAvailable = GetVesselResourceAmount(contextPart.vessel, _powerfailResource);
                if (pfAvailable < pfNeeded * 0.999)
                    powerfailShort = true;
            }

            if (!ConsumeInputs(contextPart, dt))
            {
                if (powerfailShort)
                {
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        string.Format("Converter \"{0}\": Powerfailed due to lack of {1}!",
                            _displayName, _powerfailResource),
                        8f, ScreenMessageStyle.UPPER_CENTER));
                    TriggerPowerfail(contextPart);
                }
                else
                {
                    statusDisplay = "Insufficient resources";
                }
                return;
            }

            ProduceOutputs(contextPart, dt);
            statusDisplay = _manualOperation ? "Waiting for manual cycle" : "Running";
            IsCurrentlyActive = true;
        }

        // ── Condition checking ─────────────────────────────────────────────────────

        protected bool CheckConditions(Vessel v, out string reason)
        {
            reason = null;

            if (_planetCondition != null)
            {
                string currentBody = v.mainBody?.name ?? "";
                if (!currentBody.Equals(_planetCondition, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "wrong body (" + currentBody + ")";
                    return false;
                }

                if (_biomeCondition != null)
                {
                    string currentBiome = ScienceUtil.GetExperimentBiome(
                        v.mainBody, v.latitude, v.longitude);
                    if (!currentBiome.Equals(_biomeCondition, StringComparison.OrdinalIgnoreCase))
                    {
                        reason = "wrong biome (" + currentBiome + ")";
                        return false;
                    }
                }
            }

            double alt = v.altitude;
            if (_altMin != double.MinValue && alt < _altMin)
            {
                reason = string.Format("below min altitude ({0:F0} m)", _altMin);
                return false;
            }
            if (_altMax != double.MaxValue && alt > _altMax)
            {
                reason = string.Format("above max altitude ({0:F0} m)", _altMax);
                return false;
            }

            if (_situationCondition != SituationCondition.Any && !CheckSituation(v))
            {
                reason = "wrong situation (" + v.situation + ")";
                return false;
            }

            if (!KShared.Instance.surfaceDepositsAtPoint((float)v.latitude, (float)v.longitude, v.mainBody.name, 0).Contains(_depositCondition) && _depositCondition != null && _depositCondition != "")
            {
                reason = "not at deposit " + _depositCondition;
                return false;
            }

            return true;
        }

        protected bool CheckSituation(Vessel v)
        {
            Vessel.Situations sit = v.situation;
            CelestialBody body = v.mainBody;
            double alt = v.altitude;

            switch (_situationCondition)
            {
                case SituationCondition.Landed:
                    return sit == Vessel.Situations.LANDED || sit == Vessel.Situations.PRELAUNCH;
                case SituationCondition.Splashed:
                    return sit == Vessel.Situations.SPLASHED;
                case SituationCondition.FlyingLow:
                    return sit == Vessel.Situations.FLYING
                        && body != null && alt < body.scienceValues.flyingAltitudeThreshold;
                case SituationCondition.FlyingHigh:
                    return sit == Vessel.Situations.FLYING
                        && body != null && alt >= body.scienceValues.flyingAltitudeThreshold;
                case SituationCondition.SpaceLow:
                    return (sit == Vessel.Situations.ORBITING || sit == Vessel.Situations.SUB_ORBITAL)
                        && body != null && alt < body.scienceValues.spaceAltitudeThreshold;
                case SituationCondition.SpaceHigh:
                    return (sit == Vessel.Situations.ORBITING
                         || sit == Vessel.Situations.SUB_ORBITAL
                         || sit == Vessel.Situations.ESCAPING)
                        && body != null && alt >= body.scienceValues.spaceAltitudeThreshold;
                case SituationCondition.SubOrbital:
                    return sit == Vessel.Situations.SUB_ORBITAL;
                default:
                    return true;
            }
        }

        // ── Output space check ─────────────────────────────────────────────────────

        protected string CheckOutputSpace(Vessel v, double dt)
        {
            foreach (ResourceOutput output in _outputs)
            {
                if (output.dumpExcess) continue;
                double needed = output.ratio * dt;
                if (needed <= 0.0) continue;
                if (GetVesselResourceSpace(v, output.resourceName) < needed * 0.001)
                    return output.resourceName;
            }
            return null;
        }

        // ── Vessel resource helpers ────────────────────────────────────────────────

        protected double GetVesselResourceSpace(Vessel v, string resourceName)
        {
            double space = 0.0;
            foreach (Part p in v.parts)
                foreach (PartResource pr in p.Resources)
                    if (pr.resourceName == resourceName && pr.flowState)
                        space += pr.maxAmount - pr.amount;
            return space;
        }

        protected double GetVesselResourceAmount(Vessel v, string resourceName)
        {
            double total = 0.0;
            foreach (Part p in v.parts)
                foreach (PartResource pr in p.Resources)
                    if (pr.resourceName == resourceName && pr.flowState)
                        total += pr.amount;
            return total;
        }

        protected double GetInputRatio(string resourceName)
        {
            foreach (ResourceInput inp in _inputs)
                if (inp.resourceName.Equals(resourceName, StringComparison.OrdinalIgnoreCase))
                    return inp.ratio;
            return 0.0;
        }

        // ── Resource consumption / production ──────────────────────────────────────

        protected bool ConsumeInputs(Part contextPart, double dt)
        {
            var pulled = new List<(string name, ResourceFlowMode mode, double amount)>(_inputs.Count);
            bool allSatisfied = true;

            foreach (ResourceInput inp in _inputs)
            {
                if (inp.ratio <= 0.0) { pulled.Add((inp.resourceName, inp.flowMode, 0.0)); continue; }
                double needed = inp.ratio * dt;
                double got = contextPart.RequestResource(inp.resourceName, needed, inp.flowMode);
                pulled.Add((inp.resourceName, inp.flowMode, got));
                if (got < needed * 0.999) allSatisfied = false;
            }

            if (!allSatisfied)
            {
                foreach (var entry in pulled)
                    if (entry.amount > 0.0)
                        contextPart.RequestResource(entry.name, -entry.amount, entry.mode);
                return false;
            }
            return true;
        }

        protected void ProduceOutputs(Part contextPart, double dt)
        {
            foreach (ResourceOutput output in _outputs)
            {
                if (output.ratio <= 0.0) continue;
                contextPart.RequestResource(output.resourceName, -(output.ratio * dt), ResourceFlowMode.ALL_VESSEL);
            }
        }

        // ── Powerfail ──────────────────────────────────────────────────────────────

        protected void TriggerPowerfail(Part contextPart)
        {
            KShared.Instance?.Log(
                "Converter \"" + _displayName + "\" powerfailed. Result: " + _powerfailResult,
                "KhemistryAdvancedISRUBase/TriggerPowerfail");

            switch (_powerfailResult)
            {
                case PowerfailResult.None:
                    break;
                case PowerfailResult.Stop:
                    isRunning = false;
                    statusDisplay = "Stopped (powerfail)";
                    break;
                case PowerfailResult.Maint:
                    isRunning = false;
                    needsMaintenance = true;
                    statusDisplay = "Needs maintenance";
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Converter \"" + _displayName + "\": Requires maintenance by an Engineer.",
                        8f, ScreenMessageStyle.UPPER_CENTER));
                    break;
                case PowerfailResult.Explode:
                    // For EVA converters, explode the contextPart (the Kerbal) rather than
                    // the stored item (which has no live part to explode).
                    contextPart.explode();
                    break;
            }
        }

        // ── Recipe group check ─────────────────────────────────────────────────────

        /// <summary>
        /// Checks whether another converter in the same recipeGroup is already running
        /// on the given part. For EVA use, pass the Kerbal's part and this will check
        /// all EVA ISRU modules across all stored items in the Kerbal's inventory.
        /// </summary>
        public bool CheckRecipeGroup(Part contextPart)
        {
            if (_recipeGroup == null) return true;

            // Check same-part live modules (standard ISRU)
            foreach (PartModule pm in contextPart.Modules)
            {
                KhemistryAdvancedISRUBase other = pm as KhemistryAdvancedISRUBase;
                if (other == null || other == this) continue;
                if (other._recipeGroup != _recipeGroup) continue;
                if (other.isRunning)
                {
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Another converter in group " + _recipeGroup + " is already running!",
                        5f, ScreenMessageStyle.UPPER_CENTER));
                    return false;
                }
            }
            return true;
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        protected static string NullIfEmpty(string s)
            => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        public void TickCooldown(double dt)
        {
            _outputWarnCooldown = Math.Max(0.0, _outputWarnCooldown - dt);
        }

        // ── Abstract members subclasses must implement ─────────────────────────────

        protected abstract void LoadConfigFromPartInfo();
        protected abstract void UpdateEventVisibility();
    }

    // ── KhemistryAdvancedISRU (unchanged behaviour, now extends base) ──────────────

    public class KhemistryAdvancedISRU : KhemistryAdvancedISRUBase
    {
        // Used only when no ModuleAnimationGroup is present on the part, or it has
        // no activeAnimationName configured.
        [KSPField(isPersistant = false)]
        public string activeAnimationNameOverride = "";

        private Animation _activeAnim;
        private string _activeAnimationName;
        private bool _animationPlaying = false;

        // Animations
        private void SetupActiveAnimation()
        {
            ModuleAnimationGroup animGroup = part.FindModuleImplementing<ModuleAnimationGroup>();
            string animName = (animGroup != null && !string.IsNullOrEmpty(animGroup.activeAnimationName))
                ? animGroup.activeAnimationName
                : activeAnimationNameOverride;

            if (string.IsNullOrEmpty(animName))
            {
                _activeAnim = null;
                _activeAnimationName = null;
                return;
            }

            Animation[] animators = part.FindModelAnimators(animName);
            if (animators.Length == 0)
            {
                KShared.Instance?.LogError(
                    "Converter \"" + ConverterName + "\": No animator found for clip \"" + animName + "\".",
                    "KhemistryAdvancedISRU/SetupActiveAnimation");
                _activeAnim = null;
                _activeAnimationName = null;
                return;
            }

            _activeAnim = animators[0];
            _activeAnimationName = animName;
            _activeAnim[_activeAnimationName].wrapMode = _manualOperation ? WrapMode.Once : WrapMode.Loop;

            KShared.Instance?.Log(
                "Converter \"" + ConverterName + "\": Hooked active animation \"" + animName + "\""
                + (animGroup != null ? " (from ModuleAnimationGroup)." : " (from activeAnimationNameOverride)."),
                "KhemistryAdvancedISRU/SetupActiveAnimation");
        }

        private void SetActiveAnimationPlaying(bool playing)
        {
            if (_activeAnim == null || string.IsNullOrEmpty(_activeAnimationName)) return;
            if (playing == _animationPlaying) return;

            if (playing) _activeAnim.Play(_activeAnimationName);
            else _activeAnim.Stop(_activeAnimationName);

            _animationPlaying = playing;
        }

        private void PlayActiveAnimationOnce()
        {
            if (_activeAnim == null || string.IsNullOrEmpty(_activeAnimationName)) return;
            _activeAnim.Play(_activeAnimationName);
        }

        // Main code
        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            _fatalConfigError = false;
            _outputWarnCooldown = 0.0;

            LoadConfigFromPartInfo();

            if (_fatalConfigError)
            {
                foreach (BaseEvent e in Events) e.active = false;
                statusDisplay = "ERROR: see log";
                return;
            }

            _displayName = _recipeGroup != null
                ? ConverterName + " (" + _recipeGroup + ")"
                : ConverterName;

            string startLabel = _recipeGroup != null
                ? StartActionName + " (" + _recipeGroup + ")"
                : StartActionName;
            string stopLabel = _recipeGroup != null
                ? StopActionName + " (" + _recipeGroup + ")"
                : StopActionName;

            Events["StartConverter"].guiName = startLabel;
            Events["StopConverter"].guiName = stopLabel;
            Actions["StartConverterAction"].guiName = startLabel;
            Actions["StopConverterAction"].guiName = stopLabel;

            Events["StartConverter"].unfocusedRange = _maxInteractionDistance;
            Events["StopConverter"].unfocusedRange = _maxInteractionDistance;
            Events["ExecuteCycle"].unfocusedRange = _maxInteractionDistance;
            Events["PerformMaintenance"].unfocusedRange = _maxInteractionDistance;

            if (!chargingRequired)  // This avoids unchargable parts trying to require charging
                this.state = ConverterState.On;

            SetupActiveAnimation();

            UpdateEventVisibility();
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (vessel == null || part == null) return;
            if (_fatalConfigError) return;

            double dt = TimeWarp.fixedDeltaTime;
            _outputWarnCooldown = Math.Max(0.0, _outputWarnCooldown - dt);

            HandleCharging(dt);
            UpdateUI();

            if (_manualOperation)
            {
                statusDisplay = needsMaintenance ? "Needs maintenance"
                    : !isRunning ? "Stopped"
                    : "Waiting for manual cycle";
                UpdateEventVisibility();
                return;   // Manual converters animate via ExecuteCycle, not here.
            }

            if (!isRunning || needsMaintenance)
            {
                statusDisplay = needsMaintenance ? "Needs maintenance" : "Stopped";
                UpdateEventVisibility();
                SetActiveAnimationPlaying(false);
                return;
            }

            if (state != ConverterState.On)
            {
                statusDisplay = "Not ready";
                return;
            }

            RunOneCycle(part, dt);
            UpdateEventVisibility();
            SetActiveAnimationPlaying(isRunning);
        }

        // ── Events ─────────────────────────────────────────────────────────────────

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Start Converter",
                  groupName = "khemistryisru")]
        public void StartConverter()
        {
            if (needsMaintenance)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + _displayName + "\": Requires maintenance before starting.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            if (!CheckRecipeGroup(part)) return;
            if (state != ConverterState.On) return;
            isRunning = true;
            KShared.Instance?.Log("Converter \"" + _displayName + "\" started.", "KhemistryAdvancedISRU/StartConverter");
            UpdateEventVisibility();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Stop Converter",
                  groupName = "khemistryisru")]
        public void StopConverter()
        {
            isRunning = false;
            KShared.Instance?.Log("Converter \"" + _displayName + "\" stopped.", "KhemistryAdvancedISRU/StopConverter");
            UpdateEventVisibility();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Perform Maintenance",
                  groupName = "khemistryisru",
                  externalToEVAOnly = true, guiActiveUnfocused = false, unfocusedRange = 10f)]
        public void PerformMaintenance()
        {
            ProtoCrewMember kerbal = FlightGlobals.ActiveVessel?.GetVesselCrew()?.FirstOrDefault();
            if (kerbal == null || kerbal.trait != "Engineer")
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + _displayName + "\": Maintenance requires an Engineer.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            needsMaintenance = false;
            KShared.Instance?.Log("Converter \"" + _displayName + "\" maintained by " + kerbal.name + ".",
                "KhemistryAdvancedISRU/PerformMaintenance");
            ScreenMessages.PostScreenMessage(new ScreenMessage(
                "Converter \"" + _displayName + "\": Maintenance complete.", 5f, ScreenMessageStyle.UPPER_CENTER));
            UpdateEventVisibility();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Execute Cycle",
                  groupName = "khemistryisru")]
        public void ExecuteCycle()
        {
            if (needsMaintenance)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + _displayName + "\": Requires maintenance.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            if (!_manualRequiresStartup)
                if (!CheckRecipeGroup(part)) return;

            RunOneCycle(part, TimeWarp.fixedDeltaTime);
            UpdateEventVisibility();

            if (IsCurrentlyActive)
                PlayActiveAnimationOnce();
        }

        [KSPAction("Start Converter")]
        public void StartConverterAction(KSPActionParam param) => StartConverter();

        [KSPAction("Stop Converter")]
        public void StopConverterAction(KSPActionParam param) => StopConverter();

        // ── Config loading ─────────────────────────────────────────────────────────

        protected override void LoadConfigFromPartInfo()
        {
            KShared.Instance?.Log("Called!", "KhemistryAdvancedISRU/LoadConfigFromPartInfo");
            ConfigNode moduleNode = FindModuleConfigNode("KhemistryAdvancedISRU");
            if (moduleNode == null) { _fatalConfigError = true; return; }
            LoadSharedConfig(moduleNode, "KhemistryAdvancedISRU");
        }

        // ── Event visibility ───────────────────────────────────────────────────────

        protected override void UpdateEventVisibility()
        {
            bool startStopEnabled = !_manualOperation || _manualRequiresStartup;

            ApplyShowRule(Events["StartConverter"],
                showPAW: startStopEnabled && !isRunning && !needsMaintenance && _startStopShowPAW,
                showEVA: startStopEnabled && !isRunning && !needsMaintenance && _startStopShowEVA);

            ApplyShowRule(Events["StopConverter"],
                showPAW: startStopEnabled && isRunning && _startStopShowPAW,
                showEVA: startStopEnabled && isRunning && _startStopShowEVA);

            bool cycleEnabled = _manualOperation && !needsMaintenance && (!_manualRequiresStartup || isRunning);

            ApplyShowRule(Events["ExecuteCycle"],
                showPAW: cycleEnabled && _manualShowPAW,
                showEVA: cycleEnabled && _manualShowEVA);

            Events["PerformMaintenance"].active = needsMaintenance;
            Events["PerformMaintenance"].guiActiveUnfocused = needsMaintenance;
            Events["PerformMaintenance"].unfocusedRange = _maxInteractionDistance;
        }

        private static void ApplyShowRule(BaseEvent ev, bool showPAW, bool showEVA)
        {
            ev.guiActive = showPAW;
            ev.guiActiveUnfocused = showEVA;
            ev.externalToEVAOnly = showEVA;
            ev.active = showPAW || showEVA;
        }
    }

    // Uses KHEMISTRY_RECIPE recipes, see config at the top of source.
    //
    // The module's own MODULE config may define any field a normal KhemistryAdvancedISRU
    // would (conditions, powerfail, manual operation, show rules,
    // maxInteractionDistance, charging...). If present, that value overrides the same
    // field on every loaded recipe wholesale. ConverterName, StartActionName, and
    // StopActionName are NOT overridable — each recipe must define its own, since they
    // exist specifically to differentiate between recipes. Two further exceptions:
    //   - INPUT_RESOURCE / OUTPUT_RESOURCE nodes on the module are ADDED to whichever
    //     recipe is active (after the recipe's own ratios have been scaled by
    //     `multiplier`). If a module resource shares a ResourceName with one already
    //     present on the recipe, the ratios are summed and FlowMode/DumpExcess are
    //     taken from the module's entry.
    //   - CHARGE_CON_NAMES / CHARGE_CON_AMOUNTS entries from the recipe and the module
    //     are concatenated (recipe's first, then the module's). Duplicate names are
    //     skipped (first occurrence wins) with a logged warning.
    //
    // The module can also have RECIPES and RECIPE_MULTIPLIERS nodes to only load
    // specific recipes of type resourceType, as well as multiply the inputs and outputs
    // of each by something. Both are optional, however if RECIPE_MULTIPLIERS is
    // present, RECIPES must be too.
    //
    // Only one recipe runs at a time; recipeGroup is intentionally never read or
    // checked, since recipes belonging to this module can never run concurrently.
    public class KhemistryAdvancedRecipeISRU : KhemistryAdvancedISRUBase
    {
        [KSPField(isPersistant = false)]
        public string recipeType = null;

        [KSPField(isPersistant = false)]
        public float multiplier = 1f;

        [KSPField(isPersistant = true)]
        public string activeRecipeName = null;

        // List of allowed ConverterName of recipes and multipliers
        public List<string> allowedRecipes = new List<string>();
        public List<float> multiplierRecipes = new List<float>();

        // Used only when no ModuleAnimationGroup is present on the part, or it has
        // no activeAnimationName configured.
        [KSPField(isPersistant = false)]
        public string activeAnimationNameOverride = "";

        private Animation _activeAnim;
        private string _activeAnimationName;
        private bool _animationPlaying = false;

        private readonly List<KhemistryRecipe> _recipes = new List<KhemistryRecipe>();
        private KhemistryRecipe _activeRecipe = null;

        // ── Module-level overrides, captured in OnLoad ─────────────────────────────
        // null/None means "not specified on the module — fall back to the recipe's value".

        private string _ovPlanetCondition, _ovBiomeCondition, _ovDepositCondition;
        private string _ovPowerfailResource, _ovPowerfailResultRaw;
        private string _ovSituationConditionRaw;
        private string _ovStartStopShowRulesRaw, _ovManualShowRulesRaw;
        private double? _ovAltMin, _ovAltMax;
        private float? _ovMaxInteractionDistance;
        private bool? _ovManualOperation, _ovManualRequiresStartup, _ovChargingRequired;
        private float? _ovChargeRate, _ovChargeDecayRate;

        // The module's own extra resources/charge entries (always added, never an override)
        private readonly List<ResourceInput> _extraInputs = new List<ResourceInput>();
        private readonly List<ResourceOutput> _extraOutputs = new List<ResourceOutput>();
        private readonly List<string> _ownChargeNames = new List<string>();
        private readonly List<float> _ownChargeAmounts = new List<float>();

        // ── Config loading (own module config — read directly from OnLoad's node,
        //    same pattern as KhemistryFluidCell) ───────────────────────────────────

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            recipeType = KShared.getStrValueFromCFG(node, "recipeType", recipeType);
            multiplier = KShared.getFloatValueFromCFG(node, "multiplier", 1f);

            if (node.HasNode("RECIPES"))
            {
                if (!node.GetNode("RECIPES").HasValue("name"))
                    KShared.Instance?.LogError(
                            "KhemistryAdvancedRecipeISRU: Node RECIPES is present but no \"name\" values inside, skipping node.",
                            "KhemistryAdvancedRecipeISRU/OnLoad");
                else
                    allowedRecipes = node.GetNode("RECIPES").GetValues("name").ToList();
                if (node.HasNode("RECIPE_MULTIPLIERS"))
                {
                    if (!node.GetNode("RECIPE_MULTIPLIERS").HasValue("amount"))
                        KShared.Instance?.LogError(
                                "KhemistryAdvancedRecipeISRU: Node RECIPE_MULTIPLIERS is present but no \"amount\" values inside, skipping node.",
                                "KhemistryAdvancedRecipeISRU/OnLoad");
                    else
                    {
                        multiplierRecipes.Clear();
                        foreach(string recipe in node.GetNode("RECIPE_MULTIPLIERS").GetValues("amount"))
                            multiplierRecipes.Add(float.Parse(recipe));
                        if(allowedRecipes.Count != multiplierRecipes.Count)
                        {
                            KShared.Instance?.LogError(
                                "KhemistryAdvancedRecipeISRU: RECIPE and RECIPE_MULTIPLIERS nodes have unequal amounts of \"name\" and \"amount\" values respectively ("+allowedRecipes.Count.ToString()+", "+multiplierRecipes.Count.ToString()+"), reverting to skip those nodes.",
                                "KhemistryAdvancedRecipeISRU/OnLoad");
                            allowedRecipes.Clear();
                            multiplierRecipes.Clear();
                        }
                    }
                }
            }
            else if (node.HasNode("RECIPE_MULTIPLIERS"))
            {
                KShared.Instance?.LogError(
                            "KhemistryAdvancedRecipeISRU: Node RECIPE_MULTIPLIERS is present but no RECIPES node is present.",
                            "KhemistryAdvancedRecipeISRU/OnLoad");
            }

            _ovPlanetCondition = KShared.getStrValueFromCFG(node, "planetCondition", null);
            _ovBiomeCondition = KShared.getStrValueFromCFG(node, "biomeCondition", null);
            _ovDepositCondition = KShared.getStrValueFromCFG(node, "depositCondition", null);
            _ovPowerfailResource = KShared.getStrValueFromCFG(node, "powerfailResource", null);
            _ovPowerfailResultRaw = KShared.getStrValueFromCFG(node, "powerfailResult", null);
            _ovSituationConditionRaw = KShared.getStrValueFromCFG(node, "situationCondition", null);
            _ovStartStopShowRulesRaw = KShared.getStrValueFromCFG(node, "startStopShowRules", null);
            _ovManualShowRulesRaw = KShared.getStrValueFromCFG(node, "manualShowRules", null);

            _ovAltMin = node.HasValue("altitudeMinCondition")
                ? (double?)KShared.getFloatValueFromCFG(node, "altitudeMinCondition", 0f) : null;
            _ovAltMax = node.HasValue("altitudeMaxCondition")
                ? (double?)KShared.getFloatValueFromCFG(node, "altitudeMaxCondition", 0f) : null;
            _ovMaxInteractionDistance = node.HasValue("maxInteractionDistance")
                ? (float?)KShared.getFloatValueFromCFG(node, "maxInteractionDistance", 10f) : null;

            _ovManualOperation = ParseNullableBool(node, "manualOperation");
            _ovManualRequiresStartup = ParseNullableBool(node, "manualRequiresStartup");
            _ovChargingRequired = ParseNullableBool(node, "chargingRequired");

            _ovChargeRate = node.HasValue("chargeRate")
                ? (float?)KShared.getFloatValueFromCFG(node, "chargeRate", 0f) : null;
            _ovChargeDecayRate = node.HasValue("chargeDecayRate")
                ? (float?)KShared.getFloatValueFromCFG(node, "chargeDecayRate", 0f) : null;

            // Extra INPUT_RESOURCE / OUTPUT_RESOURCE — added to whichever recipe is active
            _extraInputs.Clear();
            foreach (ConfigNode inputNode in node.GetNodes("INPUT_RESOURCE"))
            {
                string resName = inputNode.GetValue("ResourceName");
                if (string.IsNullOrEmpty(resName)) continue;

                double ratio = 0.0;
                double.TryParse(inputNode.GetValue("Ratio"), out ratio);

                ResourceFlowMode flowMode = ResourceFlowMode.ALL_VESSEL;
                string flowStr = inputNode.GetValue("FlowMode");
                if (!string.IsNullOrEmpty(flowStr))
                {
                    ResourceFlowMode parsed;
                    if (Enum.TryParse(flowStr.Trim(), true, out parsed))
                        flowMode = parsed;
                    else
                        KShared.Instance?.LogError(
                            "KhemistryAdvancedRecipeISRU: Unknown FlowMode \"" + flowStr + "\" for " + resName + ", defaulting to ALL_VESSEL.",
                            "KhemistryAdvancedRecipeISRU/OnLoad");
                }

                _extraInputs.Add(new ResourceInput { resourceName = resName, ratio = ratio, flowMode = flowMode });
            }

            _extraOutputs.Clear();
            foreach (ConfigNode outputNode in node.GetNodes("OUTPUT_RESOURCE"))
            {
                string resName = outputNode.GetValue("ResourceName");
                if (string.IsNullOrEmpty(resName)) continue;

                double ratio = 0.0;
                double.TryParse(outputNode.GetValue("Ratio"), out ratio);

                bool dumpExcess = false;
                bool.TryParse(outputNode.GetValue("DumpExcess"), out dumpExcess);

                _extraOutputs.Add(new ResourceOutput { resourceName = resName, ratio = ratio, dumpExcess = dumpExcess });
            }

            // Extra CHARGE_CON_NAMES / CHARGE_CON_AMOUNTS — appended after the recipe's own
            _ownChargeNames.Clear();
            _ownChargeAmounts.Clear();
            if (node.HasNode("CHARGE_CON_NAMES"))
                foreach (string n in node.GetNode("CHARGE_CON_NAMES").GetValues("name"))
                    _ownChargeNames.Add(n.Trim());
            if (node.HasNode("CHARGE_CON_AMOUNTS"))
                foreach (string a in node.GetNode("CHARGE_CON_AMOUNTS").GetValues("amount"))
                { float tmp; if (float.TryParse(a, out tmp)) _ownChargeAmounts.Add(tmp); }
            if (_ownChargeNames.Count != _ownChargeAmounts.Count)
                KShared.Instance?.LogError(
                    "KhemistryAdvancedRecipeISRU: CHARGE_CON_NAMES and CHARGE_CON_AMOUNTS length mismatch.",
                    "KhemistryAdvancedRecipeISRU/OnLoad");
        }

        private static bool? ParseNullableBool(ConfigNode node, string key)
        {
            if (!node.HasValue(key)) return null;
            bool b;
            return bool.TryParse(node.GetValue(key), out b) ? (bool?)b : null;
        }

        // ── Recipe resolution (needs KShared.Instance.recipeDict, only safe in OnStart) ─

        protected override void LoadConfigFromPartInfo()
        {
            if (string.IsNullOrEmpty(recipeType))
            {
                KShared.Instance?.LogError(
                    "Part \"" + part.name + "\" has a KhemistryAdvancedRecipeISRU with no recipeType set!",
                    "KhemistryAdvancedRecipeISRU/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            var shared = KShared.Instance;
            List<KhemistryRecipe> recipeList;
            if (shared == null || !shared.recipeDict.TryGetValue(recipeType, out recipeList) || recipeList.Count == 0)
            {
                KShared.Instance?.LogError(
                    "No KHEMISTRY_RECIPE entries found for recipeType \"" + recipeType + "\"!",
                    "KhemistryAdvancedRecipeISRU/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            _recipes.Clear();

            // Add recipes
            if (allowedRecipes.Count == 0)
                _recipes.AddRange(recipeList);
            else
                foreach (KhemistryRecipe recipe in recipeList)
                    if (allowedRecipes.Contains(recipe.ConverterName))
                        _recipes.Add(recipe);

            // Multiply by per-recipe multiplier
            if (multiplierRecipes.Count > 0)  // Because of logic in OnLoad, this will be empty if allowedRecipes is empty
                foreach (KhemistryRecipe recipe in _recipes)
                {
                    if (allowedRecipes.Contains(recipe.ConverterName))
                    {
                        if (allowedRecipes.Count != multiplierRecipes.Count)
                        {
                            KShared.Instance?.Log(
                                "allowedRecipes amount is not equal to the multiplierRecipes amount ("+allowedRecipes.Count.ToString()+", "+multiplierRecipes.Count.ToString()+"), skipping recipe multiplication.",
                                "KhemistryAdvancedRecipeISRU/LoadConfigFromPartInfo");
                            break;
                        }

                        recipe._inputs = recipe._inputs
                            .Select(inp => new KhemistryRecipe.ResourceInput
                            {
                                resourceName = inp.resourceName,
                                ratio = inp.ratio * multiplierRecipes[allowedRecipes.IndexOf(recipe.ConverterName)],
                                flowMode = inp.flowMode
                            })
                            .ToList();
                    }
                    else
                        KShared.Instance?.Log(
                            recipe.ConverterName.ToString()+" was not found in allowedRecipes but is in multiplierRecipes, skipping this recipe.",
                            "KhemistryAdvancedRecipeISRU/LoadConfigFromPartInfo");
                }

            KhemistryRecipe initial = null;
            if (!string.IsNullOrEmpty(activeRecipeName))
            {
                foreach (KhemistryRecipe r in _recipes)
                    if (r.ConverterName == activeRecipeName) { initial = r; break; }
            }
            if (initial == null) initial = _recipes[0];

            ApplyRecipe(initial);
            activeRecipeName = _activeRecipe.ConverterName;

            KShared.Instance?.Log(
                "Loaded " + _recipes.Count + " recipe(s) for recipeType \"" + recipeType + "\", active: \"" + _activeRecipe.ConverterName + "\".",
                "KhemistryAdvancedRecipeISRU/LoadConfigFromPartInfo");
        }

        // ── Recipe application: merges a recipe's data with this module's overrides ──

        private void ApplyRecipe(KhemistryRecipe recipe)
        {
            _activeRecipe = recipe;

            ConverterName = recipe.ConverterName;
            StartActionName = recipe.StartActionName ?? ("Start " + ConverterName);
            StopActionName = recipe.StopActionName ?? ("Stop " + ConverterName);

            _planetCondition = _ovPlanetCondition ?? recipe._planetCondition;
            _biomeCondition = _ovBiomeCondition ?? recipe._biomeCondition;
            _altMin = _ovAltMin ?? recipe._altMin;
            _altMax = _ovAltMax ?? recipe._altMax;

            string sitRaw = _ovSituationConditionRaw ?? recipe._situationCondition.ToString();
            _situationCondition = SituationCondition.Any;
            if (sitRaw != null)
            {
                SituationCondition parsedSit;
                if (Enum.TryParse(sitRaw, true, out parsedSit))
                    _situationCondition = parsedSit;
                else
                    KShared.Instance?.LogError(
                        "Converter \"" + recipe.ConverterName + "\": Unknown situationCondition \"" + sitRaw + "\" — condition ignored.",
                        "KhemistryAdvancedRecipeISRU/ApplyRecipe");
            }

            _depositCondition = _ovDepositCondition ?? recipe._depositCondition;

            _manualOperation = _ovManualOperation ?? recipe._manualOperation;
            _manualRequiresStartup = _ovManualRequiresStartup ?? recipe._manualRequiresStartup;

            if (_ovStartStopShowRulesRaw != null)
                KShared.ParseShowRule(_ovStartStopShowRulesRaw, out _startStopShowPAW, out _startStopShowEVA,
                    "startStopShowRules", "KhemistryAdvancedRecipeISRU");
            else
            {
                _startStopShowPAW = recipe._startStopShowPAW;
                _startStopShowEVA = recipe._startStopShowEVA;
            }

            if (_ovManualShowRulesRaw != null)
                KShared.ParseShowRule(_ovManualShowRulesRaw, out _manualShowPAW, out _manualShowEVA,
                    "manualShowRules", "KhemistryAdvancedRecipeISRU");
            else
            {
                _manualShowPAW = recipe._manualShowPAW;
                _manualShowEVA = recipe._manualShowEVA;
            }

            _maxInteractionDistance = _ovMaxInteractionDistance ?? recipe._maxInteractionDistance;

            // recipeGroup is intentionally never read — only one recipe can ever run at a time
            _recipeGroup = null;

            chargingRequired = _ovChargingRequired ?? recipe.chargingRequired;
            chargeRate = _ovChargeRate ?? recipe.chargeRate;
            chargeDecayRate = _ovChargeDecayRate ?? recipe.chargeDecayRate;

            // CHARGE_CON_NAMES / CHARGE_CON_AMOUNTS: recipe's own first, then the module's own,
            // duplicates (by name) skipped with a warning.
            _chargeNames.Clear();
            _chargeAmounts.Clear();
            AddChargeEntries(recipe.ChargeNames, recipe.ChargeAmounts);
            AddChargeEntries(_ownChargeNames, _ownChargeAmounts);

            // INPUT_RESOURCE: recipe's own (scaled by multiplier) merged additively with the
            // module's own extras (ratio summed, FlowMode taken from the module's entry).
            _inputs.Clear();
            var workingInputs = new List<ResourceInput>();
            foreach (KhemistryRecipe.ResourceInput ri in recipe._inputs)
                workingInputs.Add(new ResourceInput { resourceName = ri.resourceName, ratio = ri.ratio * multiplier, flowMode = ri.flowMode });
            foreach (ResourceInput extra in _extraInputs)
            {
                int idx = workingInputs.FindIndex(w => w.resourceName.Equals(extra.resourceName, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    ResourceInput merged = workingInputs[idx];
                    merged.ratio += extra.ratio;
                    merged.flowMode = extra.flowMode;
                    workingInputs[idx] = merged;
                }
                else
                {
                    workingInputs.Add(extra);
                }
            }
            _inputs.AddRange(workingInputs);

            // OUTPUT_RESOURCE: same merge rule, DumpExcess taken from the module's entry.
            _outputs.Clear();
            var workingOutputs = new List<ResourceOutput>();
            foreach (KhemistryRecipe.ResourceOutput ro in recipe._outputs)
                workingOutputs.Add(new ResourceOutput { resourceName = ro.resourceName, ratio = ro.ratio * multiplier, dumpExcess = ro.dumpExcess });
            foreach (ResourceOutput extra in _extraOutputs)
            {
                int idx = workingOutputs.FindIndex(w => w.resourceName.Equals(extra.resourceName, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    ResourceOutput merged = workingOutputs[idx];
                    merged.ratio += extra.ratio;
                    merged.dumpExcess = extra.dumpExcess;
                    workingOutputs[idx] = merged;
                }
                else
                {
                    workingOutputs.Add(extra);
                }
            }
            _outputs.AddRange(workingOutputs);

            if (_inputs.Count == 0 && _outputs.Count == 0)
                KShared.Instance?.LogError(
                    "Converter \"" + ConverterName + "\" (recipe) has no INPUT_RESOURCE or OUTPUT_RESOURCE — it will do nothing.",
                    "KhemistryAdvancedRecipeISRU/ApplyRecipe");

            // Powerfail must be re-validated against the FINAL merged _inputs.
            string pfResFinal = _ovPowerfailResource ?? recipe._powerfailResource;
            string pfResultRawFinal = _ovPowerfailResultRaw ?? CanonicalPowerfailResultRaw(recipe);
            ResolvePowerfail(pfResFinal, pfResultRawFinal);

            _displayName = ConverterName;

            if (Events["StartConverter"] != null) Events["StartConverter"].guiName = StartActionName;
            if (Events["StopConverter"] != null) Events["StopConverter"].guiName = StopActionName;
            if (Actions["StartConverterAction"] != null) Actions["StartConverterAction"].guiName = StartActionName;
            if (Actions["StopConverterAction"] != null) Actions["StopConverterAction"].guiName = StopActionName;

            if (_activeAnim != null && !string.IsNullOrEmpty(_activeAnimationName))
                _activeAnim[_activeAnimationName].wrapMode = _manualOperation ? WrapMode.Once : WrapMode.Loop;
        }

        private void AddChargeEntries(List<string> names, List<float> amounts)
        {
            int count = Math.Min(names.Count, amounts.Count);
            for (int i = 0; i < count; i++)
            {
                string name = names[i].Trim();
                bool duplicate = false;
                foreach (string existing in _chargeNames)
                    if (existing.Equals(name, StringComparison.OrdinalIgnoreCase)) { duplicate = true; break; }

                if (duplicate)
                {
                    KShared.Instance?.LogError(
                        "Converter \"" + ConverterName + "\": Duplicate charge resource \"" + name + "\" ignored (already defined).",
                        "KhemistryAdvancedRecipeISRU/ApplyRecipe");
                    continue;
                }
                _chargeNames.Add(name);
                _chargeAmounts.Add(amounts[i]);
            }
        }

        private static string CanonicalPowerfailResultRaw(KhemistryRecipe recipe)
        {
            switch (recipe._powerfailResult)
            {
                case KhemistryRecipe.PowerfailResult.Stop: return "STOP";
                case KhemistryRecipe.PowerfailResult.Maint: return "MAINT";
                case KhemistryRecipe.PowerfailResult.Explode:
                    return "EXPLODE," + recipe._powerfailExplosionPower.ToString(System.Globalization.CultureInfo.InvariantCulture);
                default: return null;
            }
        }

        private void ResolvePowerfail(string pfRes, string pfResultRaw)
        {
            _powerfailResource = null;
            _powerfailResult = PowerfailResult.None;
            _powerfailExplosionPower = 0f;

            if (pfRes != null)
            {
                bool found = false;
                foreach (ResourceInput inp in _inputs)
                    if (inp.resourceName.Equals(pfRes, StringComparison.OrdinalIgnoreCase)) { found = true; break; }

                if (!found)
                {
                    KShared.Instance?.LogError(
                        "Converter \"" + ConverterName + "\": powerfailResource \"" + pfRes + "\" is not a defined INPUT_RESOURCE — powerfail disabled.",
                        "KhemistryAdvancedRecipeISRU/ResolvePowerfail");
                }
                else
                {
                    _powerfailResource = pfRes;
                    if (pfResultRaw != null)
                    {
                        string pfResult = pfResultRaw.Trim().Trim('"').ToUpper();
                        if (pfResult == "STOP")
                        {
                            _powerfailResult = PowerfailResult.Stop;
                        }
                        else if (pfResult == "MAINT")
                        {
                            _powerfailResult = PowerfailResult.Maint;
                        }
                        else if (pfResult.StartsWith("EXPLODE,"))
                        {
                            float power;
                            if (float.TryParse(pfResult.Substring(8), out power))
                            {
                                _powerfailResult = PowerfailResult.Explode;
                                _powerfailExplosionPower = power;
                            }
                            else
                            {
                                KShared.Instance?.LogError(
                                    "Converter \"" + ConverterName + "\": Could not parse EXPLODE power \"" + pfResultRaw + "\" — defaulting to STOP.",
                                    "KhemistryAdvancedRecipeISRU/ResolvePowerfail");
                                _powerfailResult = PowerfailResult.Stop;
                            }
                        }
                        else
                        {
                            KShared.Instance?.LogError(
                                "Converter \"" + ConverterName + "\": Unknown powerfailResult \"" + pfResultRaw + "\" — defaulting to STOP.",
                                "KhemistryAdvancedRecipeISRU/ResolvePowerfail");
                            _powerfailResult = PowerfailResult.Stop;
                        }
                    }
                }
            }
            else if (pfResultRaw != null)
            {
                KShared.Instance?.LogError(
                    "Converter \"" + ConverterName + "\": powerfailResult set without powerfailResource — powerfailResult ignored.",
                    "KhemistryAdvancedRecipeISRU/ResolvePowerfail");
            }
        }

        // ── Animation (mirrors KhemistryAdvancedISRU) ──────────────────────────────

        private void SetupActiveAnimation()
        {
            ModuleAnimationGroup animGroup = part.FindModuleImplementing<ModuleAnimationGroup>();
            string animName = (animGroup != null && !string.IsNullOrEmpty(animGroup.activeAnimationName))
                ? animGroup.activeAnimationName
                : activeAnimationNameOverride;

            if (string.IsNullOrEmpty(animName))
            {
                _activeAnim = null;
                _activeAnimationName = null;
                return;
            }

            Animation[] animators = part.FindModelAnimators(animName);
            if (animators.Length == 0)
            {
                KShared.Instance?.LogError(
                    "Converter \"" + ConverterName + "\": No animator found for clip \"" + animName + "\".",
                    "KhemistryAdvancedRecipeISRU/SetupActiveAnimation");
                _activeAnim = null;
                _activeAnimationName = null;
                return;
            }

            _activeAnim = animators[0];
            _activeAnimationName = animName;
            _activeAnim[_activeAnimationName].wrapMode = _manualOperation ? WrapMode.Once : WrapMode.Loop;

            KShared.Instance?.Log(
                "Converter \"" + ConverterName + "\": Hooked active animation \"" + animName + "\""
                + (animGroup != null ? " (from ModuleAnimationGroup)." : " (from activeAnimationNameOverride)."),
                "KhemistryAdvancedRecipeISRU/SetupActiveAnimation");
        }

        private void SetActiveAnimationPlaying(bool playing)
        {
            if (_activeAnim == null || string.IsNullOrEmpty(_activeAnimationName)) return;
            if (playing == _animationPlaying) return;

            if (playing) _activeAnim.Play(_activeAnimationName);
            else _activeAnim.Stop(_activeAnimationName);

            _animationPlaying = playing;
        }

        private void PlayActiveAnimationOnce()
        {
            if (_activeAnim == null || string.IsNullOrEmpty(_activeAnimationName)) return;
            _activeAnim.Play(_activeAnimationName);
        }

        // ── Lifecycle ───────────────────────────────────────────────────────────────

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            _fatalConfigError = false;
            _outputWarnCooldown = 0.0;

            LoadConfigFromPartInfo();

            if (_fatalConfigError)
            {
                foreach (BaseEvent e in Events) e.active = false;
                statusDisplay = "ERROR: see log";
                return;
            }

            Events["StartConverter"].unfocusedRange = _maxInteractionDistance;
            Events["StopConverter"].unfocusedRange = _maxInteractionDistance;
            Events["SwitchRecipe"].unfocusedRange = _maxInteractionDistance;
            Events["ExecuteCycle"].unfocusedRange = _maxInteractionDistance;
            Events["PerformMaintenance"].unfocusedRange = _maxInteractionDistance;

            if (!chargingRequired)  // Avoids unchargable parts trying to require charging
                this.state = ConverterState.On;

            SetupActiveAnimation();

            UpdateEventVisibility();
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (vessel == null || part == null) return;
            if (_fatalConfigError) return;

            double dt = TimeWarp.fixedDeltaTime;
            _outputWarnCooldown = Math.Max(0.0, _outputWarnCooldown - dt);

            HandleCharging(dt);
            UpdateUI();

            if (_manualOperation)
            {
                statusDisplay = needsMaintenance ? "Needs maintenance"
                    : !isRunning ? "Stopped"
                    : "Waiting for manual cycle";
                UpdateEventVisibility();
                return;   // Manual converters animate via ExecuteCycle, not here.
            }

            if (!isRunning || needsMaintenance)
            {
                statusDisplay = needsMaintenance ? "Needs maintenance" : "Stopped";
                UpdateEventVisibility();
                SetActiveAnimationPlaying(false);
                return;
            }

            if (state != ConverterState.On)
            {
                statusDisplay = "Not ready";
                return;
            }

            RunOneCycle(part, dt);
            UpdateEventVisibility();
            SetActiveAnimationPlaying(isRunning);
        }

        // ── Events ─────────────────────────────────────────────────────────────────

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Switch Recipe",
                  groupName = "khemistryisru")]
        public void SwitchRecipe()
        {
            if (isRunning)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + _displayName + "\": Stop the converter before switching recipes.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (_recipes.Count <= 1)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + _displayName + "\": No other recipes available to switch to.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            var shared = KShared.Instance;
            if (shared == null) return;

            var labels = new List<string>();
            foreach (KhemistryRecipe r in _recipes)
                labels.Add(r.ConverterName + (r == _activeRecipe ? " [Active]" : ""));

            shared.ShowSelector("Switch Recipe", labels, label =>
            {
                int idx = labels.IndexOf(label);
                if (idx < 0) return;
                if (_recipes[idx] == _activeRecipe) return;

                ApplyRecipe(_recipes[idx]);
                activeRecipeName = _activeRecipe.ConverterName;
                UpdateEventVisibility();
                UpdateUI();

                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Switched to recipe \"" + _displayName + "\".", 5f, ScreenMessageStyle.UPPER_CENTER));
                KShared.Instance?.Log("Switched active recipe to \"" + _displayName + "\".",
                    "KhemistryAdvancedRecipeISRU/SwitchRecipe");
            });
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Start Converter",
                  groupName = "khemistryisru")]
        public void StartConverter()
        {
            if (needsMaintenance)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + _displayName + "\": Requires maintenance before starting.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            if (state != ConverterState.On) return;
            isRunning = true;
            KShared.Instance?.Log("Converter \"" + _displayName + "\" started.", "KhemistryAdvancedRecipeISRU/StartConverter");
            UpdateEventVisibility();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Stop Converter",
                  groupName = "khemistryisru")]
        public void StopConverter()
        {
            isRunning = false;
            KShared.Instance?.Log("Converter \"" + _displayName + "\" stopped.", "KhemistryAdvancedRecipeISRU/StopConverter");
            UpdateEventVisibility();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Perform Maintenance",
                  groupName = "khemistryisru",
                  externalToEVAOnly = true, guiActiveUnfocused = false, unfocusedRange = 10f)]
        public void PerformMaintenance()
        {
            ProtoCrewMember kerbal = null;
            var crew = FlightGlobals.ActiveVessel?.GetVesselCrew();
            if (crew != null)
                foreach (ProtoCrewMember c in crew) { kerbal = c; break; }
            if (kerbal == null || kerbal.trait != "Engineer")
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + _displayName + "\": Maintenance requires an Engineer.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            needsMaintenance = false;
            KShared.Instance?.Log("Converter \"" + _displayName + "\" maintained by " + kerbal.name + ".",
                "KhemistryAdvancedRecipeISRU/PerformMaintenance");
            ScreenMessages.PostScreenMessage(new ScreenMessage(
                "Converter \"" + _displayName + "\": Maintenance complete.", 5f, ScreenMessageStyle.UPPER_CENTER));
            UpdateEventVisibility();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Execute Cycle",
                  groupName = "khemistryisru")]
        public void ExecuteCycle()
        {
            if (needsMaintenance)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + _displayName + "\": Requires maintenance.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            RunOneCycle(part, TimeWarp.fixedDeltaTime);
            UpdateEventVisibility();

            if (IsCurrentlyActive)
                PlayActiveAnimationOnce();
        }

        [KSPAction("Start Converter")]
        public void StartConverterAction(KSPActionParam param) => StartConverter();

        [KSPAction("Stop Converter")]
        public void StopConverterAction(KSPActionParam param) => StopConverter();

        // ── Event visibility ───────────────────────────────────────────────────────

        protected override void UpdateEventVisibility()
        {
            bool startStopEnabled = !_manualOperation || _manualRequiresStartup;

            ApplyShowRule(Events["StartConverter"],
                showPAW: startStopEnabled && !isRunning && !needsMaintenance && _startStopShowPAW,
                showEVA: startStopEnabled && !isRunning && !needsMaintenance && _startStopShowEVA);

            ApplyShowRule(Events["StopConverter"],
                showPAW: startStopEnabled && isRunning && _startStopShowPAW,
                showEVA: startStopEnabled && isRunning && _startStopShowEVA);

            // Switch Recipe is never hidden based on running state — attempting to use it
            // while running just refuses with a message instead.
            ApplyShowRule(Events["SwitchRecipe"], showPAW: _startStopShowPAW, showEVA: _startStopShowEVA);

            bool cycleEnabled = _manualOperation && !needsMaintenance && (!_manualRequiresStartup || isRunning);

            ApplyShowRule(Events["ExecuteCycle"],
                showPAW: cycleEnabled && _manualShowPAW,
                showEVA: cycleEnabled && _manualShowEVA);

            Events["PerformMaintenance"].active = needsMaintenance;
            Events["PerformMaintenance"].guiActiveUnfocused = needsMaintenance;
            Events["PerformMaintenance"].unfocusedRange = _maxInteractionDistance;
        }

        private static void ApplyShowRule(BaseEvent ev, bool showPAW, bool showEVA)
        {
            ev.guiActive = showPAW;
            ev.guiActiveUnfocused = showEVA;
            ev.externalToEVAOnly = showEVA;
            ev.active = showPAW || showEVA;
        }
    }

    // ── KhemistryEVAAdvancedISRU ───────────────────────────────────────────────────
    // Lives on an inventory part. Has no FixedUpdate or events of its own.
    // KhemistryKerbal reads config from the prefab and drives cycles via the base class.
    // ──────────────────────────────────────────────────────────────────────────────

    public class KhemistryEVAAdvancedISRU : KhemistryAdvancedISRUBase
    {
        // No KSPEvents, no FixedUpdate.
        // All interaction is routed through KhemistryKerbal.
        [KSPField(isPersistant = false)]
        public bool useSuitCell = false;

        public bool UseSuitCell => useSuitCell;

        public HashSet<string> SupportedResources = new HashSet<string>();

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            SupportedResources.Clear();
            if (node.HasNode("SUPPORTED_RESOURCES"))
            {
                foreach (string name in node.GetNode("SUPPORTED_RESOURCES").GetValues("name"))
                    SupportedResources.Add(name.Trim());
                KShared.Instance?.Log(
                    "Loaded " + SupportedResources.Count + " allowed resources.",
                    "KhemistryEVAAdvancedISRU/OnLoad");
            }
            else
            {
                // Not an error — kerbal-native modules don't need SUPPORTED_RESOURCES.
                KShared.Instance?.Log(
                    "Part \"" + part.name + "\" has KhemistryEVAAdvancedISRU with no SUPPORTED_RESOURCES node " +
                    "(OK for kerbal-native modules; inventory-item modules won't accept any resource).",
                    "KhemistryEVAAdvancedISRU/OnLoad");
            }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            // This module may OnStart on the prefab when the part is loaded into the
            // part database. We load config here so the prefab is ready for KhemistryKerbal
            // to read _inputs, _outputs, conditions, etc. without needing a live vessel.
            LoadConfigFromPartInfo();

            if (_fatalConfigError)
            {
                statusDisplay = "ERROR: see log";
                return;
            }

            _displayName = _recipeGroup != null
                ? ConverterName + " (" + _recipeGroup + ")"
                : ConverterName;
        }

        protected override void LoadConfigFromPartInfo()
        {
            KShared.Instance?.Log("Called!", "KhemistryEVAAdvancedISRU/LoadConfigFromPartInfo");
            ConfigNode moduleNode = FindModuleConfigNode("KhemistryEVAAdvancedISRU");
            if (moduleNode == null) { _fatalConfigError = true; return; }
            LoadSharedConfig(moduleNode, "KhemistryEVAAdvancedISRU");

            bool tmpB;
            if (bool.TryParse(moduleNode.GetValue("useSuitCell"), out tmpB))
                useSuitCell = tmpB;
        }

        // No event visibility to manage — KhemistryKerbal owns the UI for this module.
        protected override void UpdateEventVisibility() { }

        /// <summary>
        /// Called by KhemistryKerbal to load config from the prefab and expose it.
        /// Returns false if config failed to load.
        /// </summary>
        public bool IsConfigLoaded => !_fatalConfigError;

        /// <summary>
        /// Exposes the display name for KhemistryKerbal to use in selector labels.
        /// </summary>
        public string DisplayName => string.IsNullOrEmpty(_displayName) ? ConverterName : _displayName;

        /// <summary>
        /// Whether this converter supports manual operation (single-cycle button).
        /// </summary>
        public bool IsManual => _manualOperation;

        /// <summary>
        /// Whether manual mode requires a Start before Execute Cycle.
        /// </summary>
        public bool ManualRequiresStartup => _manualRequiresStartup;
    }

    // ── KhemistryKerbal (updated) ─────────────────────────────────────────────────
    // Additions: detects stored parts with KhemistryEVAAdvancedISRU on their prefab,
    // shows EVA events per converter, and drives their cycles.
    // ──────────────────────────────────────────────────────────────────────────────

    public class KhemistryKerbal : PartModule
    {
        // ── Suit cell persistent state ─────────────────────────────────────────────

        // Serialized as "ResA:1.5000|ResB:2.0000" — same format as KhemistryEVACombinedProcessor
        [KSPField(isPersistant = true)]
        public string suitCellResourcesData = "";

        // ── Suit cell config (loaded from MODULE node, non-persistent) ─────────────

        private float _suitCellMaxAmount = 0f;          // 0 = no suit cell configured
        private float _suitCellTransferDistance = 10f;
        private readonly HashSet<string> _suitCellAllowedResources = new HashSet<string>();

        // ── Inventory part-name whitelists ─────────────────────────────────────────

        private HashSet<string> FluidCellPartNames = new HashSet<string>();
        private HashSet<string> _evaISRUPartNames = new HashSet<string>();

        private ModuleInventoryPart _inventory;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Held Cells")]
        public string CellContentsDisplay = "No cells available";

        // ── Fluid-cell abstraction ─────────────────────────────────────────────────
        // Represents either the built-in suit cell or an inventory StoredPart cell.

        private struct FluidCellRef
        {
            public bool isSuit;
            public StoredPart stored;   // valid when !isSuit
        }

        // ── ISRU handle abstraction ────────────────────────────────────────────────
        // Represents either a live KhemistryEVAAdvancedISRU on the kerbal part
        // or an inventory-backed one accessed through a StoredPart snapshot.

        private struct ISRUHandle
        {
            public bool isLive;
            public KhemistryEVAAdvancedISRU liveModule; // valid when isLive
            public StoredPart stored;                    // valid when !isLive
            public KhemistryEVAAdvancedISRU prefab;     // valid when !isLive

            // Convenience: whichever module holds the config and live state
            public KhemistryEVAAdvancedISRU Config => isLive ? liveModule : prefab;
            public string ConverterName => Config.ConverterName;
            public string DisplayName => Config.DisplayName;
        }

        private Dictionary<string, double> GetSuitCellDict()
    => KhemistryEVACombinedProcessor.Deserialize(suitCellResourcesData);

        private void SetSuitCellFromDict(Dictionary<string, double> dict)
    => suitCellResourcesData = KhemistryEVACombinedProcessor.Serialize(dict);

        // ── Config loading ─────────────────────────────────────────────────────────

        private void LoadConfigFromPartInfo()
        {
            KShared.Instance?.Log("Called!", "KhemistryKerbal/LoadConfigFromPartInfo");
            FluidCellPartNames.Clear();
            _evaISRUPartNames.Clear();
            _suitCellMaxAmount = 0f;
            _suitCellTransferDistance = 10f;
            _suitCellAllowedResources.Clear();

            ConfigNode moduleNode = null;

            // Try partInfo first
            if (part.partInfo?.partConfig != null)
            {
                foreach (ConfigNode n in part.partInfo.partConfig.GetNodes("MODULE"))
                {
                    if (n.GetValue("name") == "KhemistryKerbal") { moduleNode = n; break; }
                }
            }

            // Fallback: search GameDatabase (required for kerbalEVA parts)
            if (moduleNode == null)
            {
                string targetPartName = part.partInfo?.name ?? part.name;
                foreach (ConfigNode partNode in GameDatabase.Instance.GetConfigNodes("PART"))
                {
                    string nodeName = partNode.GetValue("name") ?? "";
                    int slash = nodeName.LastIndexOf('/');
                    if (slash >= 0) nodeName = nodeName.Substring(slash + 1);
                    if (!nodeName.Equals(targetPartName, StringComparison.OrdinalIgnoreCase)) continue;

                    foreach (ConfigNode n in partNode.GetNodes("MODULE"))
                    {
                        if (n.GetValue("name") == "KhemistryKerbal") { moduleNode = n; break; }
                    }
                    if (moduleNode != null) break;
                }
            }

            if (moduleNode == null)
            {
                KShared.Instance?.LogError(
                    "Could not find KhemistryKerbal MODULE node for part \"" + part.name + "\".",
                    "KhemistryKerbal/LoadConfigFromPartInfo");
                return;
            }

            // FLUID_CELL_PARTS whitelist
            if (moduleNode.HasNode("FLUID_CELL_PARTS"))
                foreach (string name in moduleNode.GetNode("FLUID_CELL_PARTS").GetValues("name"))
                    FluidCellPartNames.Add(name.Trim());

            // EVA_ISRU_PARTS whitelist
            if (moduleNode.HasNode("EVA_ISRU_PARTS"))
                foreach (string name in moduleNode.GetNode("EVA_ISRU_PARTS").GetValues("name"))
                    _evaISRUPartNames.Add(name.Trim());

            // SUIT_CELL (optional)
            if (moduleNode.HasNode("SUIT_CELL"))
            {
                ConfigNode suitNode = moduleNode.GetNode("SUIT_CELL");
                float tmp;
                if (float.TryParse(suitNode.GetValue("maxAmount"), out tmp))
                    _suitCellMaxAmount = tmp;
                if (float.TryParse(suitNode.GetValue("transferDistance"), out tmp))
                    _suitCellTransferDistance = tmp;
                if (suitNode.HasNode("ALLOWED_RESOURCES"))
                    foreach (string n in suitNode.GetNode("ALLOWED_RESOURCES").GetValues("name"))
                        _suitCellAllowedResources.Add(n.Trim());
            }

            KShared.Instance?.Log(
                string.Format("Loaded {0} fluid cell part names, {1} EVA ISRU part names, suitCell={2}.",
                    FluidCellPartNames.Count, _evaISRUPartNames.Count, _suitCellMaxAmount > 0f),
                "KhemistryKerbal/LoadConfigFromPartInfo");
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────────

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            var allHandlers = part.FindModulesImplementing<KhemistryKerbal>();
            if (allHandlers.Count > 1 && allHandlers[0] != this)
            {
                KShared.Instance?.Log("Duplicate handler found, removing self.", "KhemistryKerbal/OnStart");
                return;
            }

            LoadConfigFromPartInfo();

            _inventory = part.FindModuleImplementing<ModuleInventoryPart>();
            if (_inventory == null)
                KShared.Instance?.LogError("No ModuleInventoryPart on Kerbal.", "KhemistryKerbal/OnStart");
            else
                KShared.Instance?.Log("Inventory found.", "KhemistryKerbal/OnStart");

            KShared.Instance?.Log("OnStart complete!", "KhemistryKerbal/OnStart");
        }

        public override void OnUpdate()
        {
            UpdateFluidCellDisplay();
        }

        // ── Cell helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all fluid cell references: suit cell first (if configured),
        /// then inventory cells in order.
        /// </summary>
        private List<FluidCellRef> GetAllCellRefs()
        {
            var result = new List<FluidCellRef>();
            if (_suitCellMaxAmount > 0f)
                result.Add(new FluidCellRef { isSuit = true });
            foreach (StoredPart stored in GetHeldCellSnapshots())
                result.Add(new FluidCellRef { isSuit = false, stored = stored });
            return result;
        }

        // "Cell 0 (suit)" for the suit cell; "Cell N" for inventory cells (index = position in GetAllCellRefs list)
        private string GetCellLabel(FluidCellRef cell, int index)
            => cell.isSuit ? "Cell 0 (suit)" : string.Format("Cell {0}", index);

        private string ReadCellResourceName(FluidCellRef cell)
        {
            if (cell.isSuit)
            {
                var dict = GetSuitCellDict();
                if (dict.Count == 0) return "";
                var names = new List<string>();
                foreach (var kvp in dict) names.Add(kvp.Key);
                return string.Join(", ", names.ToArray());
            }
            return ReadResourceName(cell.stored);
        }

        private float ReadCellResourceAmount(FluidCellRef cell)
        {
            if (cell.isSuit)
                return (float)KhemistryEVACombinedProcessor.GetTotal(GetSuitCellDict());
            return ReadResourceAmount(cell.stored);
        }

        private float ReadCellMaxAmount(FluidCellRef cell)
            => cell.isSuit ? _suitCellMaxAmount : ReadMaxAmount(cell.stored.partName);

        // ── Fluid cell display ─────────────────────────────────────────────────────

        private void UpdateFluidCellDisplay()
        {
            var cells = GetAllCellRefs();
            if (cells.Count == 0) { CellContentsDisplay = "No cells available"; return; }
            var parts = new List<string>();
            for (int i = 0; i < cells.Count; i++)
            {
                string label = GetCellLabel(cells[i], i);
                if (cells[i].isSuit)
                {
                    var dict = GetSuitCellDict();
                    double total = KhemistryEVACombinedProcessor.GetTotal(dict);
                    if (dict.Count == 0)
                        parts.Add(string.Format("{0}: Empty (0/{1:F2})", label, _suitCellMaxAmount));
                    else
                    {
                        var cp = new List<string>();
                        foreach (var kvp in dict)
                            cp.Add(string.Format("{0}: {1:F2}", kvp.Key, kvp.Value));
                        parts.Add(string.Format("{0}: {1} ({2:F2}/{3:F2})",
                            label, string.Join(", ", cp.ToArray()), total, _suitCellMaxAmount));
                    }
                }
                else
                {
                    string resName = ReadResourceName(cells[i].stored);
                    float resAmount = ReadResourceAmount(cells[i].stored);
                    float maxAmount = ReadMaxAmount(cells[i].stored.partName);
                    parts.Add(string.IsNullOrEmpty(resName)
                        ? string.Format("{0}: Empty", label)
                        : string.Format("{0}: {1} {2:F1}/{3:F1} kg", label, resName, resAmount, maxAmount));
                }
            }
            CellContentsDisplay = string.Join("  |  ", parts.ToArray());
        }

        // ── ISRU handle helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Returns all ISRU handles: live kerbal-part modules first, then
        /// inventory-backed ones.
        /// </summary>
        private List<ISRUHandle> GetAllISRUHandles()
        {
            var result = new List<ISRUHandle>();

            // Live modules directly on the kerbal part
            foreach (KhemistryEVAAdvancedISRU m in part.FindModulesImplementing<KhemistryEVAAdvancedISRU>())
            {
                if (!m.IsConfigLoaded) continue;
                result.Add(new ISRUHandle { isLive = true, liveModule = m });
            }

            // Inventory-backed modules
            foreach (StoredPart stored in GetEVAISRUSnapshots())
                foreach (KhemistryEVAAdvancedISRU prefab in GetPrefabISRUModules(stored))
                    if (prefab.IsConfigLoaded)
                        result.Add(new ISRUHandle { isLive = false, stored = stored, prefab = prefab });

            return result;
        }

        /// <summary>Reads isRunning / needsMaintenance from either live state or snapshot.</summary>
        private bool ReadISRUBool(ISRUHandle h, string key)
        {
            if (h.isLive)
            {
                if (key == "isRunning") return h.liveModule.isRunning;
                if (key == "needsMaintenance") return h.liveModule.needsMaintenance;
                return false;
            }
            return ReadISRUBool(h.stored, h.Config.ConverterName, key);
        }

        /// <summary>Writes isRunning / needsMaintenance to either live state or snapshot.</summary>
        private void WriteISRUBool(ISRUHandle h, string key, bool value)
        {
            if (h.isLive)
            {
                if (key == "isRunning") h.liveModule.isRunning = value;
                else if (key == "needsMaintenance") h.liveModule.needsMaintenance = value;
                return;
            }
            WriteISRUBool(h.stored, h.Config.ConverterName, key, value);
        }

        // ── EVA ISRU: snapshot discovery ──────────────────────────────────────────

        private List<StoredPart> GetEVAISRUSnapshots()
        {
            var result = new List<StoredPart>();
            if (_inventory == null) return result;

            for (int i = 0; i < _inventory.storedParts.Count; i++)
            {
                StoredPart stored = _inventory.storedParts.At(i);
                if (_evaISRUPartNames.Count > 0 && !_evaISRUPartNames.Contains(stored.partName))
                    continue;
                AvailablePart ap = PartLoader.getPartInfoByName(stored.partName);
                if (ap == null) continue;
                if (ap.partPrefab.FindModuleImplementing<KhemistryEVAAdvancedISRU>() == null) continue;
                result.Add(stored);
            }
            return result;
        }

        private List<KhemistryEVAAdvancedISRU> GetPrefabISRUModules(StoredPart stored)
        {
            AvailablePart ap = PartLoader.getPartInfoByName(stored.partName);
            return ap?.partPrefab.FindModulesImplementing<KhemistryEVAAdvancedISRU>()
                ?? new List<KhemistryEVAAdvancedISRU>();
        }

        private ProtoPartModuleSnapshot GetISRUSnapshot(StoredPart stored, string converterName)
        {
            if (stored.snapshot == null) return null;
            foreach (ProtoPartModuleSnapshot moduleSnap in stored.snapshot.modules)
            {
                if (moduleSnap.moduleName != "KhemistryEVAAdvancedISRU") continue;
                if (moduleSnap.moduleValues.GetValue("ConverterName") == converterName)
                    return moduleSnap;
            }
            return null;
        }

        private bool ReadISRUBool(StoredPart stored, string converterName, string key)
        {
            string val = GetISRUSnapshot(stored, converterName)?.moduleValues.GetValue(key);
            bool result;
            return val != null && bool.TryParse(val, out result) && result;
        }

        private void WriteISRUBool(StoredPart stored, string converterName, string key, bool value)
            => GetISRUSnapshot(stored, converterName)?.moduleValues.SetValue(key, value.ToString());

        // ── EVA ISRU: events ──────────────────────────────────────────────────────

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Use Held Converter",
                  groupName = "evaisru", groupDisplayName = "EVA Converters", groupStartCollapsed = false,
                  externalToEVAOnly = true, guiActiveUnfocused = false, unfocusedRange = 10f)]
        public void EVAUseConverter()
        {
            var shared = KShared.Instance;
            if (shared == null) return;
            KShared.Instance.Log("Called! (Use Held Converter button)", "KhemistryKerbal/EVAUseConverter");

            var options = GetAllISRUHandles();

            if (options.Count == 0)
            {
                KShared.Instance.Log("No EVA converters were found.", "KhemistryKerbal/EVAUseConverter");
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No EVA converters available.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            var labels = new List<string>();
            foreach (ISRUHandle h in options)
            {
                bool running = ReadISRUBool(h, "isRunning");
                bool maint = ReadISRUBool(h, "needsMaintenance");
                string suffix = maint ? " [MAINT]" : running ? " [Running]" : " [Stopped]";
                labels.Add(h.DisplayName + suffix);
            }

            if (options.Count == 1)
                ShowConverterActionMenu(options[0]);
            else
                shared.ShowSelector("Select converter", labels, label =>
                {
                    int idx = labels.IndexOf(label);
                    if (idx >= 0) ShowConverterActionMenu(options[idx]);
                });
        }

        private void ShowConverterActionMenu(ISRUHandle handle)
        {
            var shared = KShared.Instance;
            bool running = ReadISRUBool(handle, "isRunning");
            bool maint = ReadISRUBool(handle, "needsMaintenance");

            var actions = new List<string>();

            if (maint)
            {
                actions.Add("Perform Maintenance");
            }
            else
            {
                bool startStopEnabled = !handle.Config.IsManual || handle.Config.ManualRequiresStartup;
                if (startStopEnabled)
                {
                    if (!running) actions.Add("Start");
                    else actions.Add("Stop");
                }
                if (handle.Config.IsManual && (!handle.Config.ManualRequiresStartup || running))
                    actions.Add("Execute Cycle");
            }

            if (actions.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No actions available for \"" + handle.DisplayName + "\".",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (actions.Count == 1)
            {
                ExecuteConverterAction(handle, actions[0]);
                return;
            }

            shared.ShowSelector("Action: " + handle.DisplayName, actions,
                action => ExecuteConverterAction(handle, action));
        }

        private void ExecuteConverterAction(ISRUHandle handle, string action)
        {
            switch (action)
            {
                case "Start":
                    if (!handle.Config.CheckRecipeGroup(part)) return;
                    WriteISRUBool(handle, "isRunning", true);
                    KShared.Instance?.Log("EVA converter \"" + handle.DisplayName + "\" started.",
                        "KhemistryKerbal/ExecuteConverterAction");
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Converter \"" + handle.DisplayName + "\" started.", 4f, ScreenMessageStyle.UPPER_CENTER));
                    break;

                case "Stop":
                    WriteISRUBool(handle, "isRunning", false);
                    KShared.Instance?.Log("EVA converter \"" + handle.DisplayName + "\" stopped.",
                        "KhemistryKerbal/ExecuteConverterAction");
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Converter \"" + handle.DisplayName + "\" stopped.", 4f, ScreenMessageStyle.UPPER_CENTER));
                    break;

                case "Execute Cycle":
                    if (!handle.Config.ManualRequiresStartup && !handle.Config.CheckRecipeGroup(part)) return;
                    if (handle.Config.UseSuitCell)
                    {
                        if (_suitCellMaxAmount > 0f)
                        {
                            var suitDict = GetSuitCellDict();
                            handle.Config.RunOneCycleSuitCell(part, suitDict, _suitCellMaxAmount, TimeWarp.fixedDeltaTime);
                            SetSuitCellFromDict(suitDict);
                        }
                        else
                        {
                            ScreenMessages.PostScreenMessage(new ScreenMessage(
                                "No suit cell configured for this kerbal.", 5f, ScreenMessageStyle.UPPER_CENTER));
                        }
                    }
                    else
                    {
                        handle.Config.RunOneCycle(part, TimeWarp.fixedDeltaTime);
                    }
                    KShared.Instance?.Log("EVA converter \"" + handle.DisplayName + "\" cycle executed.",
                        "KhemistryKerbal/ExecuteConverterAction");
                    break;

                case "Perform Maintenance":
                    ProtoCrewMember kerbal = FlightGlobals.ActiveVessel?.GetVesselCrew()?.FirstOrDefault();
                    if (kerbal == null || kerbal.trait != "Engineer")
                    {
                        ScreenMessages.PostScreenMessage(new ScreenMessage(
                            "Maintenance requires an Engineer.", 5f, ScreenMessageStyle.UPPER_CENTER));
                        return;
                    }
                    WriteISRUBool(handle, "needsMaintenance", false);
                    KShared.Instance?.Log("EVA converter \"" + handle.DisplayName + "\" maintained.",
                        "KhemistryKerbal/ExecuteConverterAction");
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Converter \"" + handle.DisplayName + "\": Maintenance complete.",
                        5f, ScreenMessageStyle.UPPER_CENTER));
                    break;
            }
        }

        // ── FixedUpdate ────────────────────────────────────────────────────────────

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (vessel == null || part == null) return;

            double dt = TimeWarp.fixedDeltaTime;

            // ── Live kerbal-part ISRU modules ──────────────────────────────────────────
            foreach (KhemistryEVAAdvancedISRU liveISRU in part.FindModulesImplementing<KhemistryEVAAdvancedISRU>())
            {
                if (!liveISRU.IsConfigLoaded) continue;
                liveISRU.TickCooldown(dt);          // Always tick — even for manual converters
                if (liveISRU.IsManual) continue;
                if (!liveISRU.isRunning || liveISRU.needsMaintenance) continue;

                if (liveISRU.UseSuitCell)
                {
                    if (_suitCellMaxAmount <= 0f) continue;
                    var suitDict = GetSuitCellDict();
                    liveISRU.RunOneCycleSuitCell(part, suitDict, _suitCellMaxAmount, dt);
                    SetSuitCellFromDict(suitDict);
                }
                else
                {
                    liveISRU.RunOneCycle(part, dt);
                }
            }

            // ── Inventory ISRU modules ─────────────────────────────────────────────────
            foreach (StoredPart stored in GetEVAISRUSnapshots())
            {
                foreach (KhemistryEVAAdvancedISRU prefab in GetPrefabISRUModules(stored))
                {
                    if (!prefab.IsConfigLoaded) continue;
                    prefab.TickCooldown(dt);        // Always tick
                    if (prefab.IsManual) continue;

                    bool running = ReadISRUBool(stored, prefab.ConverterName, "isRunning");
                    bool maint = ReadISRUBool(stored, prefab.ConverterName, "needsMaintenance");
                    if (!running || maint) continue;

                    prefab.isRunning = running;
                    prefab.needsMaintenance = maint;

                    if (prefab.UseSuitCell)
                    {
                        if (_suitCellMaxAmount > 0f)
                        {
                            var suitDict = GetSuitCellDict();
                            prefab.RunOneCycleSuitCell(part, suitDict, _suitCellMaxAmount, dt);
                            SetSuitCellFromDict(suitDict);
                        }
                    }
                    else
                    {
                        prefab.RunOneCycle(part, dt);
                    }

                    WriteISRUBool(stored, prefab.ConverterName, "isRunning", prefab.isRunning);
                    WriteISRUBool(stored, prefab.ConverterName, "needsMaintenance", prefab.needsMaintenance);
                }
            }

            // ── Processor modules ──────────────────────────────────────────────────
            foreach (StoredPart stored in GetProcessorSnapshots())
            {
                KhemistryEVACombinedProcessor prefab = GetPrefabProcessor(stored);
                if (prefab == null || !prefab.IsConfigLoaded) continue;

                bool running = ReadProcessorBool(stored, "isRunning");
                string converterName = ReadProcessorField(stored, "activeConverterName");
                if (!running || string.IsNullOrEmpty(converterName)) continue;

                var resources = DeserializeProcessorResources(stored);
                bool cycled = prefab.RunConversionCycle(resources, converterName, dt);
                WriteProcessorResources(stored, resources);

                if (!cycled)
                {
                    WriteProcessorField(stored, "isRunning", "False");
                    KShared.Instance?.Log(
                        "Processor converter \"" + converterName + "\" stopped: insufficient inputs.",
                        "KhemistryKerbal/FixedUpdate");
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Converter \"" + converterName + "\" stopped: insufficient inputs.",
                        5f, ScreenMessageStyle.UPPER_CENTER));
                }
            }
        }

        // ── Fluid cell snapshot helpers ────────────────────────────────────────────

        private List<StoredPart> GetHeldCellSnapshots()
        {
            var result = new List<StoredPart>();
            if (_inventory == null) return result;
            for (int i = 0; i < _inventory.storedParts.Count; i++)
            {
                StoredPart stored = _inventory.storedParts.At(i);
                if (FluidCellPartNames.Contains(stored.partName))
                    result.Add(stored);
            }
            return result;
        }

        private ProtoPartModuleSnapshot GetCellModuleSnapshot(StoredPart stored)
        {
            if (stored.snapshot == null) return null;
            foreach (ProtoPartModuleSnapshot moduleSnap in stored.snapshot.modules)
                if (moduleSnap.moduleName == "KhemistryFluidCell") return moduleSnap;
            return null;
        }

        private string ReadResourceName(StoredPart stored)
            => GetCellModuleSnapshot(stored)?.moduleValues.GetValue("ResourceName") ?? "";

        private float ReadResourceAmount(StoredPart stored)
        {
            string val = GetCellModuleSnapshot(stored)?.moduleValues.GetValue("ResourceAmount");
            return val != null ? float.Parse(val) : 0f;
        }

        private float ReadMaxAmount(string partName)
            => PartLoader.getPartInfoByName(partName)?.partPrefab
                .FindModuleImplementing<KhemistryFluidCell>()?.ResourceMaxAmount ?? 100f;

        private float ReadTransferDistance(string partName)
            => PartLoader.getPartInfoByName(partName)?.partPrefab
                .FindModuleImplementing<KhemistryFluidCell>()?.TransferDistance ?? 10f;

        private HashSet<string> ReadAllowedResources(string partName)
            => PartLoader.getPartInfoByName(partName)?.partPrefab
                .FindModuleImplementing<KhemistryFluidCell>()?.AllowedResources
                ?? new HashSet<string>();

        private void WriteResourceName(StoredPart stored, string name)
            => GetCellModuleSnapshot(stored)?.moduleValues.SetValue("ResourceName", name);

        private void WriteResourceAmount(StoredPart stored, float amount)
            => GetCellModuleSnapshot(stored)?.moduleValues.SetValue("ResourceAmount", amount.ToString("F4"));

        private List<Part> GetPartsInRange(float range)
        {
            KShared.Instance?.Log("Called with range " + range.ToString(), "KhemistryKerbal/GetPartsInRange");
            var result = new List<Part>();
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
                foreach (Part p in v.parts)
                {
                    if (p == this.part) continue;
                    if (Vector3.Distance(this.part.transform.position, p.transform.position) <= range)
                        result.Add(p);
                }
            KShared.Instance?.Log("Acquired " + result.Count.ToString() + " parts.", "KhemistryKerbal/GetPartsInRange");
            return result;
        }

        // ── Fluid cell events ──────────────────────────────────────────────────────

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Transfer from cell to nearby part",
         groupName = "fluidcelleva", groupDisplayName = "Fluid Cells", groupStartCollapsed = false)]
        public void EVASendResources()
        {
            var shared = KShared.Instance;
            if (shared == null) { Debug.LogError("Khemistry: KShared null in EVASendResources!"); return; }
            KShared.Instance.Log("Called! (Transfer from ... to nearby part button)", "KhemistryKerbal/EVASendResources");

            var cells = GetAllCellRefs();
            if (cells.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No fluid cells available.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (cells.Count == 1)
            {
                ShowPartSelectorForSend(cells[0]);
            }
            else
            {
                var labels = new List<string>();
                for (int i = 0; i < cells.Count; i++)
                {
                    string cellLabel = GetCellLabel(cells[i], i);
                    if (cells[i].isSuit)
                    {
                        var dict = GetSuitCellDict();
                        double total = KhemistryEVACombinedProcessor.GetTotal(dict);
                        if (dict.Count == 0)
                            labels.Add(string.Format("{0}: Empty (0/{1:F2})", cellLabel, _suitCellMaxAmount));
                        else
                        {
                            var cp = new List<string>();
                            foreach (var kvp in dict)
                                cp.Add(string.Format("{0}: {1:F2}", kvp.Key, kvp.Value));
                            labels.Add(string.Format("{0}: {1} ({2:F2}/{3:F2})", cellLabel,
                                string.Join(", ", cp.ToArray()), total, _suitCellMaxAmount));
                        }
                    }
                    else
                    {
                        string resName = ReadResourceName(cells[i].stored);
                        float resAmount = ReadResourceAmount(cells[i].stored);
                        float maxAmount = ReadMaxAmount(cells[i].stored.partName);
                        labels.Add(string.IsNullOrEmpty(resName)
                            ? string.Format("{0}: Empty", cellLabel)
                            : string.Format("{0}: {1} {2:F1}/{3:F1} kg", cellLabel, resName, resAmount, maxAmount));
                    }
                }
                shared.ShowSelector("Which cell to send from?", labels, label =>
                {
                    int index = labels.IndexOf(label);
                    if (index >= 0) ShowPartSelectorForSend(cells[index]);
                });
            }
        }

        private void ShowPartSelectorForSend(FluidCellRef cell)
        {
            if (cell.isSuit) { ShowSuitCellPartSelectorForSend(); return; }

            string resourceName = ReadResourceName(cell.stored);
            float resourceAmount = ReadResourceAmount(cell.stored);
            float range = ReadTransferDistance(cell.stored.partName);

            if (string.IsNullOrEmpty(resourceName) || resourceAmount <= 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "That cell is empty.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            var targetParts = new Dictionary<string, Part>();
            foreach (Part p in GetPartsInRange(range))
                foreach (PartResource pr in p.Resources)
                {
                    if (pr.resourceName != resourceName) continue;
                    if (pr.amount >= pr.maxAmount) continue;
                    string lbl = string.Format("{0} / {1}  (space: {2:F1} kg)",
                        p.vessel.vesselName, p.partInfo.title, pr.maxAmount - pr.amount);
                    if (!targetParts.ContainsKey(lbl)) targetParts.Add(lbl, p);
                    break;
                }

            if (targetParts.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No nearby parts can accept " + resourceName + ".", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            KShared.Instance.ShowSelector("Send " + resourceName + " to...",
                targetParts.Keys.ToList(), label =>
                {
                    Part target = targetParts[label];
                    var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                    if (def == null) return;
                    PartResource targetResource = target.Resources.Get(def.id);
                    if (targetResource == null) return;
                    double space = targetResource.maxAmount - targetResource.amount;
                    double pushed = Math.Min(resourceAmount, space);
                    targetResource.amount += pushed;
                    float newAmount = resourceAmount - (float)pushed;
                    if (newAmount <= 0.001f) { WriteResourceName(cell.stored, ""); WriteResourceAmount(cell.stored, 0f); }
                    else WriteResourceAmount(cell.stored, newAmount);
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        string.Format("Transferred {0:F2} kg of {1}.", pushed, resourceName),
                        5.0f, ScreenMessageStyle.UPPER_CENTER));
                });
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Transfer from nearby part to cell",
         groupName = "fluidcelleva", groupDisplayName = "Fluid Cells", groupStartCollapsed = false)]
        public void EVATakeResources()
        {
            var shared = KShared.Instance;
            if (shared == null) { Debug.LogError("Khemistry: KShared null in EVATakeResources!"); return; }
            KShared.Instance.Log("Called! (Transfer from ... to cell button)", "KhemistryKerbal/EVATakeResources");

            var cells = GetAllCellRefs();
            if (cells.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No fluid cells available.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (cells.Count == 1)
            {
                ShowPartSelectorForTake(cells[0]);
            }
            else
            {
                var labels = new List<string>();
                for (int i = 0; i < cells.Count; i++)
                {
                    string cellLabel = GetCellLabel(cells[i], i);
                    string resName = ReadCellResourceName(cells[i]);
                    float resAmount = ReadCellResourceAmount(cells[i]);
                    float maxAmount = ReadCellMaxAmount(cells[i]);
                    labels.Add(string.IsNullOrEmpty(resName)
                        ? string.Format("{0}: Empty", cellLabel)
                        : string.Format("{0}: {1} {2:F1}/{3:F1} kg", cellLabel, resName, resAmount, maxAmount));
                }
                shared.ShowSelector("Which cell to fill?", labels, label =>
                {
                    int index = labels.IndexOf(label);
                    if (index >= 0) ShowPartSelectorForTake(cells[index]);
                });
            }
        }

        private void ShowPartSelectorForTake(FluidCellRef cell)
        {
            if (cell.isSuit) { ShowSuitCellPartSelectorForTake(); return; }
            KShared.Instance.Log("Called!", "KhemistryKerbal/ShowPartSelectorForTake");

            string currentResource = ReadResourceName(cell.stored);
            float currentAmount = ReadResourceAmount(cell.stored);
            float maxAmount = ReadMaxAmount(cell.stored.partName);
            float range = ReadTransferDistance(cell.stored.partName);
            HashSet<string> allowed = ReadAllowedResources(cell.stored.partName);

            if (currentAmount >= maxAmount)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "That cell is full.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            float spaceRemaining = maxAmount - currentAmount;
            var optionParts = new Dictionary<string, Part>();
            var optionResources = new Dictionary<string, string>();

            foreach (Part p in GetPartsInRange(range))
                foreach (PartResource pr in p.Resources)
                {
                    if (pr.amount <= 0) continue;
                    if (!string.IsNullOrEmpty(currentResource) && pr.resourceName != currentResource) continue;
                    if (string.IsNullOrEmpty(currentResource) && allowed.Count > 0
                        && !allowed.Contains(pr.resourceName)) continue;
                    string lbl = string.Format("{0} / {1}  ({2}: {3:F1} kg)",
                        p.vessel.vesselName, p.partInfo.title, pr.resourceName, pr.amount);
                    if (!optionParts.ContainsKey(lbl))
                    {
                        optionParts.Add(lbl, p);
                        optionResources.Add(lbl, pr.resourceName);
                    }
                }

            if (optionParts.Count == 0)
            {
                KShared.Instance.Log("No nearby parts with resource " + currentResource + " were detected.", "KhemistryKerbal/ShowPartSelectorForTake");
                string msg = string.IsNullOrEmpty(currentResource)
                    ? "No allowed resources found within range."
                    : "No nearby parts have " + currentResource + ".";
                ScreenMessages.PostScreenMessage(new ScreenMessage(msg, 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            KShared.Instance.Log("Calling ShowSelector to take resources from a part.", "KhemistryKerbal/ShowPartSelectorForTake");
            KShared.Instance.ShowSelector("Take resources from...", optionParts.Keys.ToList(), label =>
            {
                Part source = optionParts[label];
                string resourceName = optionResources[label];
                var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                if (def == null) return;
                PartResource sourceResource = source.Resources.Get(def.id);
                if (sourceResource == null) return;
                float maxTake = (float)Math.Min(sourceResource.amount, spaceRemaining);

                KShared.Instance.Log("Calling ShowAmountSelector to get exact amount.", "KhemistryKerbal/ShowPartSelectorForTake");
                KShared.Instance.ShowAmountSelector(
                    string.Format("How much {0} to take?", resourceName),
                    0f, maxTake, maxTake, amount =>
                    {
                        double taken = Math.Min(amount, maxTake);
                        if (taken <= 0.0) return;
                        sourceResource.amount -= taken;
                        WriteResourceName(cell.stored, resourceName);
                        WriteResourceAmount(cell.stored, currentAmount + (float)taken);
                        ScreenMessages.PostScreenMessage(new ScreenMessage(
                            string.Format("Received {0:F2} kg of {1}.", taken, resourceName),
                            5.0f, ScreenMessageStyle.UPPER_CENTER));
                    });
            });
        }

        private void ShowSuitCellPartSelectorForTake()
        {
            KShared.Instance.Log("Called!", "KhemistryKerbal/ShowSuitCellPartSelectorForTake");
            var dict = GetSuitCellDict();
            double currentTotal = KhemistryEVACombinedProcessor.GetTotal(dict);
            double spaceRemaining = _suitCellMaxAmount - currentTotal;

            if (spaceRemaining <= 0.0)
            {
                KShared.Instance.Log("Unable to take resources: suit cell is full.", "KhemistryKerbal/ShowSuitCellPartSelectorForTake");
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Suit cell is full.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            var options = new Dictionary<string, (Part part, PartResource resource)>();
            foreach (Part p in GetPartsInRange(_suitCellTransferDistance))
                foreach (PartResource pr in p.Resources)
                {
                    if (pr.amount <= 0.0) continue;
                    if (_suitCellAllowedResources.Count > 0
                        && !_suitCellAllowedResources.Contains(pr.resourceName)) continue;
                    string lbl = string.Format("{0} / {1}  ({2}: {3:F2})",
                        p.vessel.vesselName, p.partInfo.title, pr.resourceName, pr.amount);
                    if (!options.ContainsKey(lbl))
                        options.Add(lbl, (p, pr));
                }

            if (options.Count == 0)
            {
                KShared.Instance.Log("No nearby parts have any of the allowed resources.", "KhemistryKerbal/ShowSuitCellPartSelectorForTake");
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No nearby parts have allowed resources.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            KShared.Instance.Log("Calling ShowSelector to take resources from a part.", "KhemistryKerbal/ShowSuitCellPartSelectorForTake");
            KShared.Instance.ShowSelector("Take from...", new List<string>(options.Keys), label =>
            {
                var (sourcePart, sourceResource) = options[label];
                string resourceName = sourceResource.resourceName;
                float maxTake = (float)Math.Min(sourceResource.amount, spaceRemaining);

                KShared.Instance.Log("Calling ShowAmountSelector to get exact amount.", "KhemistryKerbal/ShowSuitCellPartSelectorForTake");
                KShared.Instance.ShowAmountSelector(
                    string.Format("How much {0} to take?", resourceName),
                    0f, maxTake, maxTake, amount =>
                    {
                        double taken = Math.Min((double)amount, maxTake);
                        if (taken <= 0.0) return;
                        sourceResource.amount -= taken;
                        var d = GetSuitCellDict();
                        double existing;
                        d.TryGetValue(resourceName, out existing);
                        d[resourceName] = existing + taken;
                        SetSuitCellFromDict(d);
                        ScreenMessages.PostScreenMessage(new ScreenMessage(
                            string.Format("Received {0:F2} of {1}.", taken, resourceName),
                            5f, ScreenMessageStyle.UPPER_CENTER));
                    });
            });
        }

        // ── Processor helpers (unchanged) ──────────────────────────────────────────

        private List<StoredPart> GetProcessorSnapshots()
        {
            var result = new List<StoredPart>();
            if (_inventory == null) return result;
            for (int i = 0; i < _inventory.storedParts.Count; i++)
            {
                StoredPart stored = _inventory.storedParts.At(i);
                AvailablePart ap = PartLoader.getPartInfoByName(stored.partName);
                if (ap?.partPrefab.FindModuleImplementing<KhemistryEVACombinedProcessor>() != null)
                    result.Add(stored);
            }
            return result;
        }

        private KhemistryEVACombinedProcessor GetPrefabProcessor(StoredPart stored)
            => PartLoader.getPartInfoByName(stored.partName)?.partPrefab
                .FindModuleImplementing<KhemistryEVACombinedProcessor>();

        private ProtoPartModuleSnapshot GetProcessorSnapshot(StoredPart stored)
        {
            if (stored.snapshot == null) return null;
            foreach (ProtoPartModuleSnapshot snap in stored.snapshot.modules)
                if (snap.moduleName == "KhemistryEVACombinedProcessor") return snap;
            return null;
        }

        private string ReadProcessorField(StoredPart stored, string key)
            => GetProcessorSnapshot(stored)?.moduleValues.GetValue(key) ?? "";

        private void WriteProcessorField(StoredPart stored, string key, string value)
            => GetProcessorSnapshot(stored)?.moduleValues.SetValue(key, value);

        private bool ReadProcessorBool(StoredPart stored, string key)
        {
            bool result;
            return bool.TryParse(ReadProcessorField(stored, key), out result) && result;
        }

        private Dictionary<string, double> DeserializeProcessorResources(StoredPart stored)
            => KhemistryEVACombinedProcessor.Deserialize(ReadProcessorField(stored, "storedResourcesData"));

        private void WriteProcessorResources(StoredPart stored, Dictionary<string, double> resources)
            => WriteProcessorField(stored, "storedResourcesData",
                KhemistryEVACombinedProcessor.Serialize(resources));

        // ── Processor EVA event and action methods (unchanged) ────────────────────

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Use Held Processor",
                  groupName = "processoreva", groupDisplayName = "Processors", groupStartCollapsed = false,
                  externalToEVAOnly = true, guiActiveUnfocused = false, unfocusedRange = 10f)]
        public void EVAUseProcessor()
        {
            KShared.Instance.Log("Called! (Use Held Processor button)", "KhemistryKerbal/EVAUseProcessor");
            var shared = KShared.Instance;
            if (shared == null) return;

            var processors = GetProcessorSnapshots();
            if (processors.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No processors in inventory.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (processors.Count == 1)
            {
                ShowProcessorActionMenu(processors[0]);
                return;
            }

            var labels = new List<string>();
            foreach (StoredPart stored in processors)
            {
                KhemistryEVACombinedProcessor prefab = GetPrefabProcessor(stored);
                string name = prefab != null ? stored.partName : stored.partName;
                bool running = ReadProcessorBool(stored, "isRunning");
                string conv = ReadProcessorField(stored, "activeConverterName");
                string suffix = running ? " [" + conv + "]" : " [Stopped]";
                labels.Add(name + suffix);
            }

            shared.ShowSelector("Select processor", labels, label =>
            {
                int idx = labels.IndexOf(label);
                if (idx >= 0) ShowProcessorActionMenu(processors[idx]);
            });
        }

        private void ShowProcessorActionMenu(StoredPart stored)
        {
            var shared = KShared.Instance;
            KhemistryEVACombinedProcessor prefab = GetPrefabProcessor(stored);
            if (prefab == null || !prefab.IsConfigLoaded) return;

            bool running = ReadProcessorBool(stored, "isRunning");
            var actions = new List<string>();

            if (prefab.Converters.Count > 0)
            {
                if (!running) actions.Add("Start Converter");
                else actions.Add("Stop Converter");
            }

            actions.Add("Transfer In (from nearby)");

            var resources = DeserializeProcessorResources(stored);
            if (KhemistryEVACombinedProcessor.GetTotal(resources) > 0.0)
                actions.Add("Transfer Out (to nearby)");

            if (actions.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No actions available.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            shared.ShowSelector("Processor: " + stored.partName, actions,
                action => ExecuteProcessorAction(stored, prefab, action));
        }

        private void ExecuteProcessorAction(StoredPart stored,
            KhemistryEVACombinedProcessor prefab, string action)
        {
            var shared = KShared.Instance;
            if (shared == null) return;

            switch (action)
            {
                case "Start Converter":
                    {
                        if (prefab.Converters.Count == 1)
                        {
                            WriteProcessorField(stored, "activeConverterName", prefab.Converters[0].name);
                            WriteProcessorField(stored, "isRunning", "True");
                            ScreenMessages.PostScreenMessage(new ScreenMessage(
                                "Converter \"" + prefab.Converters[0].name + "\" started.",
                                4f, ScreenMessageStyle.UPPER_CENTER));
                        }
                        else
                        {
                            var names = new List<string>();
                            foreach (var conv in prefab.Converters) names.Add(conv.name);
                            shared.ShowSelector("Select converter to start", names, name =>
                            {
                                WriteProcessorField(stored, "activeConverterName", name);
                                WriteProcessorField(stored, "isRunning", "True");
                                ScreenMessages.PostScreenMessage(new ScreenMessage(
                                    "Converter \"" + name + "\" started.", 4f, ScreenMessageStyle.UPPER_CENTER));
                            });
                        }
                        break;
                    }
                case "Stop Converter":
                    WriteProcessorField(stored, "isRunning", "False");
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Converter stopped.", 4f, ScreenMessageStyle.UPPER_CENTER));
                    break;

                case "Transfer In (from nearby)":
                    ShowProcessorTransferInMenu(stored, prefab);
                    break;

                case "Transfer Out (to nearby)":
                    ShowProcessorTransferOutMenu(stored, prefab);
                    break;
            }
        }

        private void ShowProcessorTransferInMenu(StoredPart stored,
            KhemistryEVACombinedProcessor prefab)
        {
            KShared.Instance?.Log("Called!", "KhemistryKerbal/ShowProcessorTransferInMenu");
            var shared = KShared.Instance;
            var resources = DeserializeProcessorResources(stored);
            double currentTotal = KhemistryEVACombinedProcessor.GetTotal(resources);
            double spaceRemaining = prefab.MaxTotalStorage - currentTotal;

            if (spaceRemaining <= 0.0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Processor is full.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            var options = new Dictionary<string, (Part part, string resourceName)>();
            foreach (Part p in GetPartsInRange(prefab.TransferDistance))
                foreach (PartResource pr in p.Resources)
                {
                    if (!prefab.SupportedResources.Contains(pr.resourceName)) continue;
                    if (pr.amount <= 0.0) continue;
                    string label = string.Format("{0} / {1}  ({2}: {3:F1})",
                        p.vessel.vesselName, p.partInfo.title, pr.resourceName, pr.amount);
                    if (!options.ContainsKey(label))
                        options.Add(label, (p, pr.resourceName));
                }

            if (options.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No nearby parts have supported resources.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            shared.ShowSelector("Take from...", new List<string>(options.Keys), label =>
            {
                var (sourcePart, resourceName) = options[label];
                var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                if (def == null) return;
                PartResource sourceResource = sourcePart.Resources.Get(def.id);
                if (sourceResource == null) return;

                double taken = Math.Min(sourceResource.amount, spaceRemaining);
                sourceResource.amount -= taken;

                var res = DeserializeProcessorResources(stored);
                double existing;
                res.TryGetValue(resourceName, out existing);
                res[resourceName] = existing + taken;
                WriteProcessorResources(stored, res);

                KShared.Instance?.Log(
                    string.Format("Processor received {0:F4} of {1} from {2}.",
                        taken, resourceName, sourcePart.partInfo.title),
                    "KhemistryKerbal/ProcessorTransferIn");
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    string.Format("Received {0:F2} of {1}.", taken, resourceName),
                    5f, ScreenMessageStyle.UPPER_CENTER));
            });
        }

        private void ShowProcessorTransferOutMenu(StoredPart stored,
            KhemistryEVACombinedProcessor prefab)
        {
            var shared = KShared.Instance;
            var resources = DeserializeProcessorResources(stored);

            if (resources.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Processor is empty.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (resources.Count == 1)
            {
                string only = ""; double onlyAmount = 0.0;
                foreach (var kvp in resources) { only = kvp.Key; onlyAmount = kvp.Value; }
                ShowProcessorTransferOutTargets(stored, prefab, only, onlyAmount);
                return;
            }

            var resLabels = new List<string>();
            var resKeys = new List<string>();
            foreach (var kvp in resources)
            {
                resLabels.Add(string.Format("{0}: {1:F2}", kvp.Key, kvp.Value));
                resKeys.Add(kvp.Key);
            }

            shared.ShowSelector("Which resource to send?", resLabels, label =>
            {
                int idx = resLabels.IndexOf(label);
                if (idx >= 0)
                    ShowProcessorTransferOutTargets(stored, prefab, resKeys[idx], resources[resKeys[idx]]);
            });
        }

        private void ShowProcessorTransferOutTargets(StoredPart stored,
            KhemistryEVACombinedProcessor prefab, string resourceName, double resourceAmount)
        {
            var shared = KShared.Instance;
            var options = new Dictionary<string, Part>();

            foreach (Part p in GetPartsInRange(prefab.TransferDistance))
                foreach (PartResource pr in p.Resources)
                {
                    if (pr.resourceName != resourceName) continue;
                    if (pr.amount >= pr.maxAmount) continue;
                    string label = string.Format("{0} / {1}  (space: {2:F1})",
                        p.vessel.vesselName, p.partInfo.title, pr.maxAmount - pr.amount);
                    if (!options.ContainsKey(label))
                        options.Add(label, p);
                }

            if (options.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No nearby parts can accept " + resourceName + ".",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            shared.ShowSelector("Send " + resourceName + " to...",
                new List<string>(options.Keys), label =>
                {
                    Part target = options[label];
                    var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                    if (def == null) return;
                    PartResource targetResource = target.Resources.Get(def.id);
                    if (targetResource == null) return;

                    double space = targetResource.maxAmount - targetResource.amount;
                    double pushed = Math.Min(resourceAmount, space);
                    targetResource.amount += pushed;

                    var res = DeserializeProcessorResources(stored);
                    double remaining = resourceAmount - pushed;
                    if (remaining < 1e-9) res.Remove(resourceName);
                    else res[resourceName] = remaining;
                    WriteProcessorResources(stored, res);

                    KShared.Instance?.Log(
                        string.Format("Processor sent {0:F4} of {1} to {2}.",
                            pushed, resourceName, target.partInfo.title),
                        "KhemistryKerbal/ProcessorTransferOut");
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        string.Format("Transferred {0:F2} of {1}.", pushed, resourceName),
                        5f, ScreenMessageStyle.UPPER_CENTER));
                });
        }
        private void ShowSuitCellPartSelectorForSend()
        {
            var dict = GetSuitCellDict();
            if (dict.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Suit cell is empty.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (dict.Count == 1)
            {
                foreach (var kvp in dict) { ShowSuitCellSendTargets(kvp.Key, kvp.Value); return; }
            }

            var labels = new List<string>();
            var keys = new List<string>();
            var amounts = new List<double>();
            foreach (var kvp in dict)
            {
                labels.Add(string.Format("{0}: {1:F2}", kvp.Key, kvp.Value));
                keys.Add(kvp.Key);
                amounts.Add(kvp.Value);
            }

            KShared.Instance.ShowSelector("Which resource to send?", labels, label =>
            {
                int idx = labels.IndexOf(label);
                if (idx >= 0) ShowSuitCellSendTargets(keys[idx], amounts[idx]);
            });
        }

        private void ShowSuitCellSendTargets(string resourceName, double resourceAmount)
        {
            var options = new Dictionary<string, Part>();
            foreach (Part p in GetPartsInRange(_suitCellTransferDistance))
                foreach (PartResource pr in p.Resources)
                {
                    if (pr.resourceName != resourceName) continue;
                    if (pr.amount >= pr.maxAmount) continue;
                    string lbl = string.Format("{0} / {1}  (space: {2:F1})",
                        p.vessel.vesselName, p.partInfo.title, pr.maxAmount - pr.amount);
                    if (!options.ContainsKey(lbl)) options.Add(lbl, p);
                }

            if (options.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No nearby parts can accept " + resourceName + ".", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            KShared.Instance.ShowSelector("Send " + resourceName + " to...",
                new List<string>(options.Keys), label =>
                {
                    Part target = options[label];
                    var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                    if (def == null) return;
                    PartResource targetResource = target.Resources.Get(def.id);
                    if (targetResource == null) return;
                    double space = targetResource.maxAmount - targetResource.amount;
                    double pushed = Math.Min(resourceAmount, space);
                    targetResource.amount += pushed;
                    var d = GetSuitCellDict();
                    double existing;
                    d.TryGetValue(resourceName, out existing);
                    double remaining = existing - pushed;
                    if (remaining < 1e-9) d.Remove(resourceName);
                    else d[resourceName] = remaining;
                    SetSuitCellFromDict(d);
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        string.Format("Transferred {0:F2} of {1}.", pushed, resourceName),
                        5f, ScreenMessageStyle.UPPER_CENTER));
                });
        }
    }

    public class KhemistryEVACombinedProcessor : PartModule
    {
        // ── Persistent state ───────────────────────────────────────────────────────

        // Serialized as "ResA:1.5000|ResB:2.0000". Empty string = nothing stored.
        [KSPField(isPersistant = true)]
        public string storedResourcesData = "";

        [KSPField(isPersistant = true)]
        public bool isRunning = false;

        [KSPField(isPersistant = true)]
        public string activeConverterName = "";

        // ── Config fields ──────────────────────────────────────────────────────────

        [KSPField(isPersistant = false)]
        public float maxTotalStorage = 200f;

        [KSPField(isPersistant = false)]
        public float transferDistance = 10f;

        // ── Display ────────────────────────────────────────────────────────────────

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true,
                  guiName = "Contents", groupName = "khemistryprocessor",
                  groupDisplayName = "Processor", groupStartCollapsed = false)]
        public string contentsDisplay = "Empty";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "Converter", groupName = "khemistryprocessor")]
        public string converterDisplay = "Stopped";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true,
                  guiName = "Capacity", groupName = "khemistryprocessor")]
        public string capacityDisplay = "0 / 0";

        // ── Internal data ──────────────────────────────────────────────────────────

        public struct ProcessorConverter
        {
            public string name;
            public List<(string resourceName, double ratio)> inputs;
            public List<(string resourceName, double ratio)> outputs;
        }

        private readonly List<string> _supportedResources = new List<string>();
        private readonly List<ProcessorConverter> _converters = new List<ProcessorConverter>();
        private bool _fatalConfigError = false;

        // ── Public accessors for KhemistryKerbal ───────────────────────────────────

        public bool IsConfigLoaded => !_fatalConfigError;
        public List<string> SupportedResources => _supportedResources;
        public List<ProcessorConverter> Converters => _converters;
        public float MaxTotalStorage => maxTotalStorage;
        public float TransferDistance => transferDistance;

        // ── Lifecycle ──────────────────────────────────────────────────────────────

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            _supportedResources.Clear();
            _converters.Clear();
            _fatalConfigError = false;

            // Scalar config
            float tmp;
            if (float.TryParse(node.GetValue("maxTotalStorage"), out tmp)) maxTotalStorage = tmp;
            if (float.TryParse(node.GetValue("transferDistance"), out tmp)) transferDistance = tmp;

            // SUPPORTED_RESOURCES — required
            if (!node.HasNode("SUPPORTED_RESOURCES"))
            {
                KShared.Instance?.LogError(
                    "Part \"" + part.name + "\" has KhemistryEVACombinedProcessor but no SUPPORTED_RESOURCES node.",
                    "KhemistryEVACombinedProcessor/OnLoad");
                _fatalConfigError = true;
                return;
            }
            foreach (string n in node.GetNode("SUPPORTED_RESOURCES").GetValues("name"))
                _supportedResources.Add(n.Trim());

            if (_supportedResources.Count == 0)
            {
                KShared.Instance?.LogError(
                    "Part \"" + part.name + "\" has an empty SUPPORTED_RESOURCES node.",
                    "KhemistryEVACombinedProcessor/OnLoad");
                _fatalConfigError = true;
                return;
            }

            // CONVERTER nodes — optional
            foreach (ConfigNode convNode in node.GetNodes("CONVERTER"))
            {
                string convName = convNode.GetValue("ConverterName");
                if (string.IsNullOrEmpty(convName))
                {
                    KShared.Instance?.LogError("A CONVERTER node is missing ConverterName, skipping.",
                        "KhemistryEVACombinedProcessor/OnLoad");
                    continue;
                }

                var conv = new ProcessorConverter
                {
                    name = convName,
                    inputs = new List<(string, double)>(),
                    outputs = new List<(string, double)>()
                };

                foreach (ConfigNode inputNode in convNode.GetNodes("INPUT_RESOURCE"))
                {
                    string resName = inputNode.GetValue("ResourceName");
                    if (string.IsNullOrEmpty(resName)) continue;
                    double ratio = 0.0;
                    double.TryParse(inputNode.GetValue("Ratio"), out ratio);
                    conv.inputs.Add((resName, ratio));
                }

                foreach (ConfigNode outputNode in convNode.GetNodes("OUTPUT_RESOURCE"))
                {
                    string resName = outputNode.GetValue("ResourceName");
                    if (string.IsNullOrEmpty(resName)) continue;
                    double ratio = 0.0;
                    double.TryParse(outputNode.GetValue("Ratio"), out ratio);
                    conv.outputs.Add((resName, ratio));
                }

                _converters.Add(conv);
            }

            KShared.Instance?.Log(
                string.Format("OnLoad: {0} supported resources, {1} converters, maxStorage={2}, transferDist={3}",
                    _supportedResources.Count, _converters.Count, maxTotalStorage, transferDistance),
                "KhemistryEVACombinedProcessor/OnLoad");
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            // Config is already loaded by OnLoad — just update the display
            if (_fatalConfigError)
            {
                contentsDisplay = "ERROR: see log";
                converterDisplay = "ERROR";
                return;
            }

            UpdateDisplay(Deserialize(storedResourcesData));
        }

        // ── Serialization (static so KhemistryKerbal can use without a live instance) ──

        public static Dictionary<string, double> Deserialize(string data)
        {
            var dict = new Dictionary<string, double>();
            if (string.IsNullOrEmpty(data)) return dict;
            foreach (string entry in data.Split('|'))
            {
                if (string.IsNullOrEmpty(entry)) continue;
                int sep = entry.IndexOf(':');
                if (sep < 1) continue;
                string name = entry.Substring(0, sep);
                double amount;
                if (double.TryParse(entry.Substring(sep + 1), out amount) && amount > 0.0)
                    dict[name] = amount;
            }
            return dict;
        }

        public static string Serialize(Dictionary<string, double> dict)
        {
            var parts = new List<string>();
            foreach (var kvp in dict)
                if (kvp.Value > 0.0)
                    parts.Add(kvp.Key + ":" + kvp.Value.ToString("F4"));
            return string.Join("|", parts.ToArray());
        }

        public static double GetTotal(Dictionary<string, double> dict)
        {
            double total = 0.0;
            foreach (var kvp in dict) total += kvp.Value;
            return total;
        }

        // ── Conversion cycle ───────────────────────────────────────────────────────

        /// <summary>
        /// Attempts one conversion tick on the provided resource dictionary.
        /// Mutates the dictionary in place. Returns true if conversion ran.
        /// </summary>
        public bool RunConversionCycle(Dictionary<string, double> resources,
            string converterName, double dt)
        {
            ProcessorConverter? found = null;
            foreach (var conv in _converters)
                if (conv.name == converterName) { found = conv; break; }

            if (found == null) return false;
            var c = found.Value;

            // Check all inputs are available
            foreach (var input in c.inputs)
            {
                double needed = input.ratio * dt;
                double available;
                if (!resources.TryGetValue(input.resourceName, out available)
                    || available < needed * 0.999)
                    return false;
            }

            // Check total capacity won't be exceeded
            double inputSum = 0.0;
            foreach (var input in c.inputs) inputSum += input.ratio * dt;
            double outputSum = 0.0;
            foreach (var output in c.outputs) outputSum += output.ratio * dt;
            double currentTotal = GetTotal(resources);
            if (currentTotal - inputSum + outputSum > maxTotalStorage)
                return false;

            // Consume inputs
            foreach (var input in c.inputs)
            {
                double needed = input.ratio * dt;
                resources[input.resourceName] -= needed;
                if (resources[input.resourceName] < 1e-9)
                    resources.Remove(input.resourceName);
            }

            // Produce outputs
            foreach (var output in c.outputs)
            {
                double existing;
                resources.TryGetValue(output.resourceName, out existing);
                resources[output.resourceName] = existing + output.ratio * dt;
            }

            return true;
        }

        // ── Display helper ─────────────────────────────────────────────────────────

        public void UpdateDisplay(Dictionary<string, double> resources)
        {
            double total = GetTotal(resources);

            if (resources.Count == 0)
                contentsDisplay = "Empty";
            else
            {
                var parts = new List<string>();
                foreach (var kvp in resources)
                    parts.Add(string.Format("{0}: {1:F2}", kvp.Key, kvp.Value));
                contentsDisplay = string.Join(", ", parts.ToArray());
            }

            capacityDisplay = string.Format("{0:F2} / {1:F2}", total, maxTotalStorage);

            converterDisplay = (isRunning && !string.IsNullOrEmpty(activeConverterName))
                ? "Running: " + activeConverterName
                : "Stopped";
        }
    }
}