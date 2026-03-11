using System;

namespace SignalRLite.Transport
{
    /// <summary>
    /// Platform-agnostic WebSocket connection.
    /// Implement this interface to replace the default UnityWebSocket backend
    /// with WeChat, Douyin, or any other native WebSocket API.
    /// <para>
    /// All events must be dispatched on the Unity main thread.
    /// </para>
    /// </summary>
    public interface IWebSocketClient : IDisposable
    {
        /// <summary>Initiate the WebSocket connection.</summary>
        void Connect();

        /// <summary>Send a UTF-8 text frame.</summary>
        void SendText(string data);

        /// <summary>Send a binary frame.</summary>
        void SendBinary(byte[] data);

        /// <summary>Initiate a graceful close handshake.</summary>
        void Close();

        /// <summary>Fires when the connection is open and ready.</summary>
        event Action OnOpen;

        /// <summary>Fires when a UTF-8 text frame is received.</summary>
        event Action<string> OnTextMessage;

        /// <summary>Fires when a binary frame is received.</summary>
        event Action<byte[]> OnBinaryMessage;

        /// <summary>Fires when the connection is closed. Parameter is the close reason (may be empty).</summary>
        event Action<string> OnClose;

        /// <summary>Fires on connection or protocol error.</summary>
        event Action<string> OnError;
    }
}
