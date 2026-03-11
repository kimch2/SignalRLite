// UnityWebSocketAdapter.cs
//
// 默认 WebSocket 适配器，基于 psygames/UnityWebSocket。
//
// 启用步骤：
//   1. 安装 UnityWebSocket 包
//      https://github.com/psygames/UnityWebSocket.git#upm
//   2. 在 Project Settings → Player → Scripting Define Symbols 添加：
//      SIGNALRLITE_UNITYWSSOCKET
//   3. 在 HubOptions 中注入：
//      hub.Options.WebSocketFactory = url => new UnityWebSocketAdapter(url);

#if SIGNALRLITE_UNITYWSSOCKET

using System;
using SignalRLite.Transport;
using UnityWebSocket;

namespace SignalRLite.Adapters
{
    /// <summary>
    /// <see cref="IWebSocketClient"/> adapter backed by psygames/UnityWebSocket.
    /// Requires scripting define <c>SIGNALRLITE_UNITYWSSOCKET</c>.
    /// </summary>
    public sealed class UnityWebSocketAdapter : IWebSocketClient
    {
        private readonly string _url;
        private IWebSocket      _ws;
        private bool            _disposed;

        public event Action         OnOpen;
        public event Action<string> OnTextMessage;
        public event Action<byte[]> OnBinaryMessage;
        public event Action<string> OnClose;
        public event Action<string> OnError;

        public UnityWebSocketAdapter(string url) => _url = url;

        public void Connect()
        {
            _disposed = false;
            _ws       = new WebSocket(_url);

            _ws.OnOpen    += (_, _) => { if (!_disposed) OnOpen?.Invoke(); };
            _ws.OnMessage += (_, e) =>
            {
                if (_disposed) return;
                if (e.IsText) OnTextMessage?.Invoke(e.Data);
                else          OnBinaryMessage?.Invoke(e.RawData);
            };
            _ws.OnClose   += (_, e) => { if (!_disposed) OnClose?.Invoke(e.Reason); };
            _ws.OnError   += (_, e) => { if (!_disposed) OnError?.Invoke(e.Message); };

            _ws.ConnectAsync();
        }

        public void SendText(string data)   => _ws?.SendAsync(data);
        public void SendBinary(byte[] data) => _ws?.SendAsync(data);
        public void Close()                 => _ws?.CloseAsync();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ws?.CloseAsync();
            _ws = null;
        }
    }
}

#endif
