using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Khemistry;
using KSP.UI.Screens;
using UnityEngine;
using Data = KhemistryConstructionOverhaul.KSCResourceListData;

namespace KhemistryConstructionOverhaul
{
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public sealed class KSCResourceWindow : MonoBehaviour
    {
        private const int WindowId = 856220, SellId = 856221;
        private const string LockName = "Khemistry.KSCResourceWindow";
        private ApplicationLauncherButton _button;
        private Texture2D _icon;
        private bool _visible, _hidden, _resourcesOpen, _materialsOpen, _unitOpen;
        private Rect _window = new Rect(80, 80, 920, 560), _sell = new Rect(140, 140, 360, 220);
        private Vector2 _scroll;
        private string _unit = "kg", _sellName, _amount = "1", _error = "";
        private Game _saleGame;
        private GUIStyle _label, _fold;
        private readonly Dictionary<string, Data.Sort> _sorts = new Dictionary<string, Data.Sort>();
        private readonly HashSet<string> _expanded = new HashSet<string>();
        private readonly Dictionary<string, Vector2> _parameterScroll = new Dictionary<string, Vector2>();
        private readonly List<ResourceRow> _resources = new List<ResourceRow>();
        private readonly List<Data.MaterialRow> _materials = new List<Data.MaterialRow>();

        private sealed class ResourceRow
        {
            public string name, displayName, abbreviation;
            public double density, amount;
        }

        private static bool InSave => HighLogic.CurrentGame != null &&
            (HighLogic.LoadedScene == GameScenes.SPACECENTER || HighLogic.LoadedScene == GameScenes.EDITOR
            || HighLogic.LoadedScene == GameScenes.FLIGHT || HighLogic.LoadedScene == GameScenes.TRACKSTATION);
        private static bool LedgerReady => KhemistryConstructionResourcesScenario.Instance?.LedgerReady == true;

        public void Awake()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(LauncherDestroyed);
            GameEvents.onHideUI.Add(HideUI);
            GameEvents.onShowUI.Add(ShowUI);
            // A small code-drawn inventory/list glyph, independent of optional texture packs.
            _icon = new Texture2D(38, 38, TextureFormat.RGBA32, false);
            var pixels = new Color[38 * 38];
            for (int y = 0; y < 38; y++)
                for (int x = 0; x < 38; x++)
                {
                    bool border = x >= 5 && x <= 32 && y >= 5 && y <= 32
                        && (x <= 7 || x >= 30 || y <= 7 || y >= 30);
                    bool line = (y >= 12 && y <= 14 || y >= 19 && y <= 21 || y >= 26 && y <= 28)
                        && (x >= 10 && x <= 13 || x >= 17 && x <= 27);
                    pixels[y * 38 + x] = border || line ? new Color(.45f, .9f, .65f, 1) : Color.clear;
                }
            _icon.SetPixels(pixels); _icon.Apply();
            AddButton();
        }

        private void AddButton()
        {
            if (!InSave || _button != null || _icon == null || !ApplicationLauncher.Ready
                || ApplicationLauncher.Instance == null) return;
            _button = ApplicationLauncher.Instance.AddModApplication(
                () => _visible = true, Close, null, null, null, null,
                ApplicationLauncher.AppScenes.ALWAYS, _icon);
        }
        private void LauncherDestroyed() { _button = null; Close(); }
        private void HideUI() { _hidden = true; InputLockManager.RemoveControlLock(LockName); }
        private void ShowUI() { _hidden = false; }
        private void Close()
        {
            _visible = false; _sellName = null; _saleGame = null;
            InputLockManager.RemoveControlLock(LockName);
        }
        public void OnDestroy()
        {
            Close();
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(LauncherDestroyed);
            GameEvents.onHideUI.Remove(HideUI);
            GameEvents.onShowUI.Remove(ShowUI);
            if (_button != null && ApplicationLauncher.Instance != null)
                ApplicationLauncher.Instance.RemoveModApplication(_button);
            if (_icon != null) Destroy(_icon);
        }
        public void Update()
        {
            if (!InSave) { Close(); return; }
            AddButton();
            Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (_visible && !_hidden && (_window.Contains(mouse) || _sellName != null))
                InputLockManager.SetControlLock(ControlTypes.ALLBUTCAMERAS, LockName);
            else InputLockManager.RemoveControlLock(LockName);
        }

        private void OnGUI()
        {
            if (!_visible || _hidden || !InSave) return;
            if (_label == null)
            {
                _label = new GUIStyle(HighLogic.Skin.label) { wordWrap = true };
                _fold = new GUIStyle(HighLogic.Skin.button) { alignment = TextAnchor.MiddleLeft };
            }
            // Refresh only at Layout so changing amounts cannot unbalance IMGUI's control tree.
            if (Event.current.type == EventType.Layout) ReadContents();
            _window.width = Mathf.Min(920, Mathf.Max(300, Screen.width - 20));
            _window.height = Mathf.Min(560, Mathf.Max(180, Screen.height - 20));
            Clamp(ref _window);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && _sellName == null;
            _window = GUILayout.Window(WindowId, _window, DrawWindow, "", HighLogic.Skin.window);
            GUI.enabled = enabled;
            if (_sellName != null)
            {
                _sell.width = Mathf.Min(360, Screen.width - 20);
                Clamp(ref _sell);
                _sell = GUILayout.Window(SellId, _sell, DrawSell, "", HighLogic.Skin.window);
            }
        }

        private static void Clamp(ref Rect rect)
        {
            rect.x = Mathf.Clamp(rect.x, 0, Mathf.Max(0, Screen.width - rect.width));
            rect.y = Mathf.Clamp(rect.y, 0, Mathf.Max(0, Screen.height - rect.height));
        }
        private void ReadContents()
        {
            _resources.Clear(); _materials.Clear();
            if (!LedgerReady || KShared.Instance == null) return;
            foreach (var pair in KShared.Instance.ResourceDict.ToArray())
            {
                if (!Data.Finite(pair.Value) || pair.Value <= 0) continue;
                var definition = PartResourceLibrary.Instance?.GetDefinition(pair.Key);
                _resources.Add(new ResourceRow
                {
                    name = pair.Key, displayName = definition?.displayName ?? pair.Key,
                    abbreviation = definition?.abbreviation ?? "-", density = definition?.density ?? 0,
                    amount = pair.Value
                });
            }
            foreach (var material in KShared.Instance.KSCMaterialContents.ToArray())
            {
                if (material?.material == null || material.amount <= 0) continue;
                // The copy constructor derives parameters without changing the saved instance.
                var copy = new KhemistryMaterialInstance(material);
                _materials.Add(new Data.MaterialRow
                {
                    name = copy.material.name, shape = copy.shape, size = copy.size,
                    amount = copy.amount, parameters = new Dictionary<string, string>(copy.parameters)
                });
            }
        }

        private Data.Sort Sorting(string key, string initial = "Total Amount")
        {
            if (!_sorts.TryGetValue(key, out Data.Sort sort)) _sorts[key] = sort = new Data.Sort(initial);
            return sort;
        }
        private void Header(string label, string column, Data.Sort sort, float width)
        {
            string arrow = sort.column == column ? (sort.descending ? " v" : " ^") : "";
            if (GUILayout.Button(label + arrow, HighLogic.Skin.button, GUILayout.Width(width))) sort.Select(column);
        }
        private void Cell(string value, float width)
            => GUILayout.Label(value ?? "-", _label, GUILayout.Width(width));
        private bool Fold(string label, bool open)
            => GUILayout.Toggle(open, (open ? "v  " : ">  ") + label, _fold, GUILayout.ExpandWidth(true));
        private bool Expanded(string key, string text, float width)
        {
            bool open = _expanded.Contains(key);
            bool next = GUILayout.Toggle(open, (open ? "v  " : ">  ") + text, _fold, GUILayout.Width(width));
            if (next) _expanded.Add(key); else _expanded.Remove(key);
            return next;
        }
        private void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("KSC Resource List", _label, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("X", HighLogic.Skin.button, GUILayout.Width(28)))
            {
                Close(); _button?.SetFalse(false);
            }
            GUILayout.EndHorizontal();
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Width(_window.width - 20),
                GUILayout.Height(_window.height - 65));
            if (!LedgerReady) GUILayout.Label("KSC contents are loading.", _label);
            else
            {
                _resourcesOpen = Fold("Resources", _resourcesOpen);
                if (_resourcesOpen) DrawResources();
                _materialsOpen = Fold("Materials", _materialsOpen);
                if (_materialsOpen) DrawGroups("materials", _materials, 0, Mathf.Max(240, _window.width - 52));
            }
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, _window.width - 45, 40));
        }

        private void DrawResources()
        {
            float width = Mathf.Max(600, _window.width - 60);
            float nameWidth = width - 440;
            var sort = Sorting("resources", "Name");
            GUILayout.BeginVertical(HighLogic.Skin.box, GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            Header("Name", "Name", sort, nameWidth);
            Header("Abbreviation", "Abbreviation", sort, 110);
            if (GUILayout.Button(_unit + " v", HighLogic.Skin.button, GUILayout.Width(40))) _unitOpen = !_unitOpen;
            Header("/unit", "Density", sort, 65);
            Header("Amount", "Amount", sort, 100);
            Header("Actions", "Actions", sort, 125);
            GUILayout.EndHorizontal();
            if (_unitOpen)
            {
                GUILayout.BeginHorizontal(); GUILayout.Space(nameWidth + 110);
                foreach (string unit in new[] { "g", "kg", "ton" })
                    if (GUILayout.Button(unit, HighLogic.Skin.button, GUILayout.Width(48))) { _unit = unit; _unitOpen = false; }
                GUILayout.EndHorizontal();
            }
            foreach (var row in Data.Sorted(_resources, sort, r => sort.column == "Abbreviation" ? r.abbreviation
                : sort.column == "Density" ? Data.Number(Data.Density(r.density, _unit))
                : sort.column == "Amount" ? Data.Number(r.amount)
                : sort.column == "Actions" ? "Info Sell" : r.displayName, r => r.name))
            {
                GUILayout.BeginHorizontal();
                Cell(row.displayName, nameWidth); Cell(row.abbreviation, 110);
                Cell(Data.Number(Data.Density(row.density, _unit)), 109); Cell(Data.Number(row.amount), 100);
                if (GUILayout.Button("Info", HighLogic.Skin.button, GUILayout.Width(59)))
                    if (!KhemistryEngineeringInterface.OpenResourcePage(row.name))
                        ScreenMessages.PostScreenMessage("KEI information is not available yet.", 4f, ScreenMessageStyle.UPPER_CENTER);
                if (GUILayout.Button("Sell", HighLogic.Skin.button, GUILayout.Width(62)))
                {
                    _sellName = row.name; _saleGame = HighLogic.CurrentGame; _amount = "1"; _error = "";
                    _sell.x = _window.x + 60; _sell.y = _window.y + 60;
                }
                GUILayout.EndHorizontal();
            }
            if (_resources.Count == 0) GUILayout.Label("No stored resources.", _label);
            GUILayout.EndVertical();
        }

        private void DrawGroups(string path, List<Data.MaterialRow> rows, int level, float width)
        {
            GUILayout.BeginHorizontal(); GUILayout.Space(12);
            GUILayout.BeginVertical(HighLogic.Skin.box, GUILayout.Width(width - 24));
            width -= 40;
            var groups = Data.Groups(rows,
                r => level == 0 ? r.name : level == 1 ? Data.ParameterKey(r) : level == 2 ? r.shape : r.size,
                r => level == 0 ? r.name : level == 1 ? "" : level == 2 ? r.shape : r.size);
            var sort = Sorting(path);
            string heading = level == 0 ? "Name" : level == 2 ? "Shape" : "Size";
            string[] parameters = level == 1 ? Data.ParameterNames(rows) : new string[0];
            float firstWidth = Mathf.Max(80, width - 124);
            float parameterWidth = parameters.Length == 0 ? firstWidth : Mathf.Max(140, firstWidth / parameters.Length);
            bool horizontal = level == 1;
            if (horizontal)
            {
                _parameterScroll.TryGetValue(path, out Vector2 scroll);
                _parameterScroll[path] = GUILayout.BeginScrollView(scroll, true, false,
                    GUILayout.Width(width),
                    GUILayout.Height(Mathf.Min(350, 90 + groups.Count * 38 + _expanded.Count * 50)));
            }
            GUILayout.BeginHorizontal();
            if (horizontal && parameters.Length > 0)
                foreach (string parameter in parameters) Header(parameter, "param:" + parameter, sort, parameterWidth);
            else Header(horizontal ? "Parameters" : heading, heading, sort, firstWidth);
            Header("Total Amount", "Total Amount", sort, 120);
            GUILayout.EndHorizontal();
            foreach (var group in Data.Sorted(groups, sort, g =>
            {
                if (sort.column == "Total Amount") return g.amount.ToString(CultureInfo.InvariantCulture);
                if (sort.column.StartsWith("param:", StringComparison.Ordinal))
                    return g.rows[0].parameters.TryGetValue(sort.column.Substring(6), out string value) ? value : "";
                return g.label;
            }, g => g.key))
            {
                string key = path + "/" + Data.Token(group.key);
                bool open = false;
                GUILayout.BeginHorizontal();
                if (horizontal && parameters.Length > 0)
                {
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        string value = group.rows[0].parameters.TryGetValue(parameters[i], out string v) ? v : "(missing)";
                        if (i == 0) open = Expanded(key, value, parameterWidth);
                        else Cell(value, parameterWidth);
                    }
                }
                else if (level < 3) open = Expanded(key, horizontal ? "(No parameters)" : group.label, firstWidth);
                else Cell(group.label, firstWidth);
                Cell(group.amount.ToString(CultureInfo.InvariantCulture), 120);
                GUILayout.EndHorizontal();
                if (open && level < 3) DrawGroups(key, group.rows, level + 1, width);
            }
            if (groups.Count == 0) GUILayout.Label("No stored materials.", _label);
            if (horizontal) GUILayout.EndScrollView();
            GUILayout.EndVertical(); GUILayout.Space(12); GUILayout.EndHorizontal();
        }

        private void DrawSell(int id)
        {
            var definition = PartResourceLibrary.Instance?.GetDefinition(_sellName);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sell " + (definition?.displayName ?? _sellName), _label, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("X", HighLogic.Skin.button, GUILayout.Width(28))) { _sellName = null; }
            GUILayout.EndHorizontal();
            if (_sellName == null) return;
            GUILayout.Label("Amount (units):", _label);
            _amount = GUILayout.TextField(_amount, 80, HighLogic.Skin.textField);
            bool career = HighLogic.CurrentGame.Mode == Game.Modes.CAREER;
            bool valid = definition != null && LedgerReady && ReferenceEquals(_saleGame, HighLogic.CurrentGame);
            double proceeds = 0;
            string validation = "Resource storage is unavailable.";
            if (valid) valid = Data.TrySale(KShared.Instance.ResourceDict, _sellName, _amount, career,
                definition.unitCost, out _, out proceeds, out validation);
            GUILayout.Label(career ? "Funds: " + Data.Number(proceeds)
                : "Science/Sandbox: resources are removed without payment.", _label);
            if (!string.IsNullOrEmpty(_error)) GUILayout.Label(_error, _label);
            else if (!valid) GUILayout.Label(validation, _label);
            GUILayout.BeginHorizontal();
            bool enabled = GUI.enabled; GUI.enabled = enabled && valid;
            if (GUILayout.Button("Sell", HighLogic.Skin.button)) Sell();
            GUI.enabled = enabled;
            if (GUILayout.Button("Cancel", HighLogic.Skin.button)) _sellName = null;
            GUILayout.EndHorizontal();
            GUI.DragWindow(new Rect(0, 0, _sell.width - 45, 40));
        }

        private void Sell()
        {
            var definition = PartResourceLibrary.Instance?.GetDefinition(_sellName);
            bool career = HighLogic.CurrentGame?.Mode == Game.Modes.CAREER;
            if (!InSave || !LedgerReady || !ReferenceEquals(_saleGame, HighLogic.CurrentGame) || definition == null)
            { _error = "The save or resource storage changed. Reopen the sale."; return; }
            var ledger = KShared.Instance.ResourceDict;
            if (!Data.TrySale(ledger, _sellName, _amount, career, definition.unitCost,
                out double remaining, out double proceeds, out _error)) return;
            if (career && (Funding.Instance == null || !Data.Finite(Funding.Instance.Funds + proceeds)))
            { _error = "Career funds are unavailable or would overflow."; return; }
            // Debit first: funding events cannot sell the same balance again reentrantly.
            ledger[_sellName] = remaining;
            if (career) Funding.Instance.AddFunds(proceeds, TransactionReasons.VesselRecovery);
            _sellName = null; _error = "";
        }
    }
}
