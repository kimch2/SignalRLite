using System;
using UnityEngine.Networking;

namespace SignalRLite.Authentication
{
    /// <summary>
    /// Default access-token authenticator that uses the Bearer token scheme.
    /// <list type="bullet">
    ///   <item>HTTP/HTTPS requests → <c>Authorization: Bearer &lt;token&gt;</c> header.</item>
    ///   <item>WebSocket URIs (ws/wss) → <c>?access_token=&lt;token&gt;</c> query parameter.</item>
    /// </list>
    /// <para>
    /// Usage:
    /// <code>
    /// var hub = new HubConnection(url, new HubOptions
    /// {
    ///     AuthenticationProvider = new DefaultAccessTokenAuthenticator("my-jwt-token")
    /// });
    /// </code>
    /// Or, to pick the token dynamically at connection time (including server-redirected tokens):
    /// <code>
    /// var hub = new HubConnection(url);
    /// hub.Options.AuthenticationProvider = new DefaultAccessTokenAuthenticator(hub);
    /// hub.StartConnect();
    /// </code>
    /// </para>
    /// </summary>
    public sealed class DefaultAccessTokenAuthenticator : IAuthenticationProvider
    {
        // ── IAuthenticationProvider ──────────────────────────────────────────

        /// <summary>No pre-authentication step required.</summary>
        public bool IsPreAuthRequired => false;

#pragma warning disable 0067
        /// <summary>Not used — <see cref="IsPreAuthRequired"/> is <c>false</c>.</summary>
        public event OnAuthenticationSucceededDelegate OnAuthenticationSucceeded;

        /// <summary>Not used — <see cref="IsPreAuthRequired"/> is <c>false</c>.</summary>
        public event OnAuthenticationFailedDelegate OnAuthenticationFailed;
#pragma warning restore 0067

        // ── State ────────────────────────────────────────────────────────────

        private readonly HubConnection _connection;
        private readonly string        _staticToken;

        // ── Constructors ─────────────────────────────────────────────────────

        /// <summary>
        /// Creates an authenticator with a static Bearer token.
        /// </summary>
        public DefaultAccessTokenAuthenticator(string accessToken)
        {
            _staticToken = accessToken;
        }

        /// <summary>
        /// Creates an authenticator bound to a <see cref="HubConnection"/>.
        /// The token is resolved at request time:
        /// <c>NegotiationResult.AccessToken</c> (server-provided) takes precedence,
        /// then falls back to <c>Options.AccessToken</c>.
        /// </summary>
        public DefaultAccessTokenAuthenticator(HubConnection connection)
        {
            _connection = connection;
        }

        // ── IAuthenticationProvider ──────────────────────────────────────────

        /// <summary>No-op — pre-auth is not required.</summary>
        public void StartAuthentication() { }

        /// <summary>
        /// Adds the <c>Authorization: Bearer</c> header to HTTP/HTTPS requests.
        /// WebSocket connections cannot carry custom headers; use <see cref="PrepareUri"/> instead.
        /// </summary>
        public void PrepareRequest(UnityWebRequest request)
        {
            string token = ResolveToken();
            if (string.IsNullOrEmpty(token)) return;

            // Add Authorization header only for HTTP/HTTPS requests.
            // WebSocket connections (ws/wss) do not support custom headers.
            string scheme = request.uri.Scheme;
            if (scheme == "http" || scheme == "https")
                request.SetRequestHeader("Authorization", "Bearer " + token);
        }

        /// <summary>
        /// Appends <c>?access_token=&lt;token&gt;</c> to WebSocket URIs (ws/wss).
        /// Also strips a leading <c>??</c> double-question-mark if present.
        /// </summary>
        public Uri PrepareUri(Uri uri)
        {
            // Fix double-?? that can appear from UriBuilder on some .NET versions.
            if (uri.Query.StartsWith("??"))
            {
                var builder = new UriBuilder(uri);
                builder.Query = builder.Query.Substring(2);
                uri = builder.Uri;
            }

            // Append access_token only for WebSocket URIs.
            string scheme = uri.Scheme;
            if (scheme == "ws" || scheme == "wss")
                uri = PrepareUriImpl(uri);

            return uri;
        }

        /// <summary>No-op.</summary>
        public void Cancel() { }

        // ── Private helpers ──────────────────────────────────────────────────

        private string ResolveToken()
        {
            if (_connection != null)
            {
                // Prefer server-supplied token (redirect scenario), then fall back
                // to the token set on HubOptions.
                if (_connection.NegotiationResult != null &&
                    !string.IsNullOrEmpty(_connection.NegotiationResult.AccessToken))
                    return _connection.NegotiationResult.AccessToken;

                return _connection.Options.AccessToken;
            }
            return _staticToken;
        }

        private Uri PrepareUriImpl(Uri uri)
        {
            string token = ResolveToken();
            if (string.IsNullOrEmpty(token)) return uri;

            string existingQuery = string.IsNullOrEmpty(uri.Query) ? "" : uri.Query.TrimStart('?') + "&";
            var uriBuilder = new UriBuilder(uri.Scheme, uri.Host, uri.Port, uri.AbsolutePath,
                "?" + existingQuery + "access_token=" + Uri.EscapeDataString(token));
            return uriBuilder.Uri;
        }
    }
}
