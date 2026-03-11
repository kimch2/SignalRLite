# SignalR Lite for Unity

[![Unity](https://img.shields.io/badge/Unity-2021.3.45f2%2B-black)](https://unity.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Author](https://img.shields.io/badge/Author-%E7%8C%AB%E8%84%B8-orange)](https://github.com/kimch2)
[![Platform](https://img.shields.io/badge/Platform-%E5%85%A8%E5%B9%B3%E5%8F%B0%E5%90%AB%20WebGL-blue)](https://unity.com)

[English](README.md) | **[简体中文]**

面向 Unity **2021.3.45f2+** 的轻量级 ASP.NET Core SignalR 客户端，底层使用 [UnityWebSocket (psygames)](https://github.com/psygames/UnityWebSocket)。

> 轻量、无重型依赖的 Unity SignalR 客户端，支持**包括 WebGL 在内的所有平台**。

---

## 功能特性

- **零重型依赖** — 仅需 `com.psygames.unitywebsocket` 一个外部包
- **全平台支持** — 含 WebGL / 微信小游戏（继承自 UnityWebSocket）
- **完整 JSON 协议** — 支持 Invocation、Completion、订阅推送、Ping/Pong
- **自动重连** — 可配置退避延迟（0 -> 2 -> 10 -> 30 秒）
- **泛型类型转换** — `On<MyClass>()` 自动通过 `JsonUtility` 反序列化复杂类型
- **Func 重载** — 服务端可调用客户端方法并等待返回值
- **内置 JSON 解析器**（`SimpleJson`）— 无需任何外部 JSON 库
- **线程安全回调** — UnityWebSocket 在主线程分发，无需手写 Dispatcher
- **简洁 API** — 熟悉的 `On / Off / Send / Invoke` 模式，约 10 KB

---

## 安装

### 方式一：Package Manager（推荐）

打开 `Window -> Package Manager`，点击 **+ -> Add package from git URL**，输入：

```
https://github.com/kimch2/SignalRLite.git#v1.0.0
```

### 方式二：修改 manifest.json

在项目的 `Packages/manifest.json` 中添加：

```json
{
  "dependencies": {
    "com.psygames.unitywebsocket": "https://github.com/psygames/UnityWebSocket.git#upm",
    "com.mlgame.signalrlite": "https://github.com/kimch2/SignalRLite.git#v1.0.0"
  }
}
```

### 方式三：手动安装

1. 通过 Package Manager 安装 [UnityWebSocket](https://github.com/psygames/UnityWebSocket)
2. 将 `Assets/SignalRLite/` 文件夹复制到项目的 `Assets/` 目录中

---

## 快速开始

```csharp
using SignalRLite;
using UnityEngine;

public class ChatClient : MonoBehaviour
{
    private HubConnection _hub;

    void Start()
    {
        _hub = new HubConnection("http://localhost:5000/chathub");

        // 连接事件
        _hub.OnConnected    += hub => Debug.Log("已连接！");
        _hub.OnDisconnected += (hub, reason) => Debug.Log($"已断开：{reason}");
        _hub.OnError        += (hub, err) => Debug.LogError(err);
        _hub.OnReconnecting += hub => Debug.Log("重连中...");

        // 订阅服务端推送
        _hub.On<string>("ReceiveMessage", msg => Debug.Log(msg));
        _hub.On<string, string>("Chat", (user, text) => Debug.Log($"{user}: {text}"));

        _hub.StartConnect();
    }

    void OnDestroy() => _hub?.StartClose();

    public void SendChat(string message) => _hub.Send("SendMessage", message);
}
```

---

## API 参考

### 构造

```csharp
// 默认选项
var hub = new HubConnection("http://server/hub");

// 自定义选项
var hub = new HubConnection("http://server/hub", new HubOptions
{
    SkipNegotiation = false,          // true：跳过 HTTP negotiate 步骤
    AccessToken     = "bearer-token", // JWT 鉴权
    PingInterval    = TimeSpan.FromSeconds(15),
    PingTimeout     = TimeSpan.FromSeconds(30),
    ReconnectDelays = new TimeSpan?[] // null 表示停止重连
    {
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(10),
        null,
    },
});
```

### 连接生命周期

```csharp
hub.StartConnect();   // 开始异步连接
hub.StartClose();     // 关闭连接并禁止自动重连

HubConnectionState state = hub.State;
// Disconnected | Connecting | Connected | Reconnecting
```

### 事件

```csharp
hub.OnConnected    += hub => { };
hub.OnDisconnected += (hub, reason) => { };   // reason 为 null 表示正常关闭
hub.OnError        += (hub, error) => { };
hub.OnReconnecting += hub => { };
hub.OnReconnected  += hub => { };

// 可选消息拦截器（返回 false 阻止后续处理）
hub.OnMessage = (hub, msg) => { Debug.Log(msg.Type); return true; };
```

### Send（即发即忘）

```csharp
hub.Send("方法名");
hub.Send("方法名", "参数1");
hub.Send("方法名", "参数1", 42, true);
```

### Invoke（带返回值）

```csharp
// 无类型结果
hub.Invoke("GetData", (result, error) =>
{
    if (error != null) Debug.LogError(error);
    else               Debug.Log(result);
});

// 指定返回类型
hub.Invoke<string>("GetTime", (time, error) => Debug.Log(time));

hub.Invoke<PlayerData>("GetPlayer", (player, error) =>
{
    Debug.Log($"{player.Name}: {player.Score}");
}, "Alice");
```

### On — 订阅服务端调用

```csharp
hub.On("Tick", () => { });                                    // 无参数
hub.On<string>("ReceiveMessage", msg => { });                 // 1 个参数
hub.On<string, string>("Chat", (user, text) => { });         // 2 个参数
hub.On<string, int>("ScoreUpdate", (user, score) => { });    // 支持最多 4 个参数

// 服务端调用客户端并等待返回值
hub.On<string, string>("GetGreeting", name => $"你好，{name}！");

// 取消订阅
hub.Off("ReceiveMessage");
```

### 复杂类型反序列化

服务端发送的复杂对象，用 `[Serializable]` 标记 C# 类：

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
    Debug.Log($"地图：{state.MapName}，玩家数：{state.PlayerCount}");
});
```

> **注意：** `JsonUtility` 区分大小写，需要配置 ASP.NET Core 服务端使用 PascalCase：
> ```csharp
> builder.Services.AddSignalR()
>     .AddJsonProtocol(o => o.PayloadSerializerOptions.PropertyNamingPolicy = null);
> ```

---

## 架构说明

```
Assets/SignalRLite/
+-- Runtime/
|   +-- HubConnection.cs          <- 公共 API：连接 / 发送 / 调用 / 订阅
|   +-- HubOptions.cs             <- 配置项（心跳、重连、鉴权）
|   +-- Messages/
|   |   +-- SignalRMessage.cs     <- 消息类型定义
|   +-- Protocol/
|   |   +-- SignalRProtocol.cs    <- JSON 编解码 + 0x1E 分隔符
|   +-- Transport/
|   |   +-- WebSocketTransport.cs <- UnityWebSocket 适配器 + SignalR 握手
|   +-- Negotiation/
|   |   +-- NegotiationResult.cs  <- HTTP negotiate（UnityWebRequest）
|   +-- Utility/
|       +-- SimpleJson.cs         <- 内置轻量 JSON 解析器（无外部依赖）
|       +-- SignalRLiteRunner.cs  <- MonoBehaviour 单例（协程 + Update）
+-- Tests/                        <- EditMode + PlayMode 测试
+-- Samples/                      <- 示例代码
+-- ServerExample~/               <- ASP.NET Core 测试服务器（Unity 自动忽略 ~ 目录）
```

### 连接流程

```
StartConnect()
    |
    +--[需要协商]--> POST /hub/negotiate --> 获取 connectionToken
    |                                              |
    +--[跳过协商]----------> ConnectWebSocket(ws://hub?id=token)
                                       |
                              WS 打开 -> 发送握手 {"protocol":"json","version":1}
                                       |
                              服务端回复 {} --> 连接成功
                                       |
                              触发 OnConnected
                                       |
                    +-- 每 15 秒发送 Ping --------+
                    +-- 30 秒无响应触发重连 --------+
```

### 依赖对比

| 组件 | SignalR Lite |
|---|---|
| WebSocket | `UnityWebSocket`（psygames）|
| HTTP | `UnityWebRequest`（内置）|
| JSON | `SimpleJson`（内置，约 150 行）|
| 异步模型 | 协程 + 回调 |
| WebGL | 支持 |
| 包体积 | 约 10 KB |
| 外部依赖数 | 1 个 |

---

## 运行测试

### Edit Mode 测试（无需进入 Play Mode）

打开 `Window -> General -> Test Runner -> EditMode`，点击 **Run All**。

| 测试套件 | 数量 | 覆盖范围 |
|---|---|---|
| `SimpleJsonTests` | 35 | JSON 解析 / 序列化 / 往返 |
| `ProtocolTests` | 22 | SignalR 编解码 / 往返 |

### Play Mode 测试

打开 `Window -> General -> Test Runner -> PlayMode`，点击 **Run All**。

### 集成测试（需要本地服务器）

**第一步** — 启动测试服务器：

```powershell
dotnet run --project ServerExample~/SignalRTestServer
# Hub 地址：http://localhost:5000/testhub
```

**第二步** — 在 `HubConnectionTests.cs` 中设置：

```csharp
private const bool ServerRunning = true;
```

**第三步** — 在 Unity Test Runner 中运行 PlayMode 测试。

---

## 真机测试

Unity Test Framework 不支持真机运行。请使用内置的 **Device Test** 示例代替。

通过 `Package Manager -> SignalR Lite -> Samples -> Device Test` 导入。

| 按钮 | 测试内容 |
|---|---|
| **Run All** | 依次运行所有测试并输出汇总 |
| **Connect** | 连接 + 断开，验证事件触发 |
| **Echo** | `Invoke<string>` 往返测试 |
| **GetTime** | 服务端返回值非空验证 |
| **Complex Type** | `JsonUtility` 复杂类型反序列化（检测 IL2CPP 裁剪问题） |
| **Reconnect** | 切换到后台 35 秒，验证 `OnReconnecting` 触发 |

### 各平台注意事项

| 平台 | 注意事项 |
|---|---|
| **iOS** | 必须使用 `wss://`（App Transport Security 阻止 `ws://`） |
| **Android** | Android 7+ 需要 `wss://`；测试后台切换重连 |
| **WebGL** | 服务端需配置 CORS；HTTPS 页面必须使用 `wss://` |
| **所有平台** | IL2CPP + 代码裁剪 — 如反序列化结果为 null，请添加 `link.xml` |

### link.xml（防止 IL2CPP 裁剪 JsonUtility 类型）

在 `Assets/link.xml` 中添加：

```xml
<linker>
  <assembly fullname="Assembly-CSharp">
    <type fullname="SignalRLiteDeviceTest+PlayerData" preserve="all"/>
  </assembly>
</linker>
```

---

## 平台支持

| 平台 | 状态 |
|---|---|
| Windows / macOS / Linux | 支持 |
| iOS | 支持 |
| Android | 支持 |
| WebGL | 支持 |
| 主机（PS5 / Xbox / Switch）| 支持（同 UnityWebSocket） |

---

## 开源协议

MIT License — 详见 [LICENSE](LICENSE)

---

## 致谢

- [psygames/UnityWebSocket](https://github.com/psygames/UnityWebSocket) — 提供跨平台（尤其是 WebGL）WebSocket 支持的核心底层
- [ASP.NET Core SignalR 协议规范](https://github.com/dotnet/aspnetcore/tree/main/src/SignalR/docs/specs)
