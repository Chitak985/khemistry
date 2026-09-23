using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using UnityEngine;

namespace Khemistry
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class KhemistryDiscordPresence : MonoBehaviour
    {
        private static KhemistryDiscordPresence instance;
        private DiscordRpcClient client;
        private readonly ConcurrentQueue<string> messages = new ConcurrentQueue<string>();

        public void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            try
            {
                using (Process process = Process.GetCurrentProcess())
                    client = new DiscordRpcClient(process.Id, message =>
                    {
                        // Do not access game objects or retain account information on the worker.
                        if (messages.Count < 8) messages.Enqueue(message);
                    });
            }
            catch (Exception exception)
            {
                KShared.LogWarning("Discord Rich Presence could not start: " + exception.GetType().Name,
                    "DiscordPresence");
            }
        }
        public void Update()
        {
            while (messages.TryDequeue(out string message)) KShared.Log(message, "DiscordPresence");
        }
        public void OnApplicationQuit() { client?.Dispose(); }
        public void OnDestroy()
        {
            if (instance != this) return;
            client?.Dispose();
            instance = null;
        }
    }
}
