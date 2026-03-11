# SignalR Lite for Unity

[![Unity](https://img.shields.io/badge/Unity-2021.3.45f2%2B-black)](https://unity.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Author](https://img.shields.io/badge/Author-%E7%8C%AB%E8%84%B8-orange)](https://github.com/kimch2)
[![Platform](https://img.shields.io/badge/Platform-All%20Platforms%20incl.%20WebGL-blue)](https://unity.com)

**[English]** | [简体中文](README_CN.md)

A **lightweight** ASP.NET Core SignalR client for Unity **2021.3.45f2+**, powered by [UnityWebSocket (psygames)](https://github.com/psygames/UnityWebSocket).

> A lightweight, dependency-free SignalR client for Unity that works on **all platforms including WebGL**.

---

## Features

- **Zero heavyweight dependencies** ? only requires `com.psygames.unitywebsocket`
- **All platforms** including WebGL / WeChat Mini-Game (inherited from UnityWebSocket)
- **Full JSON protocol** ? invocation, completion, streaming subscriptions, ping/pong
- **Auto-reconnect** with configurable backoff delays (0 -> 2 -> 10 -> 30 s)
- **Generic type conversion** ? `On<MyClass>()` automatically deserialises complex types via `JsonUtility`
- **Func overloads** ? server can call client methods and await return values
- **Built-in JSON parser** (`SimpleJson`) ? no external JSON library required
- **Thread-safe callbacks** ? UnityWebSocket dispatches on main thread; no `Dispatcher` boilerplate needed
- **Minimal API** ? familiar `On / Off / Send / Invoke` pattern

---

## Installation

### Via Unity Package Manager (recommended)

Open `Window -> Package Manager`, click **+ -> Add package from git URL**, and enter:

```
https://github.com/kimch2/SignalRLite.git#v1.0.0
```

Or add to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.psygames.unitywebsocket": "https://github.com/psygames/UnityWebSocket.git#upm",
    "com.mlgame.signalrlite": "https://github.com/kimch2/SignalRLite.git#v1.0.0"
  }
}
```

### Manual Installation

1. Install [UnityWebSocket](https://github.com/psygames/UnityWebSocket) via Package Manager
2. Copy the `Assets/SignalRLite/` folder into your project's `Assets/` directory

---

## Quick Start

```csharp
using SignalRLite;
using UnityEngine;

public class ChatClient : MonoBehaviour
{
    private HubConnection _hub;

    void Start()
    {
        _hub = new HubConnection("http://localhost:5000/chathub");

        _hub.OnConnected    += hub => Debug.Log("Connected!");
        _hub.OnDisconnected += (hub, reason) => Debug.Log($"Disconnected: {reason}");
        _hub.OnError        += (hub, err) => Debug.LogError(err);
        _hub.OnReconnecting += hub => Debug.Log("Reconnecting...");

        _hub.On<string>("ReceiveMessage", msg => Debug.Log(msg));
        _hub.On<string, string>("Chat", (user, text) => Debug.Log($"{user}: {text}"));

        _hub.StartConnect();
    }

    void OnDestroy() => _hub?.StartClose();

    public void SendChat(string message) => _hub.Send("SendMessage", message);
}
```

---

## API Reference

### Construction

```csharp
var hub = new HubConnection("http://server/hub");

var hub = new HubConnection("http://server/hub", new HubOptions
{
    SkipNegotiation = false,
    AccessToken     = "bearer-token",
    PingInterval    = TimeSpan.FromSeconds(15),
    PingTimeout     = TimeSpan.FromSeconds(30),
    ReconnectDelays = new TimeSpan?[]
    {
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(10),
        null,
    },
});
```

### Connection Lifecycle

```csharp
hub.StartConnect();
hub.StartClose();

HubConnectionState state = hub.State;
// Disconnected | Connecting | Connected | Reconnecting
```

### Events

```csharp
hub.OnConnected    += hub => { };
hub.OnDisconnected += (hub, reason) => { };
hub.OnError        += (hub, error) => { };
hub.OnReconnecting += hub => { };
hub.OnReconnected  += hub => { };
```

### Send (fire-and-forget)

```csharp
hub.Send("MethodName");
hub.Send("MethodName", "arg1");
hub.Send("MethodName", "arg1", 42, true);
```

### Invoke (with return value)

```csharp
hub.Invoke<string>("GetTime", (time, error) => Debug.Log(time));

hub.Invoke<PlayerData>("GetPlayer", (player, error) =>
{
    Debug.Log($"{player.Name}: {player.Score}");
}, "Alice");
```

### On ? Subscribe to server -> client calls

```csharp
hub.On("Tick", () => { });
hub.On<string>("ReceiveMessage", msg => { });
hub.On<string, string>("Chat", (user, text) => { });
hub.On<string, int>("ScoreUpdate", (user, score) => { });

hub.Off("ReceiveMessage");
```

### Complex Type Deserialization

```csharp
[Serializable]
public class GameState
{
    public string MapName;
    public int    PlayerCount;
    public float  TimeRemaining;
}

hub.On<GameState>("StateUpdated", state =>
{
    Debug.Log($"Map: {state.MapName}, Players: {state.PlayerCount}");
});
```

> **Note:** Configure your ASP.NET Core server to use PascalCase to align with `JsonUtility`:
> ```csharp
> builder.Services.AddSignalR()
>     .AddJsonProtocol(o => o.PayloadSerializerOptions.PropertyNamingPolicy = null);
> ```

---

## Architecture

```
Assets/SignalRLite/
+-- Runtime/
|   +-- HubConnection.cs          <- Public API: connect / send / invoke / on / off
|   +-- HubOptions.cs             <- Configuration (ping, reconnect, auth)
|   +-- Messages/
|   |   +-- SignalRMessage.cs     <- Message types (Invocation, Completion, Ping, ...)
|   +-- Protocol/
|   |   +-- SignalRProtocol.cs    <- JSON encode/decode with 0x1E separator
|   +-- Transport/
|   |   +-- WebSocketTransport.cs <- UnityWebSocket adapter + SignalR handshake
|   +-- Negotiation/
|   |   +-- NegotiationResult.cs  <- HTTP negotiate via UnityWebRequest
|   +-- Utility/
|       +-- SimpleJson.cs         <- Embedded minimal JSON parser (no dependencies)
|       +-- SignalRLiteRunner.cs  <- MonoBehaviour singleton (coroutines + Update)
+-- Tests/
+-- Samples/
+-- ServerExample~/               <- ASP.NET Core test server (Unity ignores ~)
```

### Connection Flow

```
StartConnect()
    |
    +--[negotiate=true]--> POST /hub/negotiate --> connectionToken
    |                                                   |
    +--[negotiate=false]-----> ConnectWebSocket(ws://hub?id=token)
                                        |
                               WS Open -> send handshake {"protocol":"json","version":1}
                                        |
                               Server responds {} --> Connected
                                        |
                               OnConnected fires
                                        |
                    +-- Ping every 15 s ----------------+
                    +-- Timeout after 30 s -> reconnect -+
```

### Dependency Comparison

| Component | SignalR Lite |
|---|---|
| WebSocket | \UnityWebSocket\ (psygames) |
| HTTP | \UnityWebRequest\ (built-in) |
| JSON | \SimpleJson\ (embedded, ~150 lines) |
| Async | Coroutines + callbacks |
| WebGL | Yes |
| Package size | ~10 KB |
| External dependencies | 1 |

---

## Running Tests

### Edit Mode Tests

Open `Window -> General -> Test Runner -> EditMode` and click **Run All**.

| Suite | Count | Coverage |
|---|---|---|
| `SimpleJsonTests` | 35 | JSON parse / stringify / round-trip |
| `ProtocolTests` | 22 | SignalR encode / decode / round-trip |

### Play Mode Tests

Open `Window -> General -> Test Runner -> PlayMode` and click **Run All**.

### Integration Tests (requires local server)

```powershell
dotnet run --project ServerExample~/SignalRTestServer
# Hub: http://localhost:5000/testhub
```

Set `ServerRunning = true` in `HubConnectionTests.cs`, then run PlayMode tests.

---

## On-Device Testing

Unity Test Framework cannot run on real devices. Use the built-in **Device Test** sample instead.

Import via `Package Manager -> SignalR Lite -> Samples -> Device Test`.

| Button | Test |
|---|---|
| **Run All** | Runs all tests in sequence |
| **Connect** | Connect + Disconnect |
| **Echo** | `Invoke<string>` round-trip |
| **GetTime** | Server return value |
| **Complex Type** | `JsonUtility` deserialisation (catches IL2CPP strip issues) |
| **Reconnect** | Background 35 s, checks `OnReconnecting` |

### link.xml (if JsonUtility is stripped by IL2CPP)

```xml
<linker>
  <assembly fullname="Assembly-CSharp">
    <type fullname="SignalRLiteDeviceTest+PlayerData" preserve="all"/>
  </assembly>
</linker>
```

---

## Supported Platforms

| Platform | Status |
|---|---|
| Windows / macOS / Linux | Yes |
| iOS | Yes |
| Android | Yes |
| WebGL | Yes |
| Console (PS5 / Xbox / Switch) | Yes (same as UnityWebSocket support) |

---

## License

MIT License ? see [LICENSE](LICENSE) for details.

---

## Acknowledgements

- [UnityWebSocket by psygames](https://github.com/psygames/UnityWebSocket) ? the WebSocket backbone that makes cross-platform support (especially WebGL) possible
- [ASP.NET Core SignalR protocol spec](https://github.com/dotnet/aspnetcore/tree/main/src/SignalR/docs/specs)
