using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Khemistry
{
    public partial class KhemistryISRU
    {
        private const float ParallaxTargetScanInterval = 0.25f;
        private const double EvaParallaxInteractionDistance = 7.0;
        private const string FloatingPointPattern =
            @"[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?";

        private bool TryGetNearbyParallaxTarget(bool forceScan,
            out KhemistryParallaxTarget target)
        {
            target = null;
            if (_activeRecipe == null || !_activeRecipe.UsesParallaxScatters)
                return true;

            Part contextPart = GetPowerfailContextPart();
            Vessel processingVessel = GetProcessingVessel();
            if (contextPart == null || processingVessel?.mainBody == null) return false;
            Vector3 position = contextPart.transform.position;
            double interactionDistance = GetParallaxInteractionDistance();

            if (!forceScan && _parallaxTargetCache != null
                && KhemistryParallaxIntegration.IsTargetUsable(_parallaxTargetCache,
                    position, interactionDistance))
            {
                target = _parallaxTargetCache;
                return true;
            }

            float now = Time.realtimeSinceStartup;
            if (!forceScan && now < _nextParallaxTargetScan) return false;
            _nextParallaxTargetScan = now + ParallaxTargetScanInterval;
            _parallaxTargetCache = null;
            if (!KhemistryParallaxIntegration.TryFindClosest(
                    processingVessel.mainBody.bodyName, position,
                    _activeRecipe._parallaxScatters, interactionDistance,
                    out target))
                return false;
            _parallaxTargetCache = target;
            return true;
        }

        private double GetParallaxInteractionDistance()
        {
            // EVA converters make their PAW interaction distance unlimited because the PAW is
            // attached to the kerbal. Scatter harvesting still needs a physical reach limit.
            if (!IsEVAModuleType()) return _maxInteractionDistance;

            double distance = EvaParallaxInteractionDistance;
            if (_activeRecipe != null && _runtimeData != null)
            {
                KhemistryISRUBiomeConfig biome = _activeRecipe.GetBiomeConfig(
                    _runtimeData.planet, _runtimeData.biome);
                if (biome != null)
                    distance *= biome.maxInteractionDistanceMultiplier;
            }

            if (double.IsNaN(distance) || distance < 0.0) return 0.0;
            return double.IsInfinity(distance) || distance >= float.MaxValue
                ? float.MaxValue : distance;
        }

        private void ClearParallaxTargetCache()
        {
            _parallaxTargetCache = null;
            _nextParallaxTargetScan = 0f;
        }

        private static List<KhemistryISRURecipe.ResourceOutputMaterial>
            ResolveParallaxMaterialOutputs(
                IEnumerable<KhemistryISRURecipe.ResourceOutputMaterial> templates,
                KhemistryParallaxTarget target)
        {
            List<KhemistryISRURecipe.ResourceOutputMaterial> resolved =
                new List<KhemistryISRURecipe.ResourceOutputMaterial>();
            string radius = target.radius.ToString("R", CultureInfo.InvariantCulture);
            string height = target.height.ToString("R", CultureInfo.InvariantCulture);
            string volume = target.volume.ToString("R", CultureInfo.InvariantCulture);
            string scatter = target.identity.scatterName;

            foreach (KhemistryISRURecipe.ResourceOutputMaterial template in templates)
            {
                KhemistryISRURecipe.ResourceOutputMaterial output = template;
                bool usesParallaxValues =
                    KhemistryISRURecipe.MaterialOutputUsesParallaxValues(template);
                output.size = ResolveParallaxValue(template.size, radius, height,
                    volume, scatter);
                output.outVolume = ResolveParallaxValue(template.outVolume, radius,
                    height, volume, scatter);
                output.parameters = new Dictionary<string, string>();
                foreach (KeyValuePair<string, string> parameter in
                         template.parameters ?? new Dictionary<string, string>())
                    output.parameters[parameter.Key] = ResolveParallaxValue(parameter.Value,
                        radius, height, volume, scatter);
                output.parameterAssignments = template.parameterAssignments == null
                    ? null
                    : template.parameterAssignments.Select(parameter =>
                        new KeyValuePair<string, string>(parameter.Key,
                            ResolveParallaxValue(parameter.Value, radius, height,
                                volume, scatter))).ToList();
                output.parallaxResolved = usesParallaxValues;
                resolved.Add(output);
            }
            return resolved;
        }

        private static string ResolveParallaxValue(string value, string radius,
            string height, string volume, string scatter)
        {
            if (value == null) return null;
            return value.Replace(KhemistryISRURecipe.ParallaxRadiusValue, radius)
                .Replace(KhemistryISRURecipe.ParallaxHeightValue, height)
                .Replace(KhemistryISRURecipe.ParallaxVolumeValue, volume)
                .Replace(KhemistryISRURecipe.ParallaxScatterValue, scatter);
        }

        private static bool IsSavedParallaxMaterialRealization(
            KhemistryISRURecipe.ResourceOutputMaterial saved,
            KhemistryISRURecipe.ResourceOutputMaterial template)
        {
            if (!saved.parallaxResolved
                || !KhemistryISRURecipe.MaterialOutputUsesParallaxValues(template)
                || saved.name != template.name || saved.shape != template.shape
                || !ParallaxTemplateValueMatches(template.size, saved.size)
                || !ParallaxTemplateValueMatches(template.outVolume, saved.outVolume)
                || saved.parameters == null || template.parameters == null
                || saved.parameters.Count != template.parameters.Count)
                return false;

            foreach (KeyValuePair<string, string> parameter in template.parameters)
                if (!saved.parameters.TryGetValue(parameter.Key, out string savedValue)
                    || !ParallaxTemplateValueMatches(parameter.Value, savedValue))
                    return false;

            if (template.parameterAssignments == null
                || saved.parameterAssignments == null)
                return true;
            if (template.parameterAssignments.Count != saved.parameterAssignments.Count)
                return false;
            for (int index = 0; index < template.parameterAssignments.Count; index++)
            {
                KeyValuePair<string, string> expected = template.parameterAssignments[index];
                KeyValuePair<string, string> actual = saved.parameterAssignments[index];
                if (!string.Equals(expected.Key, actual.Key, StringComparison.Ordinal)
                    || !ParallaxTemplateValueMatches(expected.Value, actual.Value))
                    return false;
            }
            return true;
        }

        private static bool ParallaxTemplateValueMatches(string template, string actual)
        {
            if (template == null || actual == null) return template == actual;
            if (!KhemistryISRURecipe.ContainsParallaxValue(template))
                return string.Equals(template, actual, StringComparison.Ordinal);
            if (KhemistryISRURecipe.ContainsParallaxValue(actual)) return false;

            string pattern = Regex.Escape(template)
                .Replace(Regex.Escape(KhemistryISRURecipe.ParallaxRadiusValue),
                    "(?:" + FloatingPointPattern + ")")
                .Replace(Regex.Escape(KhemistryISRURecipe.ParallaxHeightValue),
                    "(?:" + FloatingPointPattern + ")")
                .Replace(Regex.Escape(KhemistryISRURecipe.ParallaxVolumeValue),
                    "(?:" + FloatingPointPattern + ")")
                .Replace(Regex.Escape(KhemistryISRURecipe.ParallaxScatterValue),
                    ".+");
            return Regex.IsMatch(actual, "^(?:" + pattern + ")$",
                RegexOptions.CultureInvariant);
        }
    }
}
