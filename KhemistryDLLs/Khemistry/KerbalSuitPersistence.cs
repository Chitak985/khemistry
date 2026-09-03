using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Khemistry
{
    /// <summary>
    /// Bridges Khemistry suit-cell and EVA-ISRU state across boarding. An EVA part is destroyed
    /// when its kerbal boards a vessel, so its PartModules cannot carry persistent fields to the
    /// next EVA. Snapshots here are transfer records: the boarding handler writes one and the
    /// next EVA consumes each component. While a kerbal is on EVA, the EVA vessel's normal
    /// PartModule save remains the authoritative copy.
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.SPACECENTER,
        GameScenes.EDITOR, GameScenes.FLIGHT, GameScenes.TRACKSTATION)]
    public class KhemistryKerbalSuitScenario : ScenarioModule
    {
        private const string SnapshotNodeName = "KERBAL_SUIT";
        private readonly Dictionary<string, ConfigNode> _snapshots =
            new Dictionary<string, ConfigNode>(StringComparer.Ordinal);

        public static KhemistryKerbalSuitScenario Instance { get; private set; }
        internal bool IsReady { get; private set; }

        public override void OnAwake()
        {
            base.OnAwake();
            Instance = this;
        }

        public void OnDestroy()
        {
            if (ReferenceEquals(Instance, this)) Instance = null;
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            IsReady = false;
            _snapshots.Clear();

            int invalidCount = 0;
            if (node != null)
            {
                foreach (ConfigNode savedNode in node.GetNodes(SnapshotNodeName))
                {
                    if (!TryNormalizeSnapshot(savedNode, out ConfigNode normalized,
                            out string key))
                    {
                        invalidCount++;
                        continue;
                    }

                    if (_snapshots.ContainsKey(key))
                    {
                        invalidCount++;
                        continue;
                    }
                    _snapshots.Add(key, normalized);
                }
            }

            if (invalidCount > 0)
                KShared.LogError("Ignored " + invalidCount
                    + " invalid or duplicate saved kerbal suit snapshot(s).",
                    "KhemistryKerbalSuitScenario/OnLoad");
            IsReady = true;
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            if (node == null) return;

            while (node.HasNode(SnapshotNodeName)) node.RemoveNode(SnapshotNodeName);
            foreach (ConfigNode snapshot in _snapshots.Values)
            {
                ConfigNode copy = new ConfigNode(SnapshotNodeName);
                snapshot.CopyTo(copy);
                node.AddNode(copy);
            }
        }

        internal void StoreBoardingSnapshot(KhemistryKerbal module, uint persistentId,
            string crewName)
        {
            if (!IsReady || module == null || !TryMakeKey(persistentId, crewName,
                    out string key))
                return;

            ConfigNode snapshot = module.ExportSuitPersistenceSnapshot()
                ?? new ConfigNode(SnapshotNodeName);
            CaptureEVAISRUStates(module.part, snapshot);
            RemoveMatchingSnapshots(persistentId, crewName);
            if (!HasSnapshotPayload(snapshot)) return;

            snapshot.AddValue("persistentId",
                persistentId.ToString(CultureInfo.InvariantCulture));
            snapshot.AddValue("crewName", crewName ?? string.Empty);
            _snapshots[key] = snapshot;
        }

        internal bool TryRestoreBoardingSnapshot(KhemistryKerbal module,
            uint persistentId, string crewName)
        {
            if (!IsReady || module == null || !TryFindSnapshot(persistentId, crewName,
                    out string key, out ConfigNode snapshot))
                return false;

            // A vessel-loaded EVA module is authoritative. Discard the older transfer record
            // as a whole rather than mixing its fluids/materials with newer live state.
            if (module.HasMeaningfulSuitPersistenceState())
            {
                RemoveSuitPayload(snapshot);
                RemoveSnapshotIfEmpty(key, snapshot);
                return false;
            }

            if (!module.ImportSuitPersistenceSnapshot(snapshot)) return false;
            RemoveSuitPayload(snapshot);
            RemoveSnapshotIfEmpty(key, snapshot);
            return true;
        }

        internal bool TryRestoreEVAISRUState(KhemistryISRU module)
        {
            if (!IsReady || module?.part == null
                || !TryGetCrewIdentity(module.part, out uint persistentId,
                    out string crewName)
                || !TryFindSnapshot(persistentId, crewName, out string key,
                    out ConfigNode snapshot)
                || !TryGetISRUIdentity(module, out ISRUIdentity targetIdentity))
                return false;

            ConfigNode matchedState = null;
            ConfigNode signatureFallback = null;
            foreach (ConfigNode candidate in snapshot.GetNodes("EVA_ISRU_STATE"))
            {
                if (!TryReadISRUIdentity(candidate, out ISRUIdentity savedIdentity))
                    continue;
                if (savedIdentity.Equals(targetIdentity))
                {
                    if (savedIdentity.moduleOrdinal == targetIdentity.moduleOrdinal)
                    {
                        matchedState = candidate;
                        break;
                    }
                    if (signatureFallback == null) signatureFallback = candidate;
                }
            }
            if (matchedState == null) matchedState = signatureFallback;
            if (matchedState == null) return false;

            // When this EVA was loaded from a vessel save, that module node is newer and
            // authoritative. Reconciliation succeeds by discarding the older transfer node.
            if (module.HasAuthoritativePersistentState)
            {
                snapshot.RemoveNode(matchedState);
                RemoveSnapshotIfEmpty(key, snapshot);
                return false;
            }

            ConfigNode[] moduleNodes = matchedState.GetNodes("MODULE");
            ConfigNode moduleNode = moduleNodes.Length == 1 ? moduleNodes[0] : null;
            if (moduleNode == null
                || !string.Equals(moduleNode.GetValue("name"), "KhemistryISRU",
                    StringComparison.Ordinal)
                || !module.LoadEVAISRUBoardingState(moduleNode))
            {
                KShared.LogError("Could not restore a saved kerbal EVA ISRU state; the transfer record was preserved for a later attempt.",
                    "KhemistryKerbalSuitScenario/TryRestoreEVAISRUState");
                return false;
            }

            snapshot.RemoveNode(matchedState);
            RemoveSnapshotIfEmpty(key, snapshot);
            return true;
        }

        private static void CaptureEVAISRUStates(Part part, ConfigNode snapshot)
        {
            if (part == null || snapshot == null) return;
            foreach (KhemistryISRU isru in part.FindModulesImplementing<KhemistryISRU>())
            {
                if (!TryGetISRUIdentity(isru, out ISRUIdentity identity)) continue;
                try
                {
                    ConfigNode savedModule = new ConfigNode("MODULE");
                    isru.Save(savedModule);
                    ConfigNode stateNode = new ConfigNode("EVA_ISRU_STATE");
                    WriteISRUIdentity(stateNode, identity);
                    stateNode.AddNode(savedModule);
                    snapshot.AddNode(stateNode);
                }
                catch (Exception ex)
                {
                    KShared.LogError("Could not capture kerbal EVA ISRU state while boarding: "
                        + ex.Message,
                        "KhemistryKerbalSuitScenario/CaptureEVAISRUStates");
                }
            }
        }

        private bool TryFindSnapshot(uint persistentId, string crewName,
            out string key, out ConfigNode snapshot)
        {
            snapshot = null;
            if (!string.IsNullOrWhiteSpace(crewName))
            {
                key = MakeNameKey(crewName.Trim());
                if (_snapshots.TryGetValue(key, out snapshot)) return true;

                // Migrate a name-only save once KSP has assigned a persistent id.
                foreach (KeyValuePair<string, ConfigNode> candidate in _snapshots)
                    if (string.Equals(candidate.Value.GetValue("crewName")?.Trim(),
                            crewName.Trim(), StringComparison.Ordinal))
                    {
                        key = candidate.Key;
                        snapshot = candidate.Value;
                        return true;
                    }
            }

            if (persistentId != 0)
            {
                key = MakeIdKey(persistentId);
                if (_snapshots.TryGetValue(key, out snapshot)) return true;
            }

            key = null;
            return false;
        }

        private void RemoveMatchingSnapshots(uint persistentId, string crewName)
        {
            var remove = new List<string>();
            foreach (KeyValuePair<string, ConfigNode> candidate in _snapshots)
            {
                if (persistentId != 0 && TryReadPersistentId(candidate.Value,
                        out uint savedId) && savedId == persistentId)
                    remove.Add(candidate.Key);
                else if (!string.IsNullOrWhiteSpace(crewName)
                    && string.Equals(candidate.Value.GetValue("crewName")?.Trim(),
                        crewName.Trim(), StringComparison.Ordinal))
                    remove.Add(candidate.Key);
            }
            foreach (string key in remove) _snapshots.Remove(key);
        }

        private static bool TryNormalizeSnapshot(ConfigNode source,
            out ConfigNode normalized, out string key)
        {
            normalized = null;
            key = null;
            if (source == null) return false;

            TryReadPersistentId(source, out uint persistentId);
            string crewName = source.GetValue("crewName")?.Trim();
            if (!TryMakeKey(persistentId, crewName, out key)) return false;

            normalized = new ConfigNode(SnapshotNodeName);
            normalized.AddValue("persistentId",
                persistentId.ToString(CultureInfo.InvariantCulture));
            normalized.AddValue("crewName", crewName ?? string.Empty);

            bool hasSuitPayload = source.HasValue("suitCellResourcesData")
                || source.HasValue("canBeOccupied")
                || source.HasNode("SUIT_STORED_MATERIAL");
            if (hasSuitPayload)
            {
                string resources = KhemistryEVACombinedProcessor.Serialize(
                    KhemistryEVACombinedProcessor.Deserialize(
                        source.GetValue("suitCellResourcesData")));

                bool canBeOccupied = true;
                string occupationSetting = source.GetValue("canBeOccupied");
                if (!string.IsNullOrEmpty(occupationSetting)
                    && !bool.TryParse(occupationSetting, out canBeOccupied))
                    return false;

                normalized.AddValue("suitCellResourcesData", resources);
                normalized.AddValue("canBeOccupied", canBeOccupied);

                // Material definitions/configuration may be unavailable while scenarios load.
                // Preserve every opaque material node; KhemistryKerbal validates it when the
                // matching EVA part and its suit-cell configuration are ready.
                foreach (ConfigNode materialNode in source.GetNodes("SUIT_STORED_MATERIAL"))
                {
                    ConfigNode copy = new ConfigNode("SUIT_STORED_MATERIAL");
                    materialNode.CopyTo(copy);
                    normalized.AddNode(copy);
                }
            }

            // Preserve malformed/unknown state nodes opaquely. Matching validates metadata and
            // the MODULE payload before it ever calls PartModule.Load, so keeping a node here is
            // safe and prevents a temporary version/config mismatch from erasing state.
            foreach (ConfigNode isruNode in source.GetNodes("EVA_ISRU_STATE"))
            {
                ConfigNode copy = new ConfigNode("EVA_ISRU_STATE");
                isruNode.CopyTo(copy);
                normalized.AddNode(copy);
            }
            // Suit and EVA-ISRU payloads are consumed independently. In particular, do not
            // manufacture an empty suit payload when a save contains only pending ISRU state.
            return HasSnapshotPayload(normalized);
        }

        private sealed class ISRUIdentity : IEquatable<ISRUIdentity>
        {
            public int moduleOrdinal;
            public int identityOccurrence;
            public string converterName;
            public string recipeType;
            public string recipeSubtype;
            public string recipeSubsubtype;

            public bool Equals(ISRUIdentity other)
                => other != null && identityOccurrence == other.identityOccurrence
                    && string.Equals(converterName, other.converterName,
                        StringComparison.Ordinal)
                    && string.Equals(recipeType, other.recipeType,
                        StringComparison.Ordinal)
                    && string.Equals(recipeSubtype, other.recipeSubtype,
                        StringComparison.Ordinal)
                    && string.Equals(recipeSubsubtype, other.recipeSubsubtype,
                        StringComparison.Ordinal);
        }

        private static bool TryGetISRUIdentity(KhemistryISRU module,
            out ISRUIdentity identity)
        {
            identity = null;
            if (module?.part == null) return false;

            var modules = module.part.FindModulesImplementing<KhemistryISRU>();
            int moduleOrdinal = modules.IndexOf(module);
            if (moduleOrdinal < 0) return false;
            ConfigNode config = KShared.FindModuleConfigNode(module, "KhemistryISRU");
            if (config == null || !string.Equals(config.GetValue("moduleType")?.Trim(),
                    "kerbalEVA", StringComparison.Ordinal))
                return false;

            identity = ReadConfigIdentity(config);
            identity.moduleOrdinal = moduleOrdinal;
            identity.identityOccurrence = 0;
            for (int i = 0; i < moduleOrdinal; i++)
            {
                ConfigNode precedingConfig = KShared.FindModuleConfigNode(modules[i],
                    "KhemistryISRU");
                if (precedingConfig == null) continue;
                ISRUIdentity preceding = ReadConfigIdentity(precedingConfig);
                if (HasSameSignature(identity, preceding)) identity.identityOccurrence++;
            }
            return true;
        }

        private static ISRUIdentity ReadConfigIdentity(ConfigNode config)
            => new ISRUIdentity
            {
                converterName = ReadIdentityValue(config, "ConverterName", "Converter"),
                recipeType = ReadIdentityValue(config, "recipeType", "kerbalEVA"),
                recipeSubtype = ReadIdentityValue(config, "recipeSubtype", ""),
                recipeSubsubtype = ReadIdentityValue(config, "recipeSubsubtype", "")
            };

        private static bool TryReadISRUIdentity(ConfigNode node,
            out ISRUIdentity identity)
        {
            identity = null;
            if (node == null
                || !int.TryParse(node.GetValue("moduleOrdinal"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int moduleOrdinal)
                || moduleOrdinal < 0
                || !int.TryParse(node.GetValue("identityOccurrence"),
                    NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int identityOccurrence)
                || identityOccurrence < 0
                || !string.Equals(node.GetValue("moduleType"), "kerbalEVA",
                    StringComparison.Ordinal))
                return false;

            identity = new ISRUIdentity
            {
                moduleOrdinal = moduleOrdinal,
                identityOccurrence = identityOccurrence,
                converterName = node.GetValue("converterName") ?? "",
                recipeType = node.GetValue("recipeType") ?? "",
                recipeSubtype = node.GetValue("recipeSubtype") ?? "",
                recipeSubsubtype = node.GetValue("recipeSubsubtype") ?? ""
            };
            return true;
        }

        private static void WriteISRUIdentity(ConfigNode node, ISRUIdentity identity)
        {
            node.AddValue("moduleOrdinal", identity.moduleOrdinal);
            node.AddValue("identityOccurrence", identity.identityOccurrence);
            node.AddValue("moduleType", "kerbalEVA");
            node.AddValue("converterName", identity.converterName);
            node.AddValue("recipeType", identity.recipeType);
            node.AddValue("recipeSubtype", identity.recipeSubtype);
            node.AddValue("recipeSubsubtype", identity.recipeSubsubtype);
        }

        private static bool HasSameSignature(ISRUIdentity left, ISRUIdentity right)
            => left != null && right != null
                && string.Equals(left.converterName, right.converterName,
                    StringComparison.Ordinal)
                && string.Equals(left.recipeType, right.recipeType,
                    StringComparison.Ordinal)
                && string.Equals(left.recipeSubtype, right.recipeSubtype,
                    StringComparison.Ordinal)
                && string.Equals(left.recipeSubsubtype, right.recipeSubsubtype,
                    StringComparison.Ordinal);

        private static string ReadIdentityValue(ConfigNode node, string name,
            string defaultValue)
        {
            string value = node?.GetValue(name)?.Trim();
            return string.IsNullOrEmpty(value) ? defaultValue : value;
        }

        private static bool TryGetCrewIdentity(Part part, out uint persistentId,
            out string crewName)
        {
            ProtoCrewMember crew = part?.protoModuleCrew?.FirstOrDefault();
            persistentId = crew?.persistentID ?? 0;
            crewName = crew?.name ?? "";
            return persistentId != 0 || !string.IsNullOrWhiteSpace(crewName);
        }

        private static void RemoveSuitPayload(ConfigNode snapshot)
        {
            while (snapshot.HasValue("suitCellResourcesData"))
                snapshot.RemoveValue("suitCellResourcesData");
            while (snapshot.HasValue("canBeOccupied"))
                snapshot.RemoveValue("canBeOccupied");
            while (snapshot.HasNode("SUIT_STORED_MATERIAL"))
                snapshot.RemoveNode("SUIT_STORED_MATERIAL");
        }

        private static bool HasSnapshotPayload(ConfigNode snapshot)
            => snapshot != null && (snapshot.HasValue("suitCellResourcesData")
                || snapshot.HasValue("canBeOccupied")
                || snapshot.HasNode("SUIT_STORED_MATERIAL")
                || snapshot.HasNode("EVA_ISRU_STATE"));

        private void RemoveSnapshotIfEmpty(string key, ConfigNode snapshot)
        {
            if (!HasSnapshotPayload(snapshot)) _snapshots.Remove(key);
        }

        private static bool TryReadPersistentId(ConfigNode node, out uint persistentId)
            => uint.TryParse(node?.GetValue("persistentId"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out persistentId);

        private static bool TryMakeKey(uint persistentId, string crewName, out string key)
        {
            // KSP deliberately assigns a new ProtoCrewMember.persistentID while boarding.
            // Crew names are unique in the roster and remain stable across EVA/boarding, so
            // use the name as the durable key and retain persistentID only as a fallback.
            if (!string.IsNullOrWhiteSpace(crewName))
            {
                key = MakeNameKey(crewName.Trim());
                return true;
            }
            if (persistentId != 0)
            {
                key = MakeIdKey(persistentId);
                return true;
            }
            key = null;
            return false;
        }

        private static string MakeIdKey(uint persistentId)
            => "id:" + persistentId.ToString(CultureInfo.InvariantCulture);

        private static string MakeNameKey(string crewName) => "name:" + crewName;
    }
}
