using System;
using System.Collections.Generic;
using SignalRLite.Encoders;
using SignalRLite.Messages;
using SignalRLite.Utility;
using UnityEngine;

namespace SignalRLite.Transport
{
    internal enum TransportState
    {
        Initial,
        Connecting,
        Connected,
        Closing,
        Closed,
        Failed,
    }

    /// <summary>
    /// WebSocket transport for SignalR Lite.
    /// Handles the SignalR handshake internally and forwards decoded messages upward.
    /// The underlying WebSocket connection is provided by an <see cref="IWebSocketClient"/>
    /// created via the factory supplied in the constructor.
    /// </summary>
    internal class WebSocketTransport : IDisposable
    {
        public TransportState State       { get; private set; } = TransportState.Initial;
        public string         ErrorReason { get; private set; }

        public event Action<List<SignalRMessage>> OnMessages;
        public event Action                       OnConnected;
        public event Action<string>               OnDisconnected;
        public event Action<string>               OnError;

        private readonly Func<string, IWebSocketClient> _factory;
        private IWebSocketClient  _ws;
        private ISignalRProtocol  _protocol;
        private bool              _handshakeDone;

        public WebSocketTransport(Func<string, IWebSocketClient> factory)
            => _factory = factory;

        // ── Public API ───────────────────────────────────────────────────────

        public void Connect(string url, ISignalRProtocol protocol)
        {
            var factory = _factory ?? SignalRLiteConfig.DefaultWebSocketFactory;
            if (factory == null)
                throw new InvalidOperationException(
                    "[SignalRLite] No WebSocket factory is configured.\n" +
                    "Option A – enable the built-in adapter:\n" +
                    "  1. Add scripting define: SIGNALRLITE_UNITYWSSOCKET\n" +
                    "  2. The factory is registered automatically at runtime.\n" +
                    "Option B – supply your own adapter:\n" +
                    "  hub.Options.WebSocketFactory = url => new MyAdapter(url);");

            _protocol      = protocol;
            State          = TransportState.Connecting;
            _handshakeDone = false;

            try
            {
                _ws = factory(url);
            }
            catch (Exception ex)
            {
                State       = TransportState.Failed;
                ErrorReason = ex.Message;
                OnError?.Invoke(ex.Message);
                return;
            }

            _ws.OnOpen          += HandleOpen;
            _ws.OnTextMessage   += HandleTextMessage;
            _ws.OnBinaryMessage += HandleBinaryMessage;
            _ws.OnClose         += HandleClose;
            _ws.OnError         += HandleError;
            _ws.Connect();
        }

        /// <summary>Send a text frame (JSON protocol).</summary>
        public void Send(string text)
        {
            if (_ws == null || State != TransportState.Connected) return;
            _ws.SendText(text);
        }

        /// <summary>Send a binary frame (MessagePack protocol).</summary>
        public void SendBytes(byte[] data)
        {
            if (_ws == null || State != TransportState.Connected) return;
            _ws.SendBinary(data);
        }

        public void Close()
        {
            if (_ws == null || State == TransportState.Closing || State == TransportState.Closed) return;
            State = TransportState.Closing;
            _ws.Close();
        }

        public void Dispose()
        {
            if (_ws == null) return;
            _ws.OnOpen          -= HandleOpen;
            _ws.OnTextMessage   -= HandleTextMessage;
            _ws.OnBinaryMessage -= HandleBinaryMessage;
            _ws.OnClose         -= HandleClose;
            _ws.OnError         -= HandleError;
            _ws.Dispose();
            _ws    = null;
            State  = TransportState.Closed;
        }

        // ── Event handlers ───────────────────────────────────────────────────

        private void HandleOpen()
        {
            _ws.SendText(_protocol.HandshakeRequest);
        }

        private void HandleTextMessage(string data)
        {
            if (!_handshakeDone) { HandleHandshake(data); return; }
            if (!_protocol.IsBinary) DispatchText(data);
        }

        private void HandleBinaryMessage(byte[] data)
        {
            if (!_handshakeDone)
            {
                // ASP.NET Core SignalR sends the handshake response as a binary WebSocket frame
                // when a binary protocol (e.g. MessagePack) was negotiated.
                // The payload is still UTF-8 JSON: {}\x1e
                HandleHandshake(System.Text.Encoding.UTF8.GetString(data));
                return;
            }
            if (_protocol.IsBinary) DispatchBinary(data);
        }

        private void HandleClose(string reason)
        {
            State = TransportState.Closed;
            OnDisconnected?.Invoke(reason);
        }

        private void HandleError(string message)
        {
            ErrorReason = message;
            if (State != TransportState.Closing && State != TransportState.Closed)
            {
                State = TransportState.Failed;
                OnError?.Invoke(message);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void HandleHandshake(string data)
        {
            int    sepIdx        = data.IndexOf(JsonProtocol.Separator);
            string handshakeJson = sepIdx >= 0 ? data.Substring(0, sepIdx) : data;

            if (!ValidateHandshakeJson(handshakeJson))
            {
                State = TransportState.Failed;
                OnError?.Invoke(ErrorReason);
                return;
            }

            _handshakeDone = true;
            State          = TransportState.Connected;
            OnConnected?.Invoke();

            if (!_protocol.IsBinary && sepIdx >= 0 && sepIdx + 1 < data.Length)
                DispatchText(data.Substring(sepIdx + 1));
        }

        private bool ValidateHandshakeJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return true;
            var obj = SimpleJson.Parse(json) as System.Collections.Generic.Dictionary<string, object>;
            if (obj == null) { ErrorReason = "Invalid handshake JSON: " + json; return false; }
            if (obj.TryGetValue("error", out var err) && err != null && !string.IsNullOrEmpty(err.ToString()))
            {
                ErrorReason = err.ToString();
                return false;
            }
            return true;
        }

        private void DispatchText(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            try
            {
                var msgs = _protocol.ParseText(data);
                if (msgs.Count > 0) OnMessages?.Invoke(msgs);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SignalRLite] Error parsing text messages: {ex}");
            }
        }

        private void DispatchBinary(byte[] data)
        {
            if (data == null || data.Length == 0) return;
            try
            {
                var msgs = _protocol.ParseBytes(data, 0, data.Length);
                if (msgs.Count > 0) OnMessages?.Invoke(msgs);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SignalRLite] Error parsing binary messages: {ex}");
            }
        }
    }
}
