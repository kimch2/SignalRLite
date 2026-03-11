using System;
using System.Collections.Generic;

namespace SignalRLite.Transport
{
    /// <summary>
    /// In-memory <see cref="IWebSocketClient"/> implementation for unit testing.
    /// Does not open any real network connection.
    /// <para>
    /// Use the <c>Simulate*</c> methods to drive the hub under test:
    /// <code>
    /// var mock = new MockWebSocketClient();
    /// hub.Options.WebSocketFactory = _ => mock;
    /// hub.StartConnect();
    ///
    /// // Complete the SignalR handshake manually:
    /// mock.SimulateOpen();
    /// mock.SimulateTextMessage("{}\x1e");
    /// // hub is now Connected
    ///
    /// // Deliver an incoming Invocation message:
    /// mock.SimulateTextMessage("{\"type\":1,\"target\":\"OnMsg\",\"arguments\":[42]}\x1e");
    /// </code>
    /// </para>
    /// </summary>
    public sealed class MockWebSocketClient : IWebSocketClient
    {
        // ── Captured outbound frames ─────────────────────────────────────────

        /// <summary>All text frames sent via <see cref="SendText"/>.</summary>
        public List<string> SentText   { get; } = new List<string>();

        /// <summary>All binary frames sent via <see cref="SendBinary"/>.</summary>
        public List<byte[]> SentBinary { get; } = new List<byte[]>();

        /// <summary>True after <see cref="Connect"/> and before <see cref="Close"/>/<see cref="Dispose"/>.</summary>
        public bool IsOpen { get; private set; }

        // ── IWebSocketClient events ──────────────────────────────────────────

        public event Action         OnOpen;
        public event Action<string> OnTextMessage;
        public event Action<byte[]> OnBinaryMessage;
        public event Action<string> OnClose;
        public event Action<string> OnError;

        // ── IWebSocketClient API ─────────────────────────────────────────────

        public void Connect()
        {
            IsOpen = true;
        }

        public void SendText(string data)
        {
            if (IsOpen) SentText.Add(data);
        }

        public void SendBinary(byte[] data)
        {
            if (IsOpen) SentBinary.Add(data);
        }

        public void Close()
        {
            IsOpen = false;
        }

        public void Dispose()
        {
            IsOpen = false;
        }

        // ── Simulation helpers (call from tests) ─────────────────────────────

        /// <summary>
        /// Fires <see cref="OnOpen"/>. The hub will respond by sending the SignalR
        /// handshake request; call <see cref="SimulateHandshakeOk"/> right after to
        /// complete the connection.
        /// </summary>
        public void SimulateOpen() => OnOpen?.Invoke();

        /// <summary>
        /// Sends the default empty-error handshake response and completes the
        /// SignalR connection handshake in one step.
        /// Equivalent to <c>SimulateTextMessage("{}\x1e")</c>.
        /// </summary>
        public void SimulateHandshakeOk() => OnTextMessage?.Invoke("{}\x1e");

        /// <summary>Delivers an arbitrary text frame to the hub.</summary>
        public void SimulateTextMessage(string data) => OnTextMessage?.Invoke(data);

        /// <summary>Delivers an arbitrary binary frame to the hub.</summary>
        public void SimulateBinaryMessage(byte[] data) => OnBinaryMessage?.Invoke(data);

        /// <summary>
        /// Fires <see cref="OnClose"/> and marks the client as closed.
        /// The hub will transition to Disconnected.
        /// </summary>
        public void SimulateClose(string reason = "")
        {
            IsOpen = false;
            OnClose?.Invoke(reason);
        }

        /// <summary>Fires <see cref="OnError"/>. The hub will transition to a Failed/Disconnected state.</summary>
        public void SimulateError(string message) => OnError?.Invoke(message);

        /// <summary>
        /// Convenience: completes a full SignalR connection handshake.
        /// Calls <see cref="SimulateOpen"/> then <see cref="SimulateHandshakeOk"/>.
        /// After this the hub state is <c>Connected</c>.
        /// </summary>
        public void SimulateConnected()
        {
            SimulateOpen();
            SimulateHandshakeOk();
        }
    }
}
