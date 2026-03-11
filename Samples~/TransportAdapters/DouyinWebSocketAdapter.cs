// DouyinWebSocketAdapter.cs
//
// 抖音小游戏 WebSocket 适配器。
//
// 启用步骤：
//   1. 导入抖音小游戏 Unity SDK（StarkSDK / TTSDK）
//      https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/unity/overview
//   2. 在 Project Settings → Player → Scripting Define Symbols 添加：
//      SIGNALRLITE_UNITYWSSOCKET;DOUYIN_MINIGAME
//   3. 在 asmdef（SignalRLite.Adapters.asmdef）的 references 中加入 StarkSDK 的程序集名称
//   4. 在 HubOptions 中注入：
//      hub.Options.WebSocketFactory = url => new DouyinWebSocketAdapter(url);

#if SIGNALRLITE_UNITYWSSOCKET && DOUYIN_MINIGAME

using System;
using SignalRLite.Transport;
using StarkSDKSpace;

namespace SignalRLite.Adapters
{
    /// <summary>
    /// <see cref="IWebSocketClient"/> adapter for the Douyin (ByteDance) Mini Game WebSocket API.
    /// Requires scripting defines <c>SIGNALRLITE_UNITYWSSOCKET</c> and <c>DOUYIN_MINIGAME</c>.
    /// </summary>
    public sealed class DouyinWebSocketAdapter : IWebSocketClient
    {
        private readonly string  _url;
        private bool             _disposed;
        private IStarkWebSocket  _socket;

        public event Action         OnOpen;
        public event Action<string> OnTextMessage;
        public event Action<byte[]> OnBinaryMessage;
        public event Action<string> OnClose;
        public event Action<string> OnError;

        public DouyinWebSocketAdapter(string url) => _url = url;

        public void Connect()
        {
            _disposed = false;
            _socket   = StarkSDK.API.GetWebSocketManager().CreateWebSocket(_url);

            _socket.OnOpen += () =>
            {
                if (!_disposed) OnOpen?.Invoke();
            };

            _socket.OnMessage += data =>
            {
                if (!_disposed) OnTextMessage?.Invoke(data);
            };

            _socket.OnBinaryMessage += data =>
            {
                if (!_disposed) OnBinaryMessage?.Invoke(data);
            };

            _socket.OnClose += (code, reason) =>
            {
                if (!_disposed) OnClose?.Invoke(reason ?? string.Empty);
            };

            _socket.OnError += errMsg =>
            {
                if (!_disposed) OnError?.Invoke(errMsg ?? "Unknown error");
            };

            _socket.Connect();
        }

        public void SendText(string data)   => _socket?.Send(data);
        public void SendBinary(byte[] data) => _socket?.Send(data);
        public void Close()                 => _socket?.Close();

        public void Dispose()
        {
            _disposed = true;
            _socket   = null;
        }
    }
}

#endif
