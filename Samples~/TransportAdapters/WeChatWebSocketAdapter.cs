// WeChatWebSocketAdapter.cs
//
// 微信小游戏 WebSocket 适配器。
//
// 启用步骤：
//   1. 导入微信小游戏 Unity SDK
//      https://github.com/wechat-miniprogram/minigame-unity-webgl-transform
//   2. 在 Project Settings → Player → Scripting Define Symbols 添加：
//      SIGNALRLITE_UNITYWSSOCKET;WECHAT_MINIGAME
//      （两个宏都需要，因为 asmdef 的 defineConstraints 要求 SIGNALRLITE_UNITYWSSOCKET）
//   3. 在 asmdef（SignalRLite.Adapters.asmdef）的 references 中加入 WX SDK 的程序集名称
//   4. 在 HubOptions 中注入：
//      hub.Options.WebSocketFactory = url => new WeChatWebSocketAdapter(url);

#if SIGNALRLITE_UNITYWSSOCKET && WECHAT_MINIGAME

using System;
using SignalRLite.Transport;
using WeChatWASM;

namespace SignalRLite.Adapters
{
    /// <summary>
    /// <see cref="IWebSocketClient"/> adapter for the WeChat Mini Game WebSocket API.
    /// Requires scripting defines <c>SIGNALRLITE_UNITYWSSOCKET</c> and <c>WECHAT_MINIGAME</c>.
    /// </summary>
    public sealed class WeChatWebSocketAdapter : IWebSocketClient
    {
        private readonly string  _url;
        private bool             _disposed;
        private WXWebSocketTask  _task;

        public event Action         OnOpen;
        public event Action<string> OnTextMessage;
        public event Action<byte[]> OnBinaryMessage;
        public event Action<string> OnClose;
        public event Action<string> OnError;

        public WeChatWebSocketAdapter(string url) => _url = url;

        public void Connect()
        {
            _disposed = false;
            _task     = WX.ConnectSocket(new WXConnectSocketOption { url = _url });

            _task.OnOpen(res =>
            {
                if (!_disposed) OnOpen?.Invoke();
            });

            _task.OnMessage(res =>
            {
                if (_disposed) return;
                if (!string.IsNullOrEmpty(res.data))
                    OnTextMessage?.Invoke(res.data);
                else if (res.dataBuffer != null)
                    OnBinaryMessage?.Invoke(res.dataBuffer);
            });

            _task.OnClose(res =>
            {
                if (!_disposed) OnClose?.Invoke(res.reason ?? string.Empty);
            });

            _task.OnError(res =>
            {
                if (!_disposed) OnError?.Invoke(res.errMsg ?? "Unknown error");
            });
        }

        public void SendText(string data)   => _task?.Send(new WXSendSocketMessageOption { data = data });
        public void SendBinary(byte[] data) => _task?.Send(new WXSendSocketMessageOption { dataBuffer = data });
        public void Close()                 => _task?.Close(new WXCloseSocketOption());

        public void Dispose()
        {
            _disposed = true;
            _task     = null;
        }
    }
}

#endif
