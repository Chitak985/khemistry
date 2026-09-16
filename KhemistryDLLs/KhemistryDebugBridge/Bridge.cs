using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Khemistry;
using Parallax;
using UnityEngine;

namespace Khemistry.DebugBridge
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class KhemistryDebugBridge : MonoBehaviour
    {
        private const string ProtocolMagic = "KHDBG1";
        private const int MaximumArguments = 64;
        private const int MaximumStringBytes = 8 * 1024 * 1024;
        private readonly Queue<PendingCommand> pending = new Queue<PendingCommand>();
        private Thread serverThread;
        private volatile bool stopping;
        private NamedPipeServerStream listeningPipe;
        private string pipeName;
        private string token;
        private string sessionPath;

        private sealed class PendingCommand
        {
            internal string command;
            internal Dictionary<string, string> arguments;
            internal readonly ManualResetEvent completed = new ManualResetEvent(false);
            internal bool ok;
            internal string response;
        }

        public void Awake()
        {
            DontDestroyOnLoad(gameObject);
            pipeName = "khemistry-debug-" + System.Diagnostics.Process.GetCurrentProcess().Id;
            token = CreateToken();
            sessionPath = Path.Combine(Path.GetTempPath(), "khemistry-debug-session.json");
            WriteSessionFile();
            serverThread = new Thread(ServerLoop) { IsBackground = true, Name = "KhemistryDebugBridge" };
            serverThread.Start();
            UnityEngine.Debug.Log("[Khemistry Debug] Bridge listening on pipe " + pipeName);
        }

        public void Update()
        {
            for (int i = 0; i < 16; i++)
            {
                PendingCommand request;
                lock (pending)
                {
                    if (pending.Count == 0) break;
                    request = pending.Dequeue();
                }
                try
                {
                    request.response = JsonWriter.Serialize(Execute(request.command, request.arguments));
                    request.ok = true;
                }
                catch (Exception exception)
                {
                    request.ok = false;
                    request.response = exception.GetType().Name + ": " + exception.Message;
                    UnityEngine.Debug.LogError("[Khemistry Debug] " + exception);
                }
                finally { request.completed.Set(); }
            }
        }

        public void OnDestroy()
        {
            stopping = true;
            try { listeningPipe?.Dispose(); } catch { }
            try
            {
                if (!string.IsNullOrEmpty(sessionPath) && File.Exists(sessionPath))
                    File.Delete(sessionPath);
            }
            catch { }
        }

        private void ServerLoop()
        {
            while (!stopping)
            {
                try
                {
                    using (var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.None, 8192, 8192))
                    {
                        listeningPipe = pipe;
                        pipe.WaitForConnection();
                        listeningPipe = null;
                        using (var reader = new BinaryReader(pipe, Encoding.UTF8, true))
                        using (var writer = new BinaryWriter(pipe, Encoding.UTF8, true))
                        {
                            string magic = ReadBoundedString(reader);
                            string suppliedToken = ReadBoundedString(reader);
                            string command = ReadBoundedString(reader);
                            int count = reader.ReadInt32();
                            if (magic != ProtocolMagic) throw new InvalidDataException("Protocol mismatch.");
                            if (!ConstantTimeEquals(token, suppliedToken)) throw new UnauthorizedAccessException("Invalid session token.");
                            if (count < 0 || count > MaximumArguments) throw new InvalidDataException("Invalid argument count.");
                            var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            for (int i = 0; i < count; i++)
                                arguments[ReadBoundedString(reader)] = ReadBoundedString(reader);

                            var request = new PendingCommand { command = command, arguments = arguments };
                            lock (pending) pending.Enqueue(request);
                            if (!request.completed.WaitOne(TimeSpan.FromSeconds(30)))
                            {
                                writer.Write(false);
                                writer.Write("Timed out waiting for KSP's main thread.");
                            }
                            else
                            {
                                writer.Write(request.ok);
                                writer.Write(request.response ?? "");
                            }
                            writer.Flush();
                        }
                    }
                }
                catch (ObjectDisposedException) when (stopping) { }
                catch (IOException) when (stopping) { }
                catch (Exception exception)
                {
                    if (!stopping) UnityEngine.Debug.LogError("[Khemistry Debug] Pipe error: " + exception);
                    Thread.Sleep(250);
                }
            }
        }

        private object Execute(string command, Dictionary<string, string> arguments)
        {
            switch ((command ?? "").Trim().ToLowerInvariant())
            {
                case "ping": return GameState(true);
                case "game_state": return GameState(false);
                case "list_parts": return ListParts(arguments);
                case "inspect_part": return InspectPart(arguments);
                case "inspect_khemistry": return InspectKhemistry(arguments);
                case "converter_action": return ConverterAction(arguments);
                case "list_parallax_scatters": return ListParallaxScatters(arguments);
                case "capture_screenshot": return CaptureScreenshot(arguments);
                default: throw new ArgumentException("Unknown bridge command: " + command);
            }
        }

        private Dictionary<string, object> GameState(bool ping)
        {
            Vessel active = SafeActiveVessel();
            return Object(
                "bridgeVersion", "0.1.0",
                "pong", ping,
                "scene", HighLogic.LoadedScene.ToString(),
                "saveFolder", HighLogic.SaveFolder,
                "universalTime", SafeUniversalTime(),
                "loadedVesselCount", SafeLoadedVessels().Count,
                "activeVessel", active == null ? null : VesselSummary(active));
        }

        private object ListParts(Dictionary<string, string> arguments)
        {
            Vessel vessel = FindVessel(arguments);
            if (vessel == null) throw new InvalidOperationException("No matching loaded vessel is available.");
            return Object("vessel", VesselSummary(vessel), "parts", vessel.parts.Select(PartSummary).ToList());
        }

        private object InspectPart(Dictionary<string, string> arguments)
        {
            Part part = FindPart(arguments);
            if (part == null) throw new InvalidOperationException("No matching loaded part is available.");
            return PartDetails(part);
        }

        private object InspectKhemistry(Dictionary<string, string> arguments)
        {
            Part selected = FindPart(arguments, false);
            Vessel vessel = selected?.vessel ?? FindVessel(arguments);
            if (vessel == null) throw new InvalidOperationException("No matching loaded vessel is available.");
            IEnumerable<Part> parts = selected == null
                ? (IEnumerable<Part>)vessel.parts : new[] { selected };
            var snapshots = new List<object>();
            foreach (Part part in parts)
                for (int index = 0; index < part.Modules.Count; index++)
                {
                    object snapshot = ModuleSnapshot(part.Modules[index]);
                    if (snapshot == null) continue;
                    snapshots.Add(Object("partFlightId", part.flightID, "partTitle", PartTitle(part),
                        "moduleIndex", index, "module", snapshot));
                }
            return Object("vessel", VesselSummary(vessel), "modules", snapshots);
        }

        private object ConverterAction(Dictionary<string, string> arguments)
        {
            Part part = FindPart(arguments);
            if (part == null) throw new InvalidOperationException("No matching loaded part is available.");
            int moduleIndex = GetInt(arguments, "module_index", -1);
            if (moduleIndex < 0 || moduleIndex >= part.Modules.Count)
                throw new ArgumentOutOfRangeException("module_index");
            var converter = part.Modules[moduleIndex] as KhemistryISRU;
            if (converter == null) throw new InvalidOperationException("Selected module is not KhemistryISRU.");
            string action = Get(arguments, "action", true).ToLowerInvariant();
            switch (action)
            {
                case "start": converter.StartConverter(); break;
                case "stop": converter.StopConverter(); break;
                case "turn_on": converter.TurnOnConverter(); break;
                case "turn_off": converter.TurnOffConverter(); break;
                case "select_recipe": SelectRecipe(converter, Get(arguments, "recipe_name", true)); break;
                default: throw new ArgumentException("Unknown converter action: " + action);
            }
            return ModuleSnapshot(converter);
        }

        private static void SelectRecipe(KhemistryISRU converter, string recipeName)
        {
            if (converter.isRunning) throw new InvalidOperationException("Stop the converter before selecting a recipe.");
            FieldInfo recipesField = typeof(KhemistryISRU).GetField("recipes", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo applyMethod = typeof(KhemistryISRU).GetMethod("ApplyRecipe", BindingFlags.Instance | BindingFlags.NonPublic);
            var recipes = recipesField?.GetValue(converter) as IEnumerable<KhemistryISRURecipe>;
            KhemistryISRURecipe recipe = recipes?.FirstOrDefault(r => r._name == recipeName);
            if (recipe == null) throw new ArgumentException("Recipe not found: " + recipeName);
            if (applyMethod == null) throw new MissingMethodException("KhemistryISRU.ApplyRecipe");
            applyMethod.Invoke(converter, new object[] { recipe });
        }

        private object ListParallaxScatters(Dictionary<string, string> arguments)
        {
            Vessel vessel = FindVessel(arguments);
            if (vessel == null) throw new InvalidOperationException("No matching loaded vessel is available.");
            int maximum = Math.Max(1, Math.Min(500, GetInt(arguments, "max_results", 50)));
            string filter = Get(arguments, "scatter_name", false);
            Vector3 origin = vessel.transform.position;
            var rows = new List<Dictionary<string, object>>();
            Scatter[] scatters = Parallax.CollisionManager.collideableScatters;
            Dictionary<PositionDataQuadID, GameObject>[] active = Parallax.CollisionManager.activeObjects;
            if (scatters != null && active != null)
                for (int scatterIndex = 0; scatterIndex < Math.Min(scatters.Length, active.Length); scatterIndex++)
                {
                    string name = scatters[scatterIndex]?.scatterName;
                    if (!string.IsNullOrEmpty(filter) && !string.Equals(name, filter, StringComparison.OrdinalIgnoreCase))
                        continue;
                    Dictionary<PositionDataQuadID, GameObject> instances = active[scatterIndex];
                    if (instances == null) continue;
                    foreach (var pair in instances)
                    {
                        GameObject go = pair.Value;
                        if (go == null || !go.activeInHierarchy) continue;
                        Collider collider = go.GetComponent<Collider>();
                        Vector3 nearest = collider != null ? collider.ClosestPoint(origin) : go.transform.position;
                        rows.Add(Object("scatter", name, "distance", Vector3.Distance(origin, nearest),
                            "positionIndex", pair.Key.index,
                            "quadId", pair.Key.quadID,
                            "localPosition", Vector(pair.Key.localPos.x, pair.Key.localPos.y, pair.Key.localPos.z),
                            "localScale", Vector(pair.Key.localScale.x, pair.Key.localScale.y, pair.Key.localScale.z),
                            "rotation", pair.Key.rotation,
                            "worldPosition", Vector(go.transform.position.x, go.transform.position.y, go.transform.position.z),
                            "colliderEnabled", collider != null && collider.enabled));
                    }
                }
            return Object("vessel", VesselSummary(vessel), "totalActiveMatches", rows.Count,
                "instances", rows.OrderBy(row => Convert.ToDouble(row["distance"], CultureInfo.InvariantCulture))
                    .Take(maximum).Cast<object>().ToList());
        }

        private object CaptureScreenshot(Dictionary<string, string> arguments)
        {
            string name = Get(arguments, "name", false);
            if (string.IsNullOrWhiteSpace(name)) name = "khemistry-debug-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".png";
            name = Path.GetFileName(name);
            if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name += ".png";
            int supersize = Math.Max(1, Math.Min(4, GetInt(arguments, "supersize", 1)));
            ScreenCapture.CaptureScreenshot(name, supersize);
            return Object("queued", true, "path", Path.Combine(KSPUtil.ApplicationRootPath, "Screenshots", name));
        }

        private static Dictionary<string, object> PartSummary(Part part)
        {
            var modules = new List<object>();
            for (int i = 0; i < part.Modules.Count; i++)
                modules.Add(Object("index", i, "name", part.Modules[i].moduleName,
                    "type", part.Modules[i].GetType().FullName));
            return Object("flightId", part.flightID, "name", part.name, "title", PartTitle(part),
                "modules", modules);
        }

        private static Dictionary<string, object> PartDetails(Part part)
        {
            var stockResources = part.Resources.Cast<PartResource>().Select(resource =>
                (object)Object("name", resource.resourceName, "amount", resource.amount,
                    "maxAmount", resource.maxAmount, "flowState", resource.flowState)).ToList();
            var modules = new List<object>();
            for (int i = 0; i < part.Modules.Count; i++)
                modules.Add(Object("index", i, "name", part.Modules[i].moduleName,
                    "type", part.Modules[i].GetType().FullName,
                    "khemistry", ModuleSnapshot(part.Modules[i])));
            return Object("flightId", part.flightID, "name", part.name, "title", PartTitle(part),
                "vesselId", part.vessel?.id.ToString("D"), "stockResources", stockResources,
                "modules", modules);
        }

        private static object ModuleSnapshot(PartModule module)
        {
            if (module is KhemistryAdvancedStorage advanced)
                return Object("type", "AdvancedStorage", "state", advanced.state.ToString(),
                    "storageType", advanced.storageType, "activeResource", advanced.activeResource,
                    "maximumResources", advanced.maximumResources,
                    "transfersEnabled", advanced.TransfersEnabled,
                    "resources", ToObjectDictionary(advanced.GetStoredResources()));
            if (module is KhemistryFluidCell fluid)
                return Object("type", "FluidCell", "maximumResources", fluid.ResourceMaxAmount,
                    "transferDistance", fluid.TransferDistance,
                    "supportedResources", fluid.SupportedResources.OrderBy(x => x).Cast<object>().ToList(),
                    "resources", ToObjectDictionary(fluid.GetStoredResources()));
            if (module is KhemistryMaterialStorage materialStorage)
                return Object("type", "MaterialStorage", "capacityVolume", materialStorage.volume,
                    "volumeDisplay", materialStorage.volumeDisplay,
                    "materials", materialStorage.contents.Select(MaterialSnapshot).ToList());
            if (module is KhemistryKerbal kerbal)
                return Object("type", "Kerbal", "occupation", kerbal.occupation,
                    "fluidSuitCell", ParseResourceData(kerbal.suitCellResourcesData),
                    "hasMaterialSuitCell", kerbal.HasMaterialSuitCell,
                    "materialSuitCell", kerbal.materialSuitCellContents.Select(MaterialSnapshot).ToList());
            if (module is KhemistryISRU converter)
            {
                FieldInfo recipesField = typeof(KhemistryISRU).GetField("recipes", BindingFlags.Instance | BindingFlags.NonPublic);
                var recipes = recipesField?.GetValue(converter) as IEnumerable<KhemistryISRURecipe>;
                return Object("type", "KhemistryISRU", "converterName", converter.ConverterName,
                    "moduleType", converter.moduleType, "state", converter.state.ToString(),
                    "isRunning", converter.isRunning, "needsMaintenance", converter.needsMaintenance,
                    "status", converter.statusDisplay, "progress", converter.progressDisplay,
                    "batchProgress", converter.batchProgress, "resolvedRecipeTime", converter.resolvedRecipeTime,
                    "activeRecipe", converter.activeRecipeName,
                    "recipes", recipes?.Select(recipe => (object)recipe._name).ToList() ?? new List<object>());
            }
            return null;
        }

        private static object MaterialSnapshot(KhemistryMaterialInstance material)
        {
            if (material == null) return null;
            var parameters = material.parameters.Select(pair => (object)Object("name", pair.Key,
                "value", pair.Value, "derived", material.material?.IsDerivedParameter(pair.Key) ?? false)).ToList();
            return Object("name", material.material?.name, "shape", material.shape, "size", material.size,
                "amount", material.amount, "unitVolume", material.volume, "totalVolume", material.TotalVolume,
                "parameters", parameters);
        }

        private static Dictionary<string, object> ParseResourceData(string data)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (string entry in (data ?? "").Split('|'))
            {
                int separator = entry.LastIndexOf(':');
                if (separator <= 0 || !double.TryParse(entry.Substring(separator + 1), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double amount)) continue;
                result[entry.Substring(0, separator).Trim()] = amount;
            }
            return result;
        }

        private static Dictionary<string, object> ToObjectDictionary(IDictionary<string, double> values)
            => values.ToDictionary(pair => pair.Key, pair => (object)pair.Value, StringComparer.Ordinal);

        private static Vessel FindVessel(Dictionary<string, string> arguments)
        {
            string id = Get(arguments, "vessel_id", false);
            if (string.IsNullOrEmpty(id)) return SafeActiveVessel();
            return SafeLoadedVessels().FirstOrDefault(vessel =>
                string.Equals(vessel.id.ToString("D"), id, StringComparison.OrdinalIgnoreCase));
        }

        private static Vessel SafeActiveVessel()
        {
            if (!HighLogic.LoadedSceneIsFlight) return null;
            try { return FlightGlobals.ActiveVessel; }
            catch (NullReferenceException) { return null; }
        }

        private static IList<Vessel> SafeLoadedVessels()
        {
            if (!HighLogic.LoadedSceneIsFlight) return new List<Vessel>();
            try { return FlightGlobals.VesselsLoaded ?? (IList<Vessel>)new List<Vessel>(); }
            catch (NullReferenceException) { return new List<Vessel>(); }
        }

        private static double SafeUniversalTime()
        {
            try { return Planetarium.fetch == null ? 0.0 : Planetarium.GetUniversalTime(); }
            catch (NullReferenceException) { return 0.0; }
        }

        private static Part FindPart(Dictionary<string, string> arguments, bool required = true)
        {
            string raw = Get(arguments, "part_flight_id", false);
            if (string.IsNullOrEmpty(raw))
            {
                if (required) throw new ArgumentException("part_flight_id is required.");
                return null;
            }
            if (!uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint flightId))
                throw new ArgumentException("part_flight_id must be an unsigned integer.");
            Vessel vessel = FindVessel(arguments);
            return vessel?.parts.FirstOrDefault(part => part.flightID == flightId);
        }

        private static Dictionary<string, object> VesselSummary(Vessel vessel)
            => Object("id", vessel.id.ToString("D"), "name", vessel.vesselName,
                "loaded", vessel.loaded, "body", vessel.mainBody?.bodyName,
                "latitude", vessel.latitude, "longitude", vessel.longitude,
                "altitude", vessel.altitude, "partCount", vessel.parts?.Count ?? 0);

        private static string PartTitle(Part part) => part.partInfo?.title ?? part.name;
        private static List<object> Vector(double x, double y, double z) => new List<object> { x, y, z };

        private static Dictionary<string, object> Object(params object[] values)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < values.Length; i += 2) result[(string)values[i]] = values[i + 1];
            return result;
        }

        private static string Get(Dictionary<string, string> arguments, string name, bool required)
        {
            arguments.TryGetValue(name, out string value);
            if (required && string.IsNullOrWhiteSpace(value)) throw new ArgumentException(name + " is required.");
            return value;
        }

        private static int GetInt(Dictionary<string, string> arguments, string name, int fallback)
            => int.TryParse(Get(arguments, name, false), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int value) ? value : fallback;

        private void WriteSessionFile()
        {
            var session = Object("protocol", ProtocolMagic, "bridgeVersion", "0.1.0", "pipeName", pipeName,
                "token", token, "processId", System.Diagnostics.Process.GetCurrentProcess().Id,
                "kspRoot", KSPUtil.ApplicationRootPath, "createdUtc", DateTime.UtcNow.ToString("O"));
            File.WriteAllText(sessionPath, JsonWriter.Serialize(session), new UTF8Encoding(false));
        }

        private static string CreateToken()
        {
            byte[] bytes = new byte[32];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        private static bool ConstantTimeEquals(string expected, string actual)
        {
            byte[] left = Encoding.UTF8.GetBytes(expected ?? "");
            byte[] right = Encoding.UTF8.GetBytes(actual ?? "");
            int difference = left.Length ^ right.Length;
            int length = Math.Max(left.Length, right.Length);
            for (int i = 0; i < length; i++)
            {
                byte leftByte = i < left.Length ? left[i] : (byte)0;
                byte rightByte = i < right.Length ? right[i] : (byte)0;
                difference |= leftByte ^ rightByte;
            }
            return difference == 0;
        }

        private static string ReadBoundedString(BinaryReader reader)
        {
            string value = reader.ReadString();
            if (Encoding.UTF8.GetByteCount(value) > MaximumStringBytes)
                throw new InvalidDataException("String exceeds protocol limit.");
            return value;
        }
    }
}
