using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Khemistry
{
    /// <summary>
    /// Contains every single resource and recipe currently loaded by the game.
    /// Data is loaded by <see cref="KEILoader"/> and this shows the toolbar GUI.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KhemistryEngineeringInterface : MonoBehaviour  // KEI (minecraft reference)
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

        private void OnLauncherDestroyed() => _toolbarButton = null;

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
                _mainRect = GUILayout.Window(MainWindowId, _mainRect, DrawMainWindow, "Khemistry Engineering Interface", HighLogic.Skin.window);
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

            if (!KEILoader.IsLoaded)
                GUILayout.Label("Resources and recipes are still loading.", _wrapLabel);
            else
            {
                string filter = _searchText.Trim().ToLower();
                foreach (KhemistryResourceInfo res in KEILoader.Resources)
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
            _filteredRecipes = KEILoader.Recipes.Where(r =>
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
                GUILayout.Label("No recipes found.", _wrapLabel);
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
            if (!KEILoader.IsLoaded) return null;
            return KEILoader.Resources.FirstOrDefault(r => r.name == name);
        }
    }
}
