// Only the engine JSON adapter is doubled. IPC framing, connection management,
// publication, acknowledgement, retries and shutdown all use the production code.
namespace UnityEngine
{
    public static class JsonUtility
    {
        public static T FromJson<T>(string json)
            => System.Text.Json.JsonSerializer.Deserialize<T>(json,
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
    }
}
