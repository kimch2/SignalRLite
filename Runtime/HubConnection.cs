using System;
using System.Collections;
using System.Collections.Generic;
using SignalRLite.Authentication;
using SignalRLite.Encoders;
using SignalRLite.Messages;
using SignalRLite.Negotiation;
using SignalRLite.Transport;
using SignalRLite.Utility;
using UnityEngine;

namespace SignalRLite
{
    // ── Connection state ─────────────────────────────────────────────────────

    public enum HubConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
    }

    // ── Invocation callback wrapper ──────────────────────────────────────────

    internal sealed class InvocationDef
    {
        public Action<SignalRMessage> Callback;
        public Type                  ReturnType;
    }

    // ── Subscription entries (store original delegate for per-callback removal) ─

    internal sealed class HandlerEntry
    {
        public Type[]           ParamTypes;
        public Action<object[]> Wrapper;
        public Delegate         Original;   // the Action<T1...> passed by the caller
    }

    internal sealed class FuncHandlerEntry
    {
        public Type                   ReturnType;
        public Type[]                 ParamTypes;
        public Func<object[], object> Wrapper;
        public Delegate               Original;   // the Func<T1..., TResult> passed by the caller
    }

    internal sealed class Subscription
    {
        public readonly List<HandlerEntry>     Handlers     = new List<HandlerEntry>();
        public readonly List<FuncHandlerEntry> FuncHandlers = new List<FuncHandlerEntry>();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HubConnection
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Lightweight ASP.NET Core SignalR client for Unity.
    /// Uses UnityWebSocket (psygames) for WebSocket transport and UnityWebRequest for negotiation.
    /// </summary>
    public sealed class HubConnection
    {
        // ── Public properties ────────────────────────────────────────────────

        public Uri                Uri               { get; private set; }
        public HubOptions         Options           { get; }
        public HubConnectionState State             { get; private set; } = HubConnectionState.Disconnected;

        /// <summary>The result of the last successful negotiation. Available after connection.</summary>
        public NegotiationResult  NegotiationResult { get; private set; }

        // ── Events ───────────────────────────────────────────────────────────

        /// <summary>Fired when the connection is fully established (after handshake).</summary>
        public event Action<HubConnection> OnConnected;

        /// <summary>Fired when the connection closes cleanly.</summary>
        public event Action<HubConnection, string> OnDisconnected;

        /// <summary>Fired on any unrecoverable error.</summary>
        public event Action<HubConnection, string> OnError;

        /// <summary>Fired when an automatic reconnect attempt begins.</summary>
        public event Action<HubConnection> OnReconnecting;

        /// <summary>Fired when an automatic reconnect succeeds.</summary>
        public event Action<HubConnection> OnReconnected;

        // ── Interceptor ─────────────────────────────────────────────────────

        /// <summary>
        /// Optional message interceptor.  Return false to suppress further processing.
        /// Useful for logging or custom handling of raw protocol messages.
        /// </summary>
        public Func<HubConnection, SignalRMessage, bool> OnMessage;

        // ── Internal state ───────────────────────────────────────────────────

        private WebSocketTransport                        _transport;
        private readonly Dictionary<long, InvocationDef>  _invocations   = new Dictionary<long, InvocationDef>();
        private readonly Dictionary<string, Subscription> _subscriptions = new Dictionary<string, Subscription>(StringComparer.OrdinalIgnoreCase);
        private long      _nextInvocationId;
        private bool      _reconnectEnabled = true;
        private uint      _reconnectAttempts;
        private float     _lastPingTime;
        private float     _lastMessageTime;
        private int       _redirectCount;
        private Coroutine _reconnectCoroutine;
        private Coroutine _connectCoroutine;

        // ── Constructors ─────────────────────────────────────────────────────

        public HubConnection(string url, HubOptions options = null)
            : this(new Uri(url), options) { }

        public HubConnection(Uri uri, HubOptions options = null)
        {
            Uri     = uri;
            Options = options ?? new HubOptions();
        }

        // ═════════════════════════════════════════════════════════════════════
        // Connect / Close
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>Begins an asynchronous connection attempt.</summary>
        public void StartConnect()
        {
            if (State != HubConnectionState.Disconnected) return;
            _reconnectEnabled  = true;
            _reconnectAttempts = 0;
            _redirectCount     = 0;

            // Lazily create DefaultAccessTokenAuthenticator from the simple AccessToken shorthand.
            if (Options.AuthenticationProvider == null && !string.IsNullOrEmpty(Options.AccessToken))
                Options.AuthenticationProvider = new DefaultAccessTokenAuthenticator(this);

            SetState(HubConnectionState.Connecting);

            if (Options.AuthenticationProvider != null && Options.AuthenticationProvider.IsPreAuthRequired)
                _connectCoroutine = SignalRLiteRunner.Instance.StartCoroutine(PreAuthenticate());
            else
                ConnectInternal();
        }

        private IEnumerator PreAuthenticate()
        {
            bool   done = false;
            string err  = null;

            OnAuthenticationSucceededDelegate onSucceeded = _ => done = true;
            OnAuthenticationFailedDelegate    onFailed    = (_, reason) => { err = reason; done = true; };

            Options.AuthenticationProvider.OnAuthenticationSucceeded += onSucceeded;
            Options.AuthenticationProvider.OnAuthenticationFailed    += onFailed;
            Options.AuthenticationProvider.StartAuthentication();

            while (!done && _reconnectEnabled) yield return null;

            Options.AuthenticationProvider.OnAuthenticationSucceeded -= onSucceeded;
            Options.AuthenticationProvider.OnAuthenticationFailed    -= onFailed;
            _connectCoroutine = null;

            if (!_reconnectEnabled) yield break;
            if (err != null) { HandleFatalError("Pre-authentication failed: " + err); yield break; }
            ConnectInternal();
        }

        /// <summary>Closes the connection and disables automatic reconnection.</summary>
        public void StartClose()
        {
            // Only fire OnDisconnected when the connection was active (Connected or Reconnecting).
            // Calling StartClose during Connecting simply cancels the attempt silently.
            bool wasActive = State == HubConnectionState.Connected
                          || State == HubConnectionState.Reconnecting;

            _reconnectEnabled = false;
            SignalRLiteRunner.Instance.UnregisterUpdate(OnUpdate);

            if (_connectCoroutine != null)
            {
                SignalRLiteRunner.Instance.StopCoroutine(_connectCoroutine);
                _connectCoroutine = null;
            }
            if (_reconnectCoroutine != null)
            {
                SignalRLiteRunner.Instance.StopCoroutine(_reconnectCoroutine);
                _reconnectCoroutine = null;
            }
            if (_transport != null)
            {
                DetachTransport();
                _transport.Close();
                _transport.Dispose();
                _transport = null;
            }

            FailPendingInvocations("Connection closed.");

            SetState(HubConnectionState.Disconnected);
            if (wasActive)
                SafeInvoke(() => OnDisconnected?.Invoke(this, null));
        }

        // ═════════════════════════════════════════════════════════════════════
        // Send / Invoke
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>Calls a hub method on the server without waiting for a response.</summary>
        public void Send(string target, params object[] args)
        {
            EnsureConnected();
            SendMessage(new SignalRMessage
            {
                Type      = MessageType.Invocation,
                Target    = target,
                Arguments = args,
                // NonBlocking = true signals fire-and-forget (no InvocationId)
            });
        }

        /// <summary>Calls a hub method and invokes <paramref name="callback"/> with (result, error) on completion.</summary>
        public void Invoke(string target, Action<object, string> callback, params object[] args)
        {
            EnsureConnected();
            long id = ++_nextInvocationId;
            if (callback != null)
                _invocations[id] = new InvocationDef
                {
                    Callback   = msg => callback(msg.Result, msg.Error),
                    ReturnType = typeof(object),
                };
            SendMessage(new SignalRMessage
            {
                Type         = MessageType.Invocation,
                InvocationId = id.ToString(),
                Target       = target,
                Arguments    = args,
            });
        }

        /// <summary>Calls a hub method and invokes <paramref name="callback"/> with (T result, string error) on completion.</summary>
        public void Invoke<T>(string target, Action<T, string> callback, params object[] args)
        {
            EnsureConnected();
            long id = ++_nextInvocationId;
            if (callback != null)
                _invocations[id] = new InvocationDef
                {
                    Callback   = msg => callback(ConvertTo<T>(msg.Result), msg.Error),
                    ReturnType = typeof(T),
                };
            SendMessage(new SignalRMessage
            {
                Type         = MessageType.Invocation,
                InvocationId = id.ToString(),
                Target       = target,
                Arguments    = args,
            });
        }

        // ═════════════════════════════════════════════════════════════════════
        // Subscribe  (On / Off)
        // ═════════════════════════════════════════════════════════════════════

        public void On(string target, Action callback)
            => Subscribe(target, null, _ => callback(), callback);

        public void On<T1>(string target, Action<T1> callback)
            => Subscribe(target, new[] { typeof(T1) },
                args => callback(ConvertTo<T1>(args[0])), callback);

        public void On<T1, T2>(string target, Action<T1, T2> callback)
            => Subscribe(target, new[] { typeof(T1), typeof(T2) },
                args => callback(ConvertTo<T1>(args[0]), ConvertTo<T2>(args[1])), callback);

        public void On<T1, T2, T3>(string target, Action<T1, T2, T3> callback)
            => Subscribe(target, new[] { typeof(T1), typeof(T2), typeof(T3) },
                args => callback(ConvertTo<T1>(args[0]), ConvertTo<T2>(args[1]), ConvertTo<T3>(args[2])), callback);

        public void On<T1, T2, T3, T4>(string target, Action<T1, T2, T3, T4> callback)
            => Subscribe(target, new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4) },
                args => callback(ConvertTo<T1>(args[0]), ConvertTo<T2>(args[1]), ConvertTo<T3>(args[2]), ConvertTo<T4>(args[3])), callback);

        public void On<TResult>(string target, Func<TResult> callback)
            => SubscribeFunc(target, typeof(TResult), null, _ => callback(), callback);

        public void On<T1, TResult>(string target, Func<T1, TResult> callback)
            => SubscribeFunc(target, typeof(TResult), new[] { typeof(T1) },
                args => callback(ConvertTo<T1>(args[0])), callback);

        public void On<T1, T2, TResult>(string target, Func<T1, T2, TResult> callback)
            => SubscribeFunc(target, typeof(TResult), new[] { typeof(T1), typeof(T2) },
                args => callback(ConvertTo<T1>(args[0]), ConvertTo<T2>(args[1])), callback);

        /// <summary>Removes ALL subscriptions for the given hub method name.</summary>
        public void Off(string target) => _subscriptions.Remove(target);

        /// <summary>Removes a specific callback registered with <see cref="On(string,Action)"/>.</summary>
        public void Off(string target, Action callback)                                    => RemoveHandler(target, callback);
        /// <summary>Removes a specific callback registered with <see cref="On{T1}"/>.</summary>
        public void Off<T1>(string target, Action<T1> callback)                            => RemoveHandler(target, callback);
        /// <summary>Removes a specific callback registered with <see cref="On{T1,T2}"/>.</summary>
        public void Off<T1, T2>(string target, Action<T1, T2> callback)                    => RemoveHandler(target, callback);
        /// <summary>Removes a specific callback registered with <see cref="On{T1,T2,T3}"/>.</summary>
        public void Off<T1, T2, T3>(string target, Action<T1, T2, T3> callback)            => RemoveHandler(target, callback);
        /// <summary>Removes a specific callback registered with <see cref="On{T1,T2,T3,T4}"/>.</summary>
        public void Off<T1, T2, T3, T4>(string target, Action<T1, T2, T3, T4> callback)   => RemoveHandler(target, callback);
        /// <summary>Removes a specific Func handler registered with <see cref="On{TResult}"/>.</summary>
        public void Off<TResult>(string target, Func<TResult> callback)                    => RemoveHandler(target, callback);
        /// <summary>Removes a specific Func handler registered with <see cref="On{T1,TResult}"/>.</summary>
        public void Off<T1, TResult>(string target, Func<T1, TResult> callback)            => RemoveHandler(target, callback);
        /// <summary>Removes a specific Func handler registered with <see cref="On{T1,T2,TResult}"/>.</summary>
        public void Off<T1, T2, TResult>(string target, Func<T1, T2, TResult> callback)   => RemoveHandler(target, callback);

        // ═════════════════════════════════════════════════════════════════════
        // Internal connection flow
        // ═════════════════════════════════════════════════════════════════════

        private void ConnectInternal()
        {
            if (Options.SkipNegotiation)
            {
                ConnectWebSocket(null);
                return;
            }
            _connectCoroutine = SignalRLiteRunner.Instance.StartCoroutine(
                NegotiationResult.Negotiate(Uri, Options.AuthenticationProvider, OnNegotiationComplete));
        }

        private void OnNegotiationComplete(NegotiationResult result)
        {
            _connectCoroutine = null;
            if (!_reconnectEnabled) return;

            if (!string.IsNullOrEmpty(result.Error))
            {
                HandleFatalError("Negotiation failed: " + result.Error);
                return;
            }

            if (!string.IsNullOrEmpty(result.RedirectUrl))
            {
                if (++_redirectCount > Options.MaxRedirects)
                {
                    HandleFatalError($"Exceeded maximum negotiation redirects ({Options.MaxRedirects}).");
                    return;
                }
                Uri = new Uri(result.RedirectUrl);
                if (!string.IsNullOrEmpty(result.AccessToken))
                    Options.AccessToken = result.AccessToken;
                ConnectInternal();
                return;
            }

            NegotiationResult = result;
            ConnectWebSocket(result);
        }

        private void ConnectWebSocket(NegotiationResult negotiation)
        {
            string wsUrl = BuildWebSocketUrl(Uri, negotiation);

            // Inject type-lookup callbacks into the protocol for binary deserialization.
            Options.Protocol.GetArgTypes = target =>
            {
                if (!_subscriptions.TryGetValue(target, out var sub)) return null;
                if (sub.Handlers.Count     > 0) return sub.Handlers[0].ParamTypes;
                if (sub.FuncHandlers.Count > 0) return sub.FuncHandlers[0].ParamTypes;
                return null;
            };
            Options.Protocol.GetReturnType = invId =>
            {
                if (long.TryParse(invId, out long id) && _invocations.TryGetValue(id, out var def))
                    return def.ReturnType;
                return null;
            };

            _transport = new WebSocketTransport(Options.WebSocketFactory);
            _transport.OnConnected    += HandleTransportConnected;
            _transport.OnDisconnected += HandleTransportDisconnected;
            _transport.OnMessages     += HandleMessages;
            _transport.OnError        += HandleTransportError;
            _transport.Connect(wsUrl, Options.Protocol);
        }

        private string BuildWebSocketUrl(Uri uri, NegotiationResult negotiation)
        {
            var ub    = new UriBuilder(uri);
            ub.Scheme = uri.Scheme == "https" ? "wss" :
                        uri.Scheme == "http"  ? "ws"  : uri.Scheme;

            // Append the connection id token required by the SignalR protocol.
            var query = new System.Text.StringBuilder(
                (string.IsNullOrEmpty(ub.Query) || ub.Query == "?") ? "" : ub.Query.TrimStart('?'));

            if (negotiation != null && !string.IsNullOrEmpty(negotiation.IdToken))
            {
                if (query.Length > 0) query.Append('&');
                query.Append("id=").Append(Uri.EscapeDataString(negotiation.IdToken));
            }

            if (query.Length > 0) ub.Query = query.ToString();

            // Let the authentication provider append its own query params (e.g. access_token).
            Uri wsUri = ub.Uri;
            if (Options.AuthenticationProvider != null)
                wsUri = Options.AuthenticationProvider.PrepareUri(wsUri);

            return wsUri.ToString();
        }

        // ── Transport callbacks ──────────────────────────────────────────────

        private void HandleTransportConnected()
        {
            bool wasReconnecting = State == HubConnectionState.Reconnecting;
            SetState(HubConnectionState.Connected);
            _reconnectAttempts = 0;
            _lastPingTime      = Time.realtimeSinceStartup;
            _lastMessageTime   = Time.realtimeSinceStartup;
            SignalRLiteRunner.Instance.RegisterUpdate(OnUpdate);

            if (wasReconnecting) SafeInvoke(() => OnReconnected?.Invoke(this));
            else                 SafeInvoke(() => OnConnected?.Invoke(this));
        }

        private void HandleTransportDisconnected(string reason)
        {
            SignalRLiteRunner.Instance.UnregisterUpdate(OnUpdate);
            TryReconnectOrClose(reason);
        }

        private void HandleTransportError(string error)
        {
            SignalRLiteRunner.Instance.UnregisterUpdate(OnUpdate);
            TryReconnectOrClose(error);
        }

        private void HandleMessages(List<SignalRMessage> messages)
        {
            _lastMessageTime = Time.realtimeSinceStartup;

            foreach (var msg in messages)
            {
                if (OnMessage != null)
                {
                    try { if (!OnMessage(this, msg)) continue; }
                    catch (Exception ex) { Debug.LogError($"[SignalRLite] OnMessage interceptor error: {ex}"); }
                }
                ProcessMessage(msg);
            }
        }

        // ── Message dispatch ─────────────────────────────────────────────────

        private void ProcessMessage(SignalRMessage msg)
        {
            switch (msg.Type)
            {
                case MessageType.Ping:
                    SendMessage(new SignalRMessage { Type = MessageType.Ping });
                    break;

                case MessageType.Invocation:
                    DispatchInvocation(msg);
                    break;

                case MessageType.Completion:
                    DispatchCompletion(msg);
                    break;

                case MessageType.Close:
                    _reconnectEnabled = _reconnectEnabled && msg.AllowReconnect;
                    _transport?.Close();
                    break;
            }
        }

        private void DispatchInvocation(SignalRMessage msg)
        {
            if (msg.Target == null || !_subscriptions.TryGetValue(msg.Target, out var sub)) return;

            object[] args = msg.Arguments ?? Array.Empty<object>();

            foreach (var handler in sub.Handlers)
                SafeInvoke(() => handler.Wrapper(args));

            foreach (var funcHandler in sub.FuncHandlers)
            {
                try
                {
                    object result = funcHandler.Wrapper(args);
                    if (msg.InvocationId != null)
                        SendMessage(new SignalRMessage
                        {
                            Type         = MessageType.Completion,
                            InvocationId = msg.InvocationId,
                            Result       = result,
                        });
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SignalRLite] Func handler error: {ex}");
                    if (msg.InvocationId != null)
                        SendMessage(new SignalRMessage
                        {
                            Type         = MessageType.Completion,
                            InvocationId = msg.InvocationId,
                            Error        = ex.Message,
                        });
                }
            }
        }

        private void DispatchCompletion(SignalRMessage msg)
        {
            if (msg.InvocationId == null) return;
            if (!long.TryParse(msg.InvocationId, out long id)) return;
            if (!_invocations.TryGetValue(id, out var def)) return;
            _invocations.Remove(id);
            SafeInvoke(() => def.Callback(msg));
        }

        // ── Unified send via protocol ────────────────────────────────────────

        private void SendMessage(SignalRMessage msg)
        {
            if (_transport == null) return;
            if (Options.Protocol.IsBinary)
            {
                byte[] data = Options.Protocol.EncodeBytes(msg);
                if (data != null) _transport.SendBytes(data);
            }
            else
            {
                string text = Options.Protocol.EncodeText(msg);
                if (text != null) _transport.Send(text);
            }
        }

        // ── Reconnect logic ──────────────────────────────────────────────────

        private void TryReconnectOrClose(string reason)
        {
            FailPendingInvocations(reason ?? "Disconnected.");

            var delay = GetReconnectDelay();
            if (_reconnectEnabled && delay.HasValue)
            {
                SetState(HubConnectionState.Reconnecting);
                SafeInvoke(() => OnReconnecting?.Invoke(this));
                DetachTransport();
                _transport?.Dispose();
                _transport          = null;
                _reconnectCoroutine = SignalRLiteRunner.Instance.StartCoroutine(ReconnectAfter(delay.Value));
            }
            else
            {
                DetachTransport();
                _transport?.Dispose();
                _transport = null;
                SetState(HubConnectionState.Disconnected);
                SafeInvoke(() => OnDisconnected?.Invoke(this, reason));
            }
        }

        private TimeSpan? GetReconnectDelay()
        {
            var delays = Options.ReconnectDelays;
            if (delays == null || _reconnectAttempts >= (uint)delays.Length) return null;
            return delays[_reconnectAttempts++];
        }

        private IEnumerator ReconnectAfter(TimeSpan delay)
        {
            if (delay > TimeSpan.Zero)
                yield return new WaitForSeconds((float)delay.TotalSeconds);
            _reconnectCoroutine = null;
            ConnectInternal();
        }

        // ── Per-frame ping / timeout ─────────────────────────────────────────

        private void OnUpdate()
        {
            if (State != HubConnectionState.Connected) return;

            float now = Time.realtimeSinceStartup;

            if (now - _lastPingTime >= (float)Options.PingInterval.TotalSeconds)
            {
                _lastPingTime = now;
                SendMessage(new SignalRMessage { Type = MessageType.Ping });
            }

            if (now - _lastMessageTime >= (float)Options.PingTimeout.TotalSeconds)
            {
                SignalRLiteRunner.Instance.UnregisterUpdate(OnUpdate);
                TryReconnectOrClose("Ping timeout");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void SetState(HubConnectionState next) => State = next;

        private void HandleFatalError(string error)
        {
            SetState(HubConnectionState.Disconnected);
            if (OnError != null) SafeInvoke(() => OnError.Invoke(this, error));
            else                 Debug.LogError($"[SignalRLite] {error}");
        }

        private void DetachTransport()
        {
            if (_transport == null) return;
            _transport.OnConnected    -= HandleTransportConnected;
            _transport.OnDisconnected -= HandleTransportDisconnected;
            _transport.OnMessages     -= HandleMessages;
            _transport.OnError        -= HandleTransportError;
        }

        private void Subscribe(string target, Type[] paramTypes, Action<object[]> wrapper, Delegate original)
        {
            if (!_subscriptions.TryGetValue(target, out var sub))
                _subscriptions[target] = sub = new Subscription();
            sub.Handlers.Add(new HandlerEntry { ParamTypes = paramTypes, Wrapper = wrapper, Original = original });
        }

        private void SubscribeFunc(string target, Type returnType, Type[] paramTypes,
                                   Func<object[], object> wrapper, Delegate original)
        {
            if (!_subscriptions.TryGetValue(target, out var sub))
                _subscriptions[target] = sub = new Subscription();
            sub.FuncHandlers.Add(new FuncHandlerEntry
                { ReturnType = returnType, ParamTypes = paramTypes, Wrapper = wrapper, Original = original });
        }

        private void RemoveHandler(string target, Delegate original)
        {
            if (!_subscriptions.TryGetValue(target, out var sub)) return;
            sub.Handlers.RemoveAll(h => h.Original == original);
            sub.FuncHandlers.RemoveAll(h => h.Original == original);
            // Clean up the key if nothing remains.
            if (sub.Handlers.Count == 0 && sub.FuncHandlers.Count == 0)
                _subscriptions.Remove(target);
        }

        private void EnsureConnected()
        {
            if (State != HubConnectionState.Connected)
                Debug.LogWarning($"[SignalRLite] Send/Invoke called when not connected (state={State}).");
        }

        private void FailPendingInvocations(string reason)
        {
            if (_invocations.Count == 0) return;
            var pending = new List<InvocationDef>(_invocations.Values);
            _invocations.Clear();
            var errorMsg = new SignalRMessage { Type = MessageType.Completion, Error = reason };
            foreach (var def in pending)
                SafeInvoke(() => def.Callback(errorMsg));
        }

        private static void SafeInvoke(Action action)
        {
            try { action(); }
            catch (Exception ex) { Debug.LogError($"[SignalRLite] User callback error: {ex}"); }
        }

        // ── Type conversion ──────────────────────────────────────────────────

        /// <summary>
        /// Converts a raw protocol-parsed value to <typeparamref name="T"/>.
        /// Uses <see cref="JsonProtocol.DefaultConvertTo"/> (SimpleJson + JsonUtility).
        /// For protocol-specific conversion (e.g. MessagePack), the protocol already
        /// deserialises to the correct type before this is called.
        /// </summary>
        public static T ConvertTo<T>(object value)
        {
            if (value == null) return default;
            if (value is T t)  return t;

            var targetType = typeof(T);
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
                targetType = Nullable.GetUnderlyingType(targetType);

            object converted = JsonProtocol.DefaultConvertTo(targetType, value);
            return converted != null ? (T)converted : default;
        }
    }
}
