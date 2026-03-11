using System;
using SignalRLite.Transport;

namespace SignalRLite
{
    /// <summary>
    /// Global configuration for SignalRLite.
    /// The default WebSocket factory is automatically registered here when
    /// the <c>SIGNALRLITE_UNITYWSSOCKET</c> scripting define is enabled.
    /// </summary>
    public static class SignalRLiteConfig
    {
        /// <summary>
        /// Global fallback WebSocket factory used when <see cref="HubOptions.WebSocketFactory"/>
        /// is not explicitly set on a connection.
        /// <para>
        /// This is automatically populated at runtime when the adapter assembly is compiled
        /// (i.e., when <c>SIGNALRLITE_UNITYWSSOCKET</c> is defined).
        /// </para>
        /// </summary>
        public static Func<string, IWebSocketClient> DefaultWebSocketFactory { get; set; }
    }
}
