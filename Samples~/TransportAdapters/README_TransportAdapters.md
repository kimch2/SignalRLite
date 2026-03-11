# Transport Adapters

示例适配器，展示如何将 SignalRLite 接入不同平台的 WebSocket API。

## 已提供的适配器

| 文件 | 平台 | 宏定义 |
|------|------|--------|
| `UnityWebSocketAdapter.cs` | 默认（PC / iOS / Android / WebGL） | 无需，自动启用 |
| `WeChatWebSocketAdapter.cs` | 微信小游戏 | `WECHAT_MINIGAME` |
| `DouyinWebSocketAdapter.cs` | 抖音小游戏 | `DOUYIN_MINIGAME` |

## 如何接入微信小游戏

1. 导入微信小游戏 Unity SDK  
   <https://github.com/wechat-miniprogram/minigame-unity-webgl-transform>

2. 添加宏定义 `WECHAT_MINIGAME`  
   `Project Settings → Player → Scripting Define Symbols`

3. 注入适配器：

```csharp
var hub = new HubConnection("wss://your-server/hub");
hub.Options.WebSocketFactory = url => new WeChatWebSocketAdapter(url);
```

## 如何接入抖音小游戏

1. 导入抖音小游戏 Unity SDK（StarkSDK / TTSDK）  
   <https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/unity/overview>

2. 添加宏定义 `DOUYIN_MINIGAME`

3. 注入适配器：

```csharp
var hub = new HubConnection("wss://your-server/hub");
hub.Options.WebSocketFactory = url => new DouyinWebSocketAdapter(url);
```

## 自定义适配器

实现 `IWebSocketClient` 接口即可：

```csharp
public class MyAdapter : IWebSocketClient
{
    public event Action         OnOpen;
    public event Action<string> OnTextMessage;
    public event Action<byte[]> OnBinaryMessage;
    public event Action<string> OnClose;
    public event Action<string> OnError;

    public void Connect()    { /* 建立连接 */ }
    public void SendText(string data)   { /* 发文本帧 */ }
    public void SendBinary(byte[] data) { /* 发二进制帧 */ }
    public void Close()   { /* 关闭 */ }
    public void Dispose() { /* 清理 */ }
}
```

> **注意**：所有事件必须在 Unity 主线程触发。如果平台 SDK 在子线程回调，
> 需要在适配器内部通过 `UnityMainThreadDispatcher` 或协程转发到主线程。
