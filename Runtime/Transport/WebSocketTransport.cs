using System;
using System.Collections.Generic;
using SignalRLite.Encoders;
using SignalRLite.Messages;
using SignalRLite.Utility;
using UnityEngine;
using UnityWebSocket;

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
    /// WebSocket transport for SignalR Lite, backed by the psygames UnityWebSocket package.
    /// Handles the SignalR handshake internally and forwards decoded messages upward.
    /// Supports both JSON (text) and MessagePack (binary) protocols via <see cref="ISignalRProtocol"/>.
    /// </summary>
    internal class WebSocketTransport : IDisposable
    {
        public TransportState State       { get; private set; } = TransportState.Initial;
        public string         ErrorReason { get; private set; }

        public event Action<List<SignalRMessage>> OnMessages;
        public event Action                       OnConnected;
        public event Action<string>               OnDisconnected;
        public event Action<string>               OnError;

        private IWebSocket        _ws;
        private ISignalRProtocol  _protocol;
        private bool              _handshakeDone;

        // ── Public API ───────────────────────────────────────────────────────

        /// <param name="url">WebSocket URL.</param>
        /// <param name="protocol">Wire protocol (JSON or MessagePack).</param>
        public void Connect(string url, ISignalRProtocol protocol)
        {
            _protocol      = protocol;
            State          = TransportState.Connecting;
            _handshakeDone = false;

            _ws            = new WebSocket(url);
            _ws.OnOpen    += HandleOpen;
            _ws.OnMessage += HandleMessage;
            _ws.OnClose   += HandleClose;
            _ws.OnError   += HandleError;
            _ws.ConnectAsync();
        }

        /// <summary>Send a text frame (JSON protocol).</summary>
        public void Send(string text)
        {
            if (_ws == null || State != TransportState.Connected) return;
            _ws.SendAsync(text);
        }

        /// <summary>Send a binary frame (MessagePack protocol).</summary>
        public void SendBytes(byte[] data)
        {
            if (_ws == null || State != TransportState.Connected) return;
            _ws.SendAsync(data);
        }

        public void Close()
        {
            if (_ws == null || State == TransportState.Closing || State == TransportState.Closed) return;
            State = TransportState.Closing;
            _ws.CloseAsync();
        }

        public void Dispose()
        {
            if (_ws == null) return;
            _ws.OnOpen    -= HandleOpen;
            _ws.OnMessage -= HandleMessage;
            _ws.OnClose   -= HandleClose;
            _ws.OnError   -= HandleError;
            _ws = null;
        }

        // ── Private event handlers ───────────────────────────────────────────

        private void HandleOpen(object sender, OpenEventArgs e)
        {
            // The handshake request is always a JSON text frame,
            // regardless of the selected protocol.
            _ws.SendAsync(_protocol.HandshakeRequest);
        }

        private void HandleMessage(object sender, MessageEventArgs e)
        {
            if (!_handshakeDone)
            {
                // Handshake response is always a JSON text frame.
                if (!e.IsText) return;
                HandleHandshake(e.Data);
                return;
            }

            if (_protocol.IsBinary)
            {
                if (e.IsBinary) DispatchBinary(e.RawData);
            }
            else
            {
                if (e.IsText) DispatchText(e.Data);
            }
        }

        private void HandleClose(object sender, CloseEventArgs e)
        {
            State = TransportState.Closed;
            OnDisconnected?.Invoke(e.Reason);
        }

        private void HandleError(object sender, ErrorEventArgs e)
        {
            ErrorReason = e.Message;
            if (State != TransportState.Closing && State != TransportState.Closed)
            {
                State = TransportState.Failed;
                OnError?.Invoke(e.Message);
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

            // Any data after the separator in the same frame is normal traffic.
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
