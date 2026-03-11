using System;
using SignalRLite;
using UnityEngine;

/// <summary>
/// Minimal SignalR Lite usage demo.
/// Attach to a GameObject in your scene and set the HubUrl in the Inspector.
/// </summary>
public class SignalRLiteDemo : MonoBehaviour
{
    [Header("Connection")]
    [Tooltip("e.g. https://myserver.com/chathub")]
    public string HubUrl = "http://localhost:5000/chathub";

    [Tooltip("Optional bearer token for authenticated hubs")]
    public string AccessToken = "";

    private HubConnection _hub;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Start()
    {
        var options = new HubOptions
        {
            SkipNegotiation = false,
            AccessToken     = AccessToken,
            PingInterval    = TimeSpan.FromSeconds(15),
            PingTimeout     = TimeSpan.FromSeconds(30),
        };

        _hub = new HubConnection(HubUrl, options);

        // ── Wire up connection events ─────────────────────────────────────────
        _hub.OnConnected    += hub => Debug.Log("[Demo] Connected to SignalR hub.");
        _hub.OnDisconnected += (hub, reason) => Debug.Log($"[Demo] Disconnected: {reason}");
        _hub.OnError        += (hub, err)    => Debug.LogError($"[Demo] Error: {err}");
        _hub.OnReconnecting += hub => Debug.LogWarning("[Demo] Reconnecting…");
        _hub.OnReconnected  += hub => Debug.Log("[Demo] Reconnected.");

        // ── Subscribe to server → client calls ────────────────────────────────

        // Simple string message
        _hub.On<string>("ReceiveMessage", msg =>
        {
            Debug.Log($"[Demo] ReceiveMessage: {msg}");
        });

        // Two parameters
        _hub.On<string, string>("ReceiveFromUser", (user, msg) =>
        {
            Debug.Log($"[Demo] {user}: {msg}");
        });

        // Complex [Serializable] type
        _hub.On<ChatMessage>("ReceiveChatMessage", cm =>
        {
            Debug.Log($"[Demo] Chat from {cm.User}: {cm.Text}");
        });

        // ── Connect ───────────────────────────────────────────────────────────
        _hub.StartConnect();
    }

    private void OnDestroy()
    {
        _hub?.StartClose();
    }

    // ── UI helpers (call from buttons etc.) ──────────────────────────────────

    public void SendMessage(string message)
    {
        // Fire-and-forget: server method  "SendMessage(string msg)"
        _hub.Send("SendMessage", message);
    }

    public void InvokeWithResult()
    {
        // Invoke server method  "GetServerTime()"  and receive the result
        _hub.Invoke<string>("GetServerTime", (result, error) =>
        {
            if (error != null) Debug.LogError($"[Demo] GetServerTime error: {error}");
            else               Debug.Log($"[Demo] Server time: {result}");
        });
    }

    // ── Sample complex type ───────────────────────────────────────────────────

    [Serializable]
    public class ChatMessage
    {
        public string User;
        public string Text;
        public long   Timestamp;
    }
}
