using System;
using System.Collections;
using System.Collections.Generic;
using SignalRLite.Authentication;
using SignalRLite.Utility;
using UnityEngine;
using UnityEngine.Networking;

namespace SignalRLite.Negotiation
{
    /// <summary>
    /// Handles the HTTP POST /negotiate request required by the ASP.NET Core SignalR protocol.
    /// https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/docs/specs/TransportProtocols.md
    /// </summary>
    public class NegotiationResult
    {
        public string ConnectionId    { get; set; }
        public string ConnectionToken { get; set; }

        /// <summary>Redirect URL returned by the server (optional).</summary>
        public string RedirectUrl     { get; set; }

        /// <summary>Access token accompanying a redirect (optional).</summary>
        public string AccessToken     { get; set; }

        /// <summary>Non-null when negotiation failed.</summary>
        public string Error           { get; set; }

        /// <summary>
        /// The token to append as ?id= when building the WebSocket URL.
        /// Prefers ConnectionToken over ConnectionId.
        /// </summary>
        public string IdToken => !string.IsNullOrEmpty(ConnectionToken) ? ConnectionToken : ConnectionId;

        // ── Coroutine entry point ────────────────────────────────────────────

        public static IEnumerator Negotiate(
            Uri hubUri,
            IAuthenticationProvider provider,
            Action<NegotiationResult> onComplete)
        {
            string url = BuildNegotiateUrl(hubUri);
            using var request = new UnityWebRequest(url, "POST");
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            // Let the authentication provider add its headers (e.g. Authorization: Bearer).
            provider?.PrepareRequest(request);

            yield return request.SendWebRequest();

            var result = new NegotiationResult();

#if UNITY_2020_1_OR_NEWER
            bool isNetworkError = request.result == UnityWebRequest.Result.ConnectionError ||
                                  request.result == UnityWebRequest.Result.ProtocolError;
#else
            bool isNetworkError = request.isNetworkError || request.isHttpError;
#endif
            if (isNetworkError)
            {
                result.Error = $"Negotiation HTTP error: {request.error} ({request.downloadHandler?.text})";
                onComplete(result);
                yield break;
            }

            string body = request.downloadHandler.text;
            var json = SimpleJson.Parse(body) as Dictionary<string, object>;

            if (json == null)
            {
                result.Error = "Negotiation: failed to parse response: " + body;
                onComplete(result);
                yield break;
            }

            if (json.TryGetValue("error", out var err) && err != null)
            {
                result.Error = err.ToString();
                onComplete(result);
                yield break;
            }

            if (json.TryGetValue("connectionId",    out var cid))  result.ConnectionId    = cid as string;
            if (json.TryGetValue("connectionToken", out var ctok)) result.ConnectionToken = ctok as string;
            if (json.TryGetValue("url",             out var rurl)) result.RedirectUrl     = rurl as string;
            if (json.TryGetValue("accessToken",     out var at))   result.AccessToken     = at  as string;

            onComplete(result);
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static string BuildNegotiateUrl(Uri hubUri)
        {
            var ub   = new UriBuilder(hubUri);
            ub.Path  = ub.Path.TrimEnd('/') + "/negotiate";
            string q = (ub.Query == null || ub.Query.Length <= 1)
                ? "negotiateVersion=1"
                : ub.Query.TrimStart('?') + "&negotiateVersion=1";
            ub.Query = q;
            return ub.Uri.ToString();
        }
    }
}
