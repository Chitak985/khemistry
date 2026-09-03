using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Khemistry
{
    public partial class KShared
    {
        private void OnDepositsLauncherReady()
        {
            if (_depositsToolbarButton != null || ApplicationLauncher.Instance == null
                || _depositsButtonTexture == null) return;
            _depositsToolbarButton = ApplicationLauncher.Instance.AddModApplication(
                () => _depositsVisible = true,
                () => _depositsVisible = false,
                null, null, null, null,
                ApplicationLauncher.AppScenes.FLIGHT,
                _depositsButtonTexture
            );
        }

        private void OnDepositsLauncherDestroyed()
        {
            _depositsToolbarButton = null;
            _depositsVisible = false;
        }

        private void OnGameSceneLoadRequested(GameScenes destination)
        {
            CloseTransientWindows();
        }

        /// <summary>
        /// Closes persistent KShared windows and releases their callbacks before changing
        /// scenes. A callback often captures a PartModule, so retaining it after the part's
        /// vessel is destroyed can both keep stale objects alive and invoke invalid game state.
        /// </summary>
        private void CloseTransientWindows()
        {
            _selectorVisible = false;
            _kcoSelectorVisible = false;
            _amountVisible = false;
            _depositsVisible = false;
            _selectorCallback = null;
            _amountCallback = null;
            _selectorOptions = null;
            _selectorResources = null;
        }

        /// <summary>
        /// Lists every loaded surface/underground deposit on the active vessel's current body,
        /// nearest first, with distance in meters from the vessel.
        /// </summary>
        private void DrawDepositsWindow(int windowId)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || v.mainBody == null)
            {
                GUILayout.Label("No active vessel.", HighLogic.Skin.label);
            }
            else
            {
                var nearby = new List<(string label, float distance)>();
                const float maxDepositDistance = 500000f;

                foreach (KhemistryGDeposit d in surfaceDeposits)
                {
                    if (d.Planet != v.mainBody.name) continue;
                    float dist = d.DistanceFromDeposit((float)v.latitude, (float)v.longitude);
                    if (dist > maxDepositDistance) continue;
                    nearby.Add((d.Resource + " (surface)", dist));
                }
                foreach (KhemistryUDeposit d in undergroundDeposits)
                {
                    if (d.Planet != v.mainBody.name) continue;
                    float dist = d.DistanceFromDeposit((float)v.latitude, (float)v.longitude);
                    if (dist > maxDepositDistance) continue;
                    nearby.Add((d.Resource + " (underground)", dist));
                }

                nearby.Sort((a, b) => a.distance.CompareTo(b.distance));

                GUILayout.Label("Deposits within 500 km of " + v.mainBody.name + ":", HighLogic.Skin.label);
                _depositsScroll = GUILayout.BeginScrollView(_depositsScroll, GUILayout.Width(360f), GUILayout.Height(320f));
                if (nearby.Count == 0)
                    GUILayout.Label("No deposits within 500 km.", HighLogic.Skin.label);
                else
                    foreach (var entry in nearby)
                        GUILayout.Label(string.Format("{0} — {1:F0} m", entry.label, entry.distance), HighLogic.Skin.label);
                GUILayout.EndScrollView();
            }

            if (GUILayout.Button("Close", HighLogic.Skin.button))
                _depositsVisible = false;

            GUI.DragWindow();
        }

        private void DrawSelectorWindowKCO(int windowId)
        {
            GUILayout.Label("Select a resource to send to the KSC:", HighLogic.Skin.label);

            // GUILayout.BeginScrollView is Unity's legacy IMGUI scroller,
            // compatible with all KSP-supported Unity versions and reliably
            // handles any number of items without content height issues.
            _selectorScroll = GUILayout.BeginScrollView(
                _selectorScroll,
                HighLogic.Skin.scrollView,
                GUILayout.Height(180f)
            );
            foreach (string res in _selectorResources ?? new List<string>())
            {
                if (GUILayout.Button(res, HighLogic.Skin.button))
                {
                    _kcoSelectorVisible = false;
                    Action<string> callback = _selectorCallback;
                    _selectorCallback = null;
                    _selectorResources = null;
                    callback?.Invoke(res);
                }
            }
            GUILayout.EndScrollView();

            if (GUILayout.Button("Cancel", HighLogic.Skin.button))
            {
                _kcoSelectorVisible = false;
                _selectorCallback = null;
                _selectorResources = null;
            }

            // Allow the player to drag the window around
            GUI.DragWindow();
        }

        // Opens the resource selector window, centered on screen.
        // onSelect is called with the chosen resource name when the player picks one.
        // Used in KhemistryConstructionOverhaul
        public void ShowResourceSelector(List<string> resources, Action<string> onSelect)
        {
            // Keep a snapshot. Callers commonly build this list from live vessel state, and an
            // external mutation during an IMGUI pass would otherwise invalidate the enumerator.
            _selectorResources = resources == null
                ? new List<string>() : new List<string>(resources);
            _selectorCallback = onSelect;
            _selectorScroll = Vector2.zero;
            _selectorTitle = "Send resources to the KSC";
            _windowRect = new Rect(
                (Screen.width - _windowRect.width) / 2f,
                (Screen.height - _windowRect.height) / 2f,
                _windowRect.width,
                _windowRect.height
            );
            _selectorVisible = false;
            _kcoSelectorVisible = true;
        }

        public void ShowSelector(string title, List<string> options, Action<string> onSelect)
        {
            _selectorTitle = title ?? "";
            _selectorOptions = options == null
                ? new List<string>() : new List<string>(options);
            _selectorCallback = onSelect;
            _selectorScroll = Vector2.zero;
            _windowRect = new Rect(
                (Screen.width - _windowRect.width) / 2f,
                (Screen.height - _windowRect.height) / 2f,
                _windowRect.width,
                _windowRect.height
            );
            _kcoSelectorVisible = false;
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

            if (_kcoSelectorVisible)
                _windowRect = GUILayout.Window(
                    _windowId,
                    _windowRect,
                    DrawSelectorWindowKCO,
                    _selectorTitle,
                    HighLogic.Skin.window);

            if (_depositsVisible)
                _depositsRect = GUILayout.Window(
                    _depositsWindowId,
                    _depositsRect,
                    DrawDepositsWindow,
                    "Nearby Deposits",
                    HighLogic.Skin.window);
        }

        public void ShowAmountSelector(string title, float min, float max, float initial, Action<float> onConfirm)
        {
            if (float.IsNaN(min) || float.IsInfinity(min)) min = 0f;
            if (float.IsNaN(max) || float.IsInfinity(max)) max = min;
            if (max < min)
            {
                float swap = min;
                min = max;
                max = swap;
            }
            if (float.IsNaN(initial) || float.IsInfinity(initial)) initial = min;
            _amountTitle = title ?? "";
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
                Action<float> callback = _amountCallback;
                _amountCallback = null;
                callback?.Invoke(_amountValue);
            }
            if (GUILayout.Button("Cancel", HighLogic.Skin.button))
            {
                _amountVisible = false;
                _amountCallback = null;
            }
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
            foreach (string option in _selectorOptions ?? new List<string>())
            {
                if (GUILayout.Button(option, HighLogic.Skin.button))
                {
                    _selectorVisible = false;
                    Action<string> callback = _selectorCallback;
                    _selectorCallback = null;
                    _selectorOptions = null;
                    callback?.Invoke(option);
                }
            }
            GUILayout.EndScrollView();

            if (GUILayout.Button("Cancel", HighLogic.Skin.button))
            {
                _selectorVisible = false;
                _selectorCallback = null;
                _selectorOptions = null;
            }

            GUI.DragWindow();
        }
    }
}
