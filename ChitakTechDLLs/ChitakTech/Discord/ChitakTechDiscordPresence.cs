using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using UnityEngine;

namespace ChitakTech
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class ChitakTechDiscordPresence : MonoBehaviour
    {
        private static ChitakTechDiscordPresence instance;
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
                CTShared.LogWarning("Discord Rich Presence could not start: " + exception.GetType().Name,
                    "DiscordPresence");
            }
        }
        public void Update()
        {
            while (messages.TryDequeue(out string message)) CTShared.Log(message, "DiscordPresence");
        }
        public void OnApplicationQuit()
        {
            client?.Dispose();
            CTShared.Log("Discord shutdown requested; game thread released.", "DiscordPresence");
        }
        public void OnDestroy()
        {
            if (instance != this) return;
            client?.Dispose();
            instance = null;
        }
    }
}
