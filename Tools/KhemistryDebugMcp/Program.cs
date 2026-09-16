using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KhemistryDebugMcp;

internal static class Program
{
    private const string ProtocolMagic = "KHDBG1";
    private static readonly string KspRoot = Environment.GetEnvironmentVariable("KHEMISTRY_KSP_ROOT")
        ?? @"C:\Users\name\Desktop\v1 test";
    private static readonly string SessionPath = Path.Combine(Path.GetTempPath(), "khemistry-debug-session.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private sealed record Tool(string name, string description, JsonObject inputSchema);

    private static async Task Main()
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = new UTF8Encoding(false);
        string? line;
        while ((line = await Console.In.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonNode? request = null;
            try
            {
                request = JsonNode.Parse(line);
                string? method = request?["method"]?.GetValue<string>();
                JsonNode? id = request?["id"]?.DeepClone();
                if (method == "notifications/initialized" || method == "notifications/cancelled") continue;
                if (id == null) continue;
                JsonNode result = method switch
                {
                    "initialize" => Initialize(request!),
                    "ping" => new JsonObject(),
                    "tools/list" => new JsonObject { ["tools"] = JsonSerializer.SerializeToNode(Tools(), JsonOptions) },
                    "tools/call" => await CallTool(request!),
                    _ => throw new McpException(-32601, "Method not found: " + method)
                };
                await Write(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });
            }
            catch (Exception exception)
            {
                JsonNode? id = request?["id"]?.DeepClone();
                int code = exception is McpException mcp ? mcp.Code : -32603;
                await Write(new JsonObject
                {
                    ["jsonrpc"] = "2.0", ["id"] = id,
                    ["error"] = new JsonObject { ["code"] = code, ["message"] = exception.Message }
                });
            }
        }
    }

    private static JsonNode Initialize(JsonNode request)
    {
        string version = request["params"]?["protocolVersion"]?.GetValue<string>() ?? "2025-06-18";
        return new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = "khemistry-debug", ["version"] = "0.1.0" },
            ["instructions"] = "Development-only live debugger for the local KSP Khemistry test instance. Inspect before mutating. Use only dedicated test saves."
        };
    }

    private static async Task<JsonNode> CallTool(JsonNode request)
    {
        string name = request["params"]?["name"]?.GetValue<string>()
            ?? throw new McpException(-32602, "Tool name is required.");
        JsonObject arguments = request["params"]?["arguments"] as JsonObject ?? new JsonObject();
        try
        {
            string text = name switch
            {
                "status" => await Status(),
                "launch_ksp" => await LaunchKsp(arguments),
                "game_state" => CallBridge("game_state", arguments),
                "list_parts" => CallBridge("list_parts", arguments),
                "inspect_part" => CallBridge("inspect_part", arguments),
                "inspect_khemistry" => CallBridge("inspect_khemistry", arguments),
                "converter_action" => CallBridge("converter_action", arguments),
                "run_converter_test" => await RunConverterTest(arguments),
                "list_parallax_scatters" => CallBridge("list_parallax_scatters", arguments),
                "capture_screenshot" => CallBridge("capture_screenshot", arguments),
                "recent_logs" => RecentLogs(arguments),
                _ => throw new McpException(-32602, "Unknown tool: " + name)
            };
            return ToolResult(text, false);
        }
        catch (Exception exception)
        {
            return ToolResult(exception.Message, true);
        }
    }

    private static IEnumerable<Tool> Tools()
    {
        yield return NewTool("status", "Check whether the KSP debug bridge is running and return its live scene.");
        yield return NewTool("launch_ksp", "Launch the configured KSP test instance and wait for the debug bridge.",
            Property("arguments", "string", "Optional KSP command-line arguments."),
            Property("wait_seconds", "integer", "Seconds to wait for the bridge (default 120)."));
        yield return NewTool("game_state", "Return the current KSP scene, active vessel, time, and save folder.");
        yield return NewTool("list_parts", "List parts and module indexes on a loaded vessel.",
            Property("vessel_id", "string", "Vessel GUID; omitted means active vessel."));
        yield return NewRequiredTool("inspect_part", "Inspect one loaded part, stock tanks, and Khemistry modules.",
            [Property("part_flight_id", "integer", "Required part flight ID."),
             Property("vessel_id", "string", "Optional vessel GUID.")], ["part_flight_id"]);
        yield return NewTool("inspect_khemistry", "Inspect every Khemistry storage, cell, kerbal, and converter on a vessel or one part.",
            Property("vessel_id", "string", "Vessel GUID; omitted means active vessel."),
            Property("part_flight_id", "integer", "Optional part flight ID."));
        yield return NewRequiredTool("converter_action", "Select a recipe or start/stop/turn on/off a Khemistry converter.",
            [Property("part_flight_id", "integer", "Required part flight ID."),
             Property("module_index", "integer", "Required index from list_parts."),
             EnumProperty("action", "select_recipe", "turn_on", "turn_off", "start", "stop"),
             Property("recipe_name", "string", "Required for select_recipe."),
             Property("vessel_id", "string", "Optional vessel GUID.")],
            ["part_flight_id", "module_index", "action"]);
        yield return NewRequiredTool("run_converter_test", "Select/enable/start a converter and observe one batch or a terminal failure.",
            [Property("part_flight_id", "integer", "Required part flight ID."),
             Property("module_index", "integer", "Required converter module index."),
             Property("recipe_name", "string", "Optional recipe to select first."),
             Property("timeout_seconds", "number", "Observation timeout, 1-180 seconds (default 30)."),
             Property("vessel_id", "string", "Optional vessel GUID.")],
            ["part_flight_id", "module_index"]);
        yield return NewTool("list_parallax_scatters", "List nearest active Parallax collider instances for the active/selected vessel.",
            Property("vessel_id", "string", "Vessel GUID; omitted means active vessel."),
            Property("scatter_name", "string", "Optional exact scatter name filter."),
            Property("max_results", "integer", "Maximum nearest instances (default 50, maximum 500)."));
        yield return NewTool("capture_screenshot", "Ask KSP to capture a PNG screenshot.",
            Property("name", "string", "Optional filename."),
            Property("supersize", "integer", "Unity supersize multiplier 1-4."));
        yield return NewTool("recent_logs", "Read the tail of KSP.log, optionally filtered to Khemistry messages.",
            Property("lines", "integer", "Maximum returned lines (default 200, maximum 2000)."),
            Property("khemistry_only", "boolean", "Filter to Khemistry and Khemistry Debug messages (default true)."));
    }

    private static Tool NewTool(string name, string description, params JsonObject[] properties)
        => NewRequiredTool(name, description, properties, []);

    private static Tool NewRequiredTool(string name, string description, JsonObject[] properties, string[] required)
    {
        var map = new JsonObject();
        foreach (JsonObject property in properties)
        {
            string key = property["_name"]!.GetValue<string>();
            property.Remove("_name");
            map[key] = property;
        }
        var schema = new JsonObject
        {
            ["type"] = "object", ["properties"] = map, ["additionalProperties"] = false
        };
        if (required.Length > 0) schema["required"] = new JsonArray(required.Select(x => (JsonNode?)x).ToArray());
        return new Tool(name, description, schema);
    }

    private static JsonObject Property(string name, string type, string description)
        => new() { ["_name"] = name, ["type"] = type, ["description"] = description };

    private static JsonObject EnumProperty(string name, params string[] values)
        => new() { ["_name"] = name, ["type"] = "string",
            ["enum"] = new JsonArray(values.Select(x => (JsonNode?)x).ToArray()) };

    private static async Task<string> Status()
    {
        if (!File.Exists(SessionPath)) return JsonSerializer.Serialize(new { running = false, sessionPath = SessionPath });
        try { return CallBridge("ping", new JsonObject()); }
        catch (Exception exception)
        {
            return JsonSerializer.Serialize(new { running = false, staleSession = true, error = exception.Message });
        }
    }

    private static async Task<string> LaunchKsp(JsonObject arguments)
    {
        if (File.Exists(SessionPath))
        {
            try { return CallBridge("ping", new JsonObject()); }
            catch { try { File.Delete(SessionPath); } catch { } }
        }
        string executable = Path.Combine(KspRoot, "KSP_x64.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("KSP_x64.exe was not found.", executable);
        string commandLine = GetString(arguments, "arguments") ?? "-popupwindow";
        Process process = Process.Start(new ProcessStartInfo(executable, commandLine)
        {
            WorkingDirectory = KspRoot,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("KSP did not start.");
        int waitSeconds = Math.Clamp(GetInt(arguments, "wait_seconds", 120), 1, 600);
        DateTime deadline = DateTime.UtcNow.AddSeconds(waitSeconds);
        Exception? lastError = null;
        while (DateTime.UtcNow < deadline && !process.HasExited)
        {
            await Task.Delay(500);
            if (!File.Exists(SessionPath)) continue;
            try { return CallBridge("ping", new JsonObject()); }
            catch (Exception exception) { lastError = exception; }
        }
        throw new TimeoutException(process.HasExited
            ? "KSP exited before the debug bridge became ready. Check KSP.log."
            : "Timed out waiting for the debug bridge. " + lastError?.Message);
    }

    private static async Task<string> RunConverterTest(JsonObject arguments)
    {
        var common = CloneArguments(arguments, "vessel_id", "part_flight_id", "module_index");
        string before = CallBridge("inspect_part", common);
        string? recipe = GetString(arguments, "recipe_name");
        if (!string.IsNullOrWhiteSpace(recipe))
        {
            JsonObject select = CloneArguments(common);
            select["action"] = "select_recipe"; select["recipe_name"] = recipe;
            CallBridge("converter_action", select);
        }
        JsonObject action = CloneArguments(common); action["action"] = "turn_on";
        CallBridge("converter_action", action);
        action["action"] = "start";
        string started = CallBridge("converter_action", action);

        double timeout = Math.Clamp(GetDouble(arguments, "timeout_seconds", 30), 1, 180);
        DateTime deadline = DateTime.UtcNow.AddSeconds(timeout);
        double previousProgress = -1;
        bool sawProgress = false;
        bool completed = false;
        string current = started;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
            current = CallBridge("inspect_part", common);
            using JsonDocument document = JsonDocument.Parse(current);
            if (!TryConverter(document.RootElement, GetInt(common, "module_index", -1), out JsonElement converter)) break;
            bool running = converter.TryGetProperty("isRunning", out JsonElement run) && run.GetBoolean();
            double progress = converter.TryGetProperty("batchProgress", out JsonElement value) && value.ValueKind == JsonValueKind.Number
                ? value.GetDouble() : 0;
            if (progress > 1e-8) sawProgress = true;
            if (!running || sawProgress && previousProgress >= 0 && progress + 1e-8 < previousProgress)
            {
                completed = true;
                break;
            }
            previousProgress = progress;
        }
        string logs = RecentLogs(new JsonObject { ["lines"] = 100, ["khemistry_only"] = true });
        return JsonSerializer.Serialize(new
        {
            completed,
            timedOut = !completed,
            before = JsonNode.Parse(before),
            after = JsonNode.Parse(current),
            recentLogs = logs.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        }, JsonOptions);
    }

    private static bool TryConverter(JsonElement root, int moduleIndex, out JsonElement converter)
    {
        converter = default;
        if (!root.TryGetProperty("modules", out JsonElement modules)) return false;
        foreach (JsonElement module in modules.EnumerateArray())
            if (module.GetProperty("index").GetInt32() == moduleIndex
                && module.TryGetProperty("khemistry", out converter)
                && converter.ValueKind == JsonValueKind.Object) return true;
        return false;
    }

    private static string CallBridge(string command, JsonObject arguments)
    {
        if (!File.Exists(SessionPath)) throw new InvalidOperationException("KSP debug bridge is not running. Use launch_ksp first.");
        using JsonDocument session = JsonDocument.Parse(File.ReadAllText(SessionPath));
        string pipeName = session.RootElement.GetProperty("pipeName").GetString()!;
        string token = session.RootElement.GetProperty("token").GetString()!;
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.None);
        pipe.Connect(5000);
        using var writer = new BinaryWriter(pipe, Encoding.UTF8, true);
        using var reader = new BinaryReader(pipe, Encoding.UTF8, true);
        writer.Write(ProtocolMagic); writer.Write(token); writer.Write(command);
        writer.Write(arguments.Count);
        foreach ((string key, JsonNode? value) in arguments)
        {
            writer.Write(ToSnakeCase(key));
            writer.Write(NodeToString(value));
        }
        writer.Flush();
        bool ok = reader.ReadBoolean();
        string response = reader.ReadString();
        if (!ok) throw new InvalidOperationException(response);
        return response;
    }

    private static string RecentLogs(JsonObject arguments)
    {
        string path = Path.Combine(KspRoot, "KSP.log");
        if (!File.Exists(path)) throw new FileNotFoundException("KSP.log was not found.", path);
        int count = Math.Clamp(GetInt(arguments, "lines", 200), 1, 2000);
        bool filtered = GetBool(arguments, "khemistry_only", true);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long start = Math.Max(0, stream.Length - 4 * 1024 * 1024);
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        if (start > 0) reader.ReadLine();
        IEnumerable<string> lines = reader.ReadToEnd().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (filtered) lines = lines.Where(line => line.Contains("Khemistry", StringComparison.OrdinalIgnoreCase));
        return string.Join("\n", lines.TakeLast(count));
    }

    private static JsonObject CloneArguments(JsonObject source, params string[] allowed)
    {
        var clone = new JsonObject();
        IEnumerable<string> names = allowed.Length == 0 ? source.Select(x => x.Key) : allowed;
        foreach (string name in names)
            if (source.TryGetPropertyValue(name, out JsonNode? value)) clone[name] = value?.DeepClone();
        return clone;
    }

    private static JsonNode ToolResult(string text, bool error) => new JsonObject
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["isError"] = error
    };

    private static async Task Write(JsonNode response)
    {
        await Console.Out.WriteLineAsync(response.ToJsonString(JsonOptions));
        await Console.Out.FlushAsync();
    }

    private static string NodeToString(JsonNode? node)
    {
        if (node == null) return "";
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out string? text)) return text ?? "";
            if (value.TryGetValue<bool>(out bool boolean)) return boolean ? "true" : "false";
            if (value.TryGetValue<long>(out long integer)) return integer.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<double>(out double number)) return number.ToString("R", CultureInfo.InvariantCulture);
        }
        return node.ToJsonString(JsonOptions);
    }

    private static string ToSnakeCase(string value)
    {
        var result = new StringBuilder();
        foreach (char c in value)
        {
            if (char.IsUpper(c) && result.Length > 0) result.Append('_');
            result.Append(char.ToLowerInvariant(c));
        }
        return result.ToString();
    }

    private static string? GetString(JsonObject arguments, string name)
        => arguments[name]?.GetValue<string>();
    private static int GetInt(JsonObject arguments, string name, int fallback)
        => arguments[name]?.GetValue<int>() ?? fallback;
    private static double GetDouble(JsonObject arguments, string name, double fallback)
        => arguments[name]?.GetValue<double>() ?? fallback;
    private static bool GetBool(JsonObject arguments, string name, bool fallback)
        => arguments[name]?.GetValue<bool>() ?? fallback;

    private sealed class McpException(int code, string message) : Exception(message)
    {
        internal int Code { get; } = code;
    }
}
