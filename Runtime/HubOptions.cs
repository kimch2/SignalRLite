using System;
using SignalRLite.Authentication;
using SignalRLite.Encoders;
using SignalRLite.Transport;


namespace SignalRLite
{
    /// <summary>
    /// Configuration options for a <see cref="HubConnection"/>.
    /// </summary>
    public class HubOptions
    {
        // ── Protocol ─────────────────────────────────────────────────────────

        /// <summary>
        /// Wire protocol. Default is <see cref="JsonProtocol"/> (no extra dependencies).
        /// <para>
        /// To use MessagePack-CSharp (neuecc/MessagePack-CSharp):
        /// <code>
        /// Protocol = new MessagePackCSharpProtocol()
        /// </code>
        /// Requires the <c>MessagePack</c> package <b>and</b> the scripting define
        /// <c>SIGNALRLITE_MESSAGEPACK_CSHARP</c>.
        /// </para>
        /// <para>
        /// To use GameDevWare's MessagePack:
        /// <code>
        /// Protocol = new MessagePackProtocol()
        /// </code>
        /// Requires the asset-store package <b>and</b> the scripting define
        /// <c>SIGNALRLITE_GAMEDEVWARE_MESSAGEPACK</c>.
        /// </para>
        /// <para>
        /// To use Newtonsoft.Json for JSON:
        /// <code>
        /// Protocol = new JsonProtocol(new JsonDotNetEncoder())
        /// </code>
        /// Requires Newtonsoft.Json <b>and</b> the scripting define
        /// <c>SIGNALRLITE_NEWTONSOFT_JSON</c>.
        /// </para>
        /// </summary>
        public ISignalRProtocol Protocol { get; set; } = new JsonProtocol();

        // ── Authentication ───────────────────────────────────────────────────

        /// <summary>
        /// Pluggable authentication provider.
        /// <para>
        /// When set, <see cref="IAuthenticationProvider.PrepareRequest"/> is called before
        /// every negotiation request, and <see cref="IAuthenticationProvider.PrepareUri"/> is
        /// called when building the WebSocket URL.
        /// </para>
        /// <para>
        /// If <c>null</c> and <see cref="AccessToken"/> is set, a
        /// <see cref="DefaultAccessTokenAuthenticator"/> is created automatically.
        /// </para>
        /// <example>
        /// Static token:
        /// <code>
        /// AuthenticationProvider = new DefaultAccessTokenAuthenticator("my-jwt")
        /// </code>
        /// Dynamic token (resolved at request time):
        /// <code>
        /// // Set after constructing HubConnection so the provider can reference it.
        /// hub.Options.AuthenticationProvider = new DefaultAccessTokenAuthenticator(hub);
        /// </code>
        /// </example>
        /// </summary>
        public IAuthenticationProvider AuthenticationProvider { get; set; }

        // ── Connection ───────────────────────────────────────────────────────

        /// <summary>
        /// When true, skips the HTTP negotiate step and connects directly via WebSocket.
        /// Requires the server to also allow skip-negotiate.
        /// Default: false.
        /// </summary>
        public bool SkipNegotiation { get; set; } = false;

        /// <summary>
        /// Convenience shorthand for a static Bearer token.
        /// Equivalent to setting <c>AuthenticationProvider = new DefaultAccessTokenAuthenticator(value)</c>.
        /// Ignored when <see cref="AuthenticationProvider"/> is already set.
        /// </summary>
        public string AccessToken { get; set; }

        /// <summary>
        /// How often to send a Ping message to keep the connection alive.
        /// Default: 15 seconds.
        /// </summary>
        public TimeSpan PingInterval { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// If no message is received within this window the connection is considered dead
        /// and a reconnect is triggered.  Default: 30 seconds.
        /// </summary>
        public TimeSpan PingTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Custom retry delays. null entry means stop retrying.
        /// Default: 0, 2, 10, 30 seconds then stop.
        /// </summary>
        public TimeSpan?[] ReconnectDelays { get; set; } = new TimeSpan?[]
        {
            TimeSpan.Zero,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
            null,
        };

        /// <summary>Maximum number of negotiation redirects to follow. Default: 10.</summary>
        public int MaxRedirects { get; set; } = 10;

        // ── Transport ────────────────────────────────────────────────────────

        /// <summary>
        /// Factory that creates the <see cref="IWebSocketClient"/> for a given URL.
        /// <para>
        /// Must be assigned before calling <c>Connect()</c>. Enable the built-in adapter by
        /// adding the scripting define <c>SIGNALRLITE_UNITYWSSOCKET</c> and setting:
        /// <code>
        /// WebSocketFactory = url => new UnityWebSocketAdapter(url)
        /// </code>
        /// </para>
        /// <para>
        /// For WeChat or Douyin mini-games, see the adapter samples in
        /// <c>Samples/TransportAdapters/</c>.
        /// </para>
        /// </summary>
        public Func<string, IWebSocketClient> WebSocketFactory { get; set; }
    }
}
