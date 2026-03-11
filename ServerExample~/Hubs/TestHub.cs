using Microsoft.AspNetCore.SignalR;

namespace SignalRTestServer.Hubs;

/// <summary>
/// A simple SignalR hub for testing the Unity SignalRLite client.
/// 
/// Mapped at:  /testhub
/// 
/// Client → Server methods:
///   SendMessage(string)          → broadcasts ReceiveMessage(string) to ALL
///   Chat(string user, string)    → broadcasts ReceiveFromUser(string, string) to ALL
///   ScoreUpdate(string, int)     → broadcasts ScoreUpdate(string, int) to ALL
///   Echo(string)                 → [Invocation] returns the same string
///   GetTime()                    → [Invocation] returns server UTC time string
///   GetPlayer(string name)       → [Invocation] returns JSON-serialisable PlayerData
///   DirectEcho(string)           → sends ReceiveMessage only back to the CALLER
///   Fail(string reason)          → throws, causing a Completion with error
/// </summary>
public class TestHub : Hub
{
    // ── Fire-and-forget broadcasts ────────────────────────────────────────────

    /// <summary>
    /// Broadcasts a plain string to all connected clients.
    /// Client subscribes with: hub.On&lt;string&gt;("ReceiveMessage", msg => ...)
    /// </summary>
    public async Task SendMessage(string message)
    {
        Console.WriteLine($"[Hub] SendMessage: {message}");
        await Clients.All.SendAsync("ReceiveMessage", message);
    }

    /// <summary>
    /// Broadcasts a user+text pair to all connected clients.
    /// Client subscribes with: hub.On&lt;string, string&gt;("ReceiveFromUser", (user, text) => ...)
    /// </summary>
    public async Task Chat(string user, string text)
    {
        Console.WriteLine($"[Hub] Chat {user}: {text}");
        await Clients.All.SendAsync("ReceiveFromUser", user, text);
    }

    /// <summary>
    /// Broadcasts a score update.
    /// Client subscribes with: hub.On&lt;string, int&gt;("ScoreUpdate", (user, score) => ...)
    /// </summary>
    public async Task ScoreUpdate(string user, int score)
    {
        Console.WriteLine($"[Hub] ScoreUpdate {user}: {score}");
        await Clients.All.SendAsync("ScoreUpdate", user, score);
    }

    // ── Invocations with return values ────────────────────────────────────────

    /// <summary>
    /// Returns the input string unchanged.
    /// Client calls: hub.Invoke&lt;string&gt;("Echo", (result, err) => ..., "ping")
    /// </summary>
    public string Echo(string message)
    {
        Console.WriteLine($"[Hub] Echo: {message}");
        return message;
    }

    /// <summary>
    /// Returns the server's current UTC time as an ISO-8601 string.
    /// Client calls: hub.Invoke&lt;string&gt;("GetTime", (t, err) => ...)
    /// </summary>
    public string GetTime()
    {
        string t = DateTime.UtcNow.ToString("O");
        Console.WriteLine($"[Hub] GetTime: {t}");
        return t;
    }

    /// <summary>
    /// Returns a structured object.
    /// Client calls: hub.Invoke&lt;PlayerData&gt;("GetPlayer", (p, err) => ..., "Alice")
    /// The returned object is automatically serialised to JSON by SignalR.
    /// </summary>
    public PlayerData GetPlayer(string name)
    {
        Console.WriteLine($"[Hub] GetPlayer: {name}");
        return new PlayerData { Name = name, Score = 99 };
    }

    // ── Caller-only send ─────────────────────────────────────────────────────

    /// <summary>
    /// Sends ReceiveMessage ONLY back to the calling client.
    /// Useful for testing that the client receives its own echo.
    /// </summary>
    public async Task DirectEcho(string message)
    {
        Console.WriteLine($"[Hub] DirectEcho: {message}");
        await Clients.Caller.SendAsync("ReceiveMessage", message);
    }

    // ── Error handling ────────────────────────────────────────────────────────

    /// <summary>
    /// Always throws — the client receives a Completion with error field.
    /// Client calls: hub.Invoke("Fail", (result, err) => { /* err != null */ }, "reason")
    /// </summary>
    public Task Fail(string reason)
    {
        Console.WriteLine($"[Hub] Fail called with: {reason}");
        throw new HubException($"Intentional failure: {reason}");
    }

    // ── Connection lifecycle ─────────────────────────────────────────────────

    public override Task OnConnectedAsync()
    {
        Console.WriteLine($"[Hub] Client connected: {Context.ConnectionId}");
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        Console.WriteLine($"[Hub] Client disconnected: {Context.ConnectionId}  reason={exception?.Message ?? "clean"}");
        return base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Sample complex type returned by GetPlayer.
/// SignalR serialises this to JSON automatically.
/// On the Unity side mark with [Serializable] to use JsonUtility.FromJson.
/// </summary>
public class PlayerData
{
    public string Name  { get; set; } = "";
    public int    Score { get; set; }
}
