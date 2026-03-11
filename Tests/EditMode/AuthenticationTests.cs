using System;
using NUnit.Framework;
using SignalRLite.Authentication;
using UnityEngine.Networking;

namespace SignalRLite.Tests
{
    /// <summary>
    /// Edit-Mode tests for DefaultAccessTokenAuthenticator.
    /// No Unity runtime or network required.
    /// </summary>
    [TestFixture]
    public class AuthenticationTests
    {
        // ── IsPreAuthRequired ────────────────────────────────────────────────

        [Test]
        public void IsPreAuthRequired_IsFalse()
        {
            var auth = new DefaultAccessTokenAuthenticator("token");
            Assert.IsFalse(auth.IsPreAuthRequired);
        }

        // ── PrepareRequest: HTTP adds Authorization header ────────────────────

        [Test]
        public void PrepareRequest_Http_AddsAuthorizationHeader()
        {
            var auth    = new DefaultAccessTokenAuthenticator("my-secret-token");
            var request = new UnityWebRequest("http://localhost:5000/testhub/negotiate");

            auth.PrepareRequest(request);

            Assert.AreEqual("Bearer my-secret-token",
                            request.GetRequestHeader("Authorization"));
        }

        [Test]
        public void PrepareRequest_Https_AddsAuthorizationHeader()
        {
            var auth    = new DefaultAccessTokenAuthenticator("https-token");
            var request = new UnityWebRequest("https://example.com/hub/negotiate");

            auth.PrepareRequest(request);

            Assert.AreEqual("Bearer https-token",
                            request.GetRequestHeader("Authorization"));
        }

        [Test]
        public void PrepareRequest_WebSocket_DoesNotAddHeader()
        {
            // WebSocket connections cannot carry custom headers —
            // access_token is passed as a query parameter via PrepareUri instead.
            var auth    = new DefaultAccessTokenAuthenticator("ws-token");
            var request = new UnityWebRequest("ws://localhost:5000/testhub");

            auth.PrepareRequest(request);

            Assert.IsNull(request.GetRequestHeader("Authorization"));
        }

        [Test]
        public void PrepareRequest_EmptyToken_DoesNotAddHeader()
        {
            var auth    = new DefaultAccessTokenAuthenticator("");
            var request = new UnityWebRequest("http://localhost/negotiate");

            auth.PrepareRequest(request);

            Assert.IsNull(request.GetRequestHeader("Authorization"));
        }

        // ── PrepareUri: WebSocket appends access_token query param ────────────

        [Test]
        public void PrepareUri_Ws_AppendsAccessToken()
        {
            var auth = new DefaultAccessTokenAuthenticator("abc123");
            var uri  = new Uri("ws://localhost:5000/testhub");

            Uri result = auth.PrepareUri(uri);

            StringAssert.Contains("access_token=abc123", result.Query);
        }

        [Test]
        public void PrepareUri_Wss_AppendsAccessToken()
        {
            var auth = new DefaultAccessTokenAuthenticator("secure-token");
            var uri  = new Uri("wss://example.com/hub");

            Uri result = auth.PrepareUri(uri);

            StringAssert.Contains("access_token=secure-token", result.Query);
        }

        [Test]
        public void PrepareUri_Http_DoesNotAppendToken()
        {
            // HTTP URIs are covered by the Authorization header, not query params.
            var auth = new DefaultAccessTokenAuthenticator("token");
            var uri  = new Uri("http://localhost/negotiate");

            Uri result = auth.PrepareUri(uri);

            StringAssert.DoesNotContain("access_token", result.Query);
        }

        [Test]
        public void PrepareUri_PreservesExistingQueryParams()
        {
            var auth = new DefaultAccessTokenAuthenticator("tok");
            var uri  = new Uri("ws://localhost/hub?id=conn123");

            Uri result = auth.PrepareUri(uri);

            StringAssert.Contains("id=conn123",     result.Query);
            StringAssert.Contains("access_token=tok", result.Query);
        }

        [Test]
        public void PrepareUri_TokenIsUrlEncoded()
        {
            // Tokens can contain special characters that need encoding.
            var auth = new DefaultAccessTokenAuthenticator("tok+en/val=ue");
            var uri  = new Uri("ws://localhost/hub");

            Uri result = auth.PrepareUri(uri);

            StringAssert.Contains("access_token=", result.Query);
            StringAssert.DoesNotContain("tok+en/val=ue", result.Query,
                "raw unencoded token should not appear in URI");
        }

        [Test]
        public void PrepareUri_EmptyToken_DoesNotAppendParam()
        {
            var auth = new DefaultAccessTokenAuthenticator("");
            var uri  = new Uri("ws://localhost/hub");

            Uri result = auth.PrepareUri(uri);

            StringAssert.DoesNotContain("access_token", result.ToString());
        }

        [Test]
        public void PrepareUri_DoubleQuestionMark_IsFixed()
        {
            // UriBuilder can produce ??-prefixed queries on some .NET versions.
            // Simulate by constructing the URI the same way UriBuilder does internally.
            var auth = new DefaultAccessTokenAuthenticator("tok");

            // Manually build a URI that has ??" in the query (ub.Query already has "?")
            var ub  = new UriBuilder("ws", "localhost", 5000, "/hub");
            ub.Query = "?" + "id=abc";                  // results in ??id=abc on some runtimes
            Uri uri = ub.Uri;

            Uri result = auth.PrepareUri(uri);

            Assert.IsFalse(result.Query.StartsWith("??"),
                $"Query should not start with '??', was: {result.Query}");
        }

        // ── StartAuthentication / Cancel are no-ops ───────────────────────────

        [Test]
        public void StartAuthentication_DoesNotThrow()
        {
            var auth = new DefaultAccessTokenAuthenticator("token");
            Assert.DoesNotThrow(() => auth.StartAuthentication());
        }

        [Test]
        public void Cancel_DoesNotThrow()
        {
            var auth = new DefaultAccessTokenAuthenticator("token");
            Assert.DoesNotThrow(() => auth.Cancel());
        }
    }
}
