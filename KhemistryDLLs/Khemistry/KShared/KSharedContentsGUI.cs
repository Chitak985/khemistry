using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Khemistry
{
    public partial class KShared
    {
        private const float MaterialNameWidth = 180f;
        private const float MaterialSizeWidth = 105f;
        private const float MaterialShapeWidth = 105f;
        private const float MaterialAmountWidth = 75f;
        private const float MaterialVolumeWidth = 120f;
        private const float MaterialParametersWidth = 100f;

        private const float ResourceNameWidth = 250f;
        private const float ResourceAmountWidth = 160f;
        private const float ResourceKeiWidth = 90f;

        private bool _materialContentsVisible;
        private string _materialContentsTitle = "Material Contents";
        private Func<IEnumerable<KhemistryMaterialInstance>> _materialContentsProvider;
        private Rect _materialContentsRect = new Rect(0f, 0f, 760f, 450f);
        private Vector2 _materialContentsScroll = Vector2.zero;
        private int _materialContentsWindowId;

        private bool _materialParametersVisible;
        private string _materialParametersTitle = "Material Parameters";
        private readonly List<KeyValuePair<string, string>> _materialParameterRows
            = new List<KeyValuePair<string, string>>();
        private Rect _materialParametersRect = new Rect(0f, 0f, 680f, 400f);
        private Vector2 _materialParametersScroll = Vector2.zero;
        private int _materialParametersWindowId;

        private bool _resourceContentsVisible;
        private string _resourceContentsTitle = "Cell Contents";
        private Func<IDictionary<string, double>> _resourceContentsProvider;
        private Rect _resourceContentsRect = new Rect(0f, 0f, 560f, 420f);
        private Vector2 _resourceContentsScroll = Vector2.zero;
        private int _resourceContentsWindowId;

        private GUIStyle _contentsHeaderStyle;
        private GUIStyle _contentsWrappedLabelStyle;

        private static Rect CenterContentsWindow(Rect window)
        {
            window.x = Mathf.Max(0f, (Screen.width - window.width) / 2f);
            window.y = Mathf.Max(0f, (Screen.height - window.height) / 2f);
            return window;
        }

        private void EnsureContentsStyles()
        {
            if (_contentsHeaderStyle != null && _contentsWrappedLabelStyle != null)
                return;

            _contentsHeaderStyle = new GUIStyle(HighLogic.Skin.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            _contentsWrappedLabelStyle = new GUIStyle(HighLogic.Skin.label)
            {
                wordWrap = true,
                alignment = TextAnchor.MiddleLeft
            };
        }

        /// <summary>Shows the live contents of a material container.</summary>
        public void ShowMaterialContents(string title,
            Func<IEnumerable<KhemistryMaterialInstance>> contentsProvider)
        {
            if (contentsProvider == null) return;
            _materialContentsTitle = string.IsNullOrWhiteSpace(title)
                ? "Material Contents" : title;
            _materialContentsProvider = contentsProvider;
            _materialContentsScroll = Vector2.zero;
            _materialContentsRect = CenterContentsWindow(_materialContentsRect);
            _materialParametersVisible = false;
            _materialParameterRows.Clear();
            _materialContentsVisible = true;
        }

        /// <summary>Shows the live contents of a dictionary-backed resource cell.</summary>
        public void ShowResourceContents(string title,
            Func<IDictionary<string, double>> contentsProvider)
        {
            if (contentsProvider == null) return;
            _resourceContentsTitle = string.IsNullOrWhiteSpace(title)
                ? "Cell Contents" : title;
            _resourceContentsProvider = contentsProvider;
            _resourceContentsScroll = Vector2.zero;
            _resourceContentsRect = CenterContentsWindow(_resourceContentsRect);
            _resourceContentsVisible = true;
        }

        private void CloseContentsWindows()
        {
            _materialContentsVisible = false;
            _materialParametersVisible = false;
            _resourceContentsVisible = false;
            _materialContentsProvider = null;
            _resourceContentsProvider = null;
            _materialParameterRows.Clear();
        }

        private void DrawContentsWindows()
        {
            if (!_materialContentsVisible && !_materialParametersVisible
                && !_resourceContentsVisible)
                return;

            EnsureContentsStyles();
            if (_materialContentsVisible)
                _materialContentsRect = GUILayout.Window(
                    _materialContentsWindowId,
                    _materialContentsRect,
                    DrawMaterialContentsWindow,
                    _materialContentsTitle,
                    HighLogic.Skin.window);
            if (_materialParametersVisible)
                _materialParametersRect = GUILayout.Window(
                    _materialParametersWindowId,
                    _materialParametersRect,
                    DrawMaterialParametersWindow,
                    _materialParametersTitle,
                    HighLogic.Skin.window);
            if (_resourceContentsVisible)
                _resourceContentsRect = GUILayout.Window(
                    _resourceContentsWindowId,
                    _resourceContentsRect,
                    DrawResourceContentsWindow,
                    _resourceContentsTitle,
                    HighLogic.Skin.window);
        }

        private List<KhemistryMaterialInstance> ReadMaterialContents()
        {
            try
            {
                IEnumerable<KhemistryMaterialInstance> source
                    = _materialContentsProvider?.Invoke();
                if (source == null)
                {
                    _materialContentsProvider = null;
                    return new List<KhemistryMaterialInstance>();
                }
                return source.Where(material => material?.material != null
                    && material.amount > 0).ToList();
            }
            catch (Exception exception)
            {
                KShared.LogError("Could not read material contents: " + exception.Message,
                    "KShared/ReadMaterialContents");
                _materialContentsProvider = null;
                return new List<KhemistryMaterialInstance>();
            }
        }

        private void DrawMaterialContentsWindow(int windowId)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _contentsHeaderStyle,
                GUILayout.Width(MaterialNameWidth));
            GUILayout.Label("Size", _contentsHeaderStyle,
                GUILayout.Width(MaterialSizeWidth));
            GUILayout.Label("Shape", _contentsHeaderStyle,
                GUILayout.Width(MaterialShapeWidth));
            GUILayout.Label("Amount", _contentsHeaderStyle,
                GUILayout.Width(MaterialAmountWidth));
            GUILayout.Label("Volume", _contentsHeaderStyle,
                GUILayout.Width(MaterialVolumeWidth));
            GUILayout.Label("Parameters", _contentsHeaderStyle,
                GUILayout.Width(MaterialParametersWidth));
            GUILayout.EndHorizontal();

            float scrollHeight = Mathf.Min(340f,
                Mathf.Max(120f, Screen.height - 180f));
            _materialContentsScroll = GUILayout.BeginScrollView(
                _materialContentsScroll,
                HighLogic.Skin.scrollView,
                GUILayout.Height(scrollHeight));

            List<KhemistryMaterialInstance> materials = ReadMaterialContents();
            if (materials.Count == 0)
            {
                GUILayout.Label(_materialContentsProvider == null
                    ? "Storage is no longer available." : "Empty",
                    _contentsWrappedLabelStyle);
            }
            else
            {
                foreach (KhemistryMaterialInstance material in materials)
                {
                    material.UpdateParams("KShared/DrawMaterialContentsWindow");
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(material.material.name,
                        _contentsWrappedLabelStyle,
                        GUILayout.Width(MaterialNameWidth));
                    GUILayout.Label(material.size ?? "",
                        _contentsWrappedLabelStyle,
                        GUILayout.Width(MaterialSizeWidth));
                    GUILayout.Label(material.shape ?? "",
                        _contentsWrappedLabelStyle,
                        GUILayout.Width(MaterialShapeWidth));
                    GUILayout.Label(material.amount.ToString(
                            CultureInfo.InvariantCulture),
                        _contentsWrappedLabelStyle,
                        GUILayout.Width(MaterialAmountWidth));
                    GUILayout.Label(material.volume.ToString("G9",
                            CultureInfo.InvariantCulture),
                        _contentsWrappedLabelStyle,
                        GUILayout.Width(MaterialVolumeWidth));
                    if (GUILayout.Button("Open List", HighLogic.Skin.button,
                            GUILayout.Width(MaterialParametersWidth)))
                        ShowMaterialParameters(material);
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndScrollView();
            if (GUILayout.Button("Close", HighLogic.Skin.button))
            {
                _materialContentsVisible = false;
                _materialContentsProvider = null;
                _materialParametersVisible = false;
                _materialParameterRows.Clear();
            }
            GUI.DragWindow();
        }

        private void ShowMaterialParameters(KhemistryMaterialInstance material)
        {
            _materialParameterRows.Clear();
            if (material?.material == null) return;

            material.UpdateParams("KShared/ShowMaterialParameters");
            var added = new HashSet<string>(StringComparer.Ordinal);
            foreach (string parameterName in material.material.parameterOrder)
                AddMaterialParameterRow(material, parameterName, added);
            foreach (string parameterName in material.parameters.Keys
                         .OrderBy(name => name, StringComparer.Ordinal))
                AddMaterialParameterRow(material, parameterName, added);

            _materialParametersTitle = material.material.name + " Parameters";
            _materialParametersScroll = Vector2.zero;
            _materialParametersRect = CenterContentsWindow(_materialParametersRect);
            _materialParametersVisible = true;
        }

        private void AddMaterialParameterRow(KhemistryMaterialInstance material,
            string parameterName, ISet<string> added)
        {
            if (string.IsNullOrEmpty(parameterName) || !added.Add(parameterName)
                || !material.parameters.TryGetValue(parameterName, out string value))
                return;

            string displayName = material.material.IsDerivedParameter(parameterName)
                ? parameterName + " (derived)" : parameterName;
            _materialParameterRows.Add(new KeyValuePair<string, string>(
                displayName, value ?? ""));
        }

        private void DrawMaterialParametersWindow(int windowId)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _contentsHeaderStyle, GUILayout.Width(220f));
            GUILayout.Label("Value", _contentsHeaderStyle, GUILayout.Width(400f));
            GUILayout.EndHorizontal();

            float scrollHeight = Mathf.Min(290f,
                Mathf.Max(120f, Screen.height - 180f));
            _materialParametersScroll = GUILayout.BeginScrollView(
                _materialParametersScroll,
                HighLogic.Skin.scrollView,
                GUILayout.Height(scrollHeight));
            if (_materialParameterRows.Count == 0)
                GUILayout.Label("No parameters", _contentsWrappedLabelStyle);
            else
                foreach (KeyValuePair<string, string> parameter in
                         _materialParameterRows)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(parameter.Key, _contentsWrappedLabelStyle,
                        GUILayout.Width(220f));
                    GUILayout.Label(parameter.Value, _contentsWrappedLabelStyle,
                        GUILayout.Width(400f));
                    GUILayout.EndHorizontal();
                }
            GUILayout.EndScrollView();

            if (GUILayout.Button("Close", HighLogic.Skin.button))
                _materialParametersVisible = false;
            GUI.DragWindow();
        }

        private List<KeyValuePair<string, double>> ReadResourceContents()
        {
            try
            {
                IDictionary<string, double> source
                    = _resourceContentsProvider?.Invoke();
                if (source == null)
                {
                    _resourceContentsProvider = null;
                    return new List<KeyValuePair<string, double>>();
                }
                return source.Where(resource => !string.IsNullOrWhiteSpace(resource.Key)
                        && IsFinite(resource.Value) && resource.Value > 0.0)
                    .OrderBy(resource => resource.Key, StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception exception)
            {
                KShared.LogError("Could not read resource-cell contents: "
                    + exception.Message, "KShared/ReadResourceContents");
                _resourceContentsProvider = null;
                return new List<KeyValuePair<string, double>>();
            }
        }

        private void DrawResourceContentsWindow(int windowId)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _contentsHeaderStyle,
                GUILayout.Width(ResourceNameWidth));
            GUILayout.Label("Amount", _contentsHeaderStyle,
                GUILayout.Width(ResourceAmountWidth));
            GUILayout.Label("KEI Page", _contentsHeaderStyle,
                GUILayout.Width(ResourceKeiWidth));
            GUILayout.EndHorizontal();

            float scrollHeight = Mathf.Min(310f,
                Mathf.Max(120f, Screen.height - 180f));
            _resourceContentsScroll = GUILayout.BeginScrollView(
                _resourceContentsScroll,
                HighLogic.Skin.scrollView,
                GUILayout.Height(scrollHeight));

            List<KeyValuePair<string, double>> resources = ReadResourceContents();
            if (resources.Count == 0)
            {
                GUILayout.Label(_resourceContentsProvider == null
                    ? "Cell is no longer available." : "Empty",
                    _contentsWrappedLabelStyle);
            }
            else
            {
                foreach (KeyValuePair<string, double> resource in resources)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(resource.Key, _contentsWrappedLabelStyle,
                        GUILayout.Width(ResourceNameWidth));
                    GUILayout.Label(resource.Value.ToString("G10",
                            CultureInfo.InvariantCulture),
                        _contentsWrappedLabelStyle,
                        GUILayout.Width(ResourceAmountWidth));
                    if (GUILayout.Button("Open", HighLogic.Skin.button,
                            GUILayout.Width(ResourceKeiWidth))
                        && !KhemistryEngineeringInterface.OpenResourcePage(
                            resource.Key))
                        ScreenMessages.PostScreenMessage(new ScreenMessage(
                            "No KEI page is available for " + resource.Key + ".",
                            5f, ScreenMessageStyle.UPPER_CENTER));
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndScrollView();
            if (GUILayout.Button("Close", HighLogic.Skin.button))
            {
                _resourceContentsVisible = false;
                _resourceContentsProvider = null;
            }
            GUI.DragWindow();
        }
    }
}
