// Default SignalR JSON text protocol.
// Zero dependencies: uses the bundled SimpleJson parser.
// Optionally plug in a richer JSON encoder via the IEncoder interface:
//   new JsonProtocol(new JsonDotNetEncoder())   ← requires Newtonsoft.Json + SIGNALRLITE_NEWTONSOFT_JSON

using System;
using System.Collections.Generic;
using System.Text;
using SignalRLite.Messages;
using SignalRLite.Utility;
using UnityEngine;

namespace SignalRLite.Encoders
{
    /// <summary>
    /// ASP.NET Core SignalR JSON protocol (text frames separated by 0x1E record separator).
    /// https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/docs/specs/HubProtocol.md
    /// </summary>
    public sealed class JsonProtocol : ISignalRProtocol
    {
        // ── ISignalRProtocol ─────────────────────────────────────────────────

        public string Name      => "json";
        public bool   IsBinary  => false;

        public string HandshakeRequest
            => $"{{\"protocol\":\"json\",\"version\":1}}{Separator}";

        public Func<string, Type[]> GetArgTypes   { set { /* JSON infers types at dispatch time */ } }
        public Func<string, Type>   GetReturnType { set { /* JSON infers types at dispatch time */ } }

        // ── Optional pluggable encoder (e.g. JsonDotNetEncoder) ──────────────

        public IEncoder Encoder { get; }

        public JsonProtocol() { }
        public JsonProtocol(IEncoder encoder) { Encoder = encoder; }

        // ── Constants ────────────────────────────────────────────────────────

        public const char Separator = (char)0x1E;

        // ── EncodeText ───────────────────────────────────────────────────────

        public string EncodeText(SignalRMessage msg)
        {
            switch (msg.Type)
            {
                case MessageType.Invocation:
                    return msg.InvocationId == null
                        ? EncodeSend(msg.Target, msg.Arguments)
                        : EncodeInvocation(msg.InvocationId, msg.Target, msg.Arguments);

                case MessageType.Completion:
                    return EncodeCompletion(msg.InvocationId, msg.Result, msg.Error);

                case MessageType.Ping:
                    return EncodePing();

                case MessageType.StreamItem:
                    return EncodeStreamItem(msg.InvocationId, msg.Item);

                case MessageType.CancelInvocation:
                    return EncodeCancelInvocation(msg.InvocationId);

                case MessageType.Close:
                    return EncodeClose(msg.Error, msg.AllowReconnect);

                default:
                    return null;
            }
        }

        public byte[] EncodeBytes(SignalRMessage msg) => null;

        // ── ParseText ────────────────────────────────────────────────────────

        public List<SignalRMessage> ParseText(string data)
        {
            var messages = new List<SignalRMessage>();
            if (string.IsNullOrEmpty(data)) return messages;

            int start = 0;
            for (int i = 0; i <= data.Length; i++)
            {
                if (i == data.Length || data[i] == Separator)
                {
                    if (i > start)
                    {
                        var msg = ParseOne(data.Substring(start, i - start));
                        if (msg != null) messages.Add(msg);
                    }
                    start = i + 1;
                }
            }
            return messages;
        }

        public List<SignalRMessage> ParseBytes(byte[] data, int offset, int length)
            => new List<SignalRMessage>();

        // ── ConvertTo ────────────────────────────────────────────────────────

        public object ConvertTo(Type toType, object obj)
        {
            if (Encoder != null) return Encoder.ConvertTo(toType, obj);
            return DefaultConvertTo(toType, obj);
        }

        // ── Private encode helpers ───────────────────────────────────────────

        private string EncodePing()
            => $"{{\"type\":6}}{Separator}";

        private string EncodeSend(string target, object[] args)
        {
            var sb = new StringBuilder(128);
            sb.Append("{\"type\":1,\"target\":");
            sb.Append(Stringify(target));
            sb.Append(",\"arguments\":");
            AppendArgs(sb, args);
            sb.Append(",\"nonBlocking\":true}");
            sb.Append(Separator);
            return sb.ToString();
        }

        private string EncodeInvocation(string invocationId, string target, object[] args)
        {
            var sb = new StringBuilder(128);
            sb.Append("{\"type\":1,\"invocationId\":");
            sb.Append(Stringify(invocationId));
            sb.Append(",\"target\":");
            sb.Append(Stringify(target));
            sb.Append(",\"arguments\":");
            AppendArgs(sb, args);
            sb.Append('}');
            sb.Append(Separator);
            return sb.ToString();
        }

        private string EncodeCompletion(string invocationId, object result, string error)
        {
            var sb = new StringBuilder(64);
            sb.Append("{\"type\":3,\"invocationId\":");
            sb.Append(Stringify(invocationId));
            if (!string.IsNullOrEmpty(error))
            {
                sb.Append(",\"error\":");
                sb.Append(Stringify(error));
            }
            else
            {
                sb.Append(",\"result\":");
                sb.Append(Stringify(result));
            }
            sb.Append('}');
            sb.Append(Separator);
            return sb.ToString();
        }

        private string EncodeStreamItem(string invocationId, object item)
        {
            var sb = new StringBuilder(64);
            sb.Append("{\"type\":2,\"invocationId\":");
            sb.Append(Stringify(invocationId));
            sb.Append(",\"item\":");
            sb.Append(Stringify(item));
            sb.Append('}');
            sb.Append(Separator);
            return sb.ToString();
        }

        private string EncodeCancelInvocation(string invocationId)
        {
            var sb = new StringBuilder(64);
            sb.Append("{\"type\":5,\"invocationId\":");
            sb.Append(Stringify(invocationId));
            sb.Append('}');
            sb.Append(Separator);
            return sb.ToString();
        }

        private string EncodeClose(string error, bool allowReconnect)
        {
            if (string.IsNullOrEmpty(error))
                return $"{{\"type\":7}}{Separator}";
            return $"{{\"type\":7,\"error\":{Stringify(error)},\"allowReconnect\":{(allowReconnect ? "true" : "false")}}}{Separator}";
        }

        private void AppendArgs(StringBuilder sb, object[] args)
        {
            sb.Append('[');
            if (args != null)
                for (int i = 0; i < args.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(Stringify(args[i]));
                }
            sb.Append(']');
        }

        private string Stringify(object value)
        {
            if (Encoder != null && value != null && !IsPrimitive(value))
                return Encoder.Encode(value);
            return SimpleJson.Stringify(value);
        }

        private static bool IsPrimitive(object v)
            => v is string || v is bool || v is int || v is long || v is float || v is double || v is null;

        // ── Private parse ────────────────────────────────────────────────────

        private static SignalRMessage ParseOne(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            var obj = SimpleJson.Parse(json) as Dictionary<string, object>;
            if (obj == null) return null;

            var msg = new SignalRMessage();

            if (obj.TryGetValue("type", out var typeVal) && typeVal != null)
            {
                try   { msg.Type = (MessageType)Convert.ToInt32(Convert.ToDouble(typeVal)); }
                catch { msg.Type = MessageType.Invocation; }
            }

            obj.TryGetValue("invocationId", out var invId);
            msg.InvocationId = invId as string;

            obj.TryGetValue("target", out var target);
            msg.Target = target as string;

            if (obj.TryGetValue("arguments", out var args) && args is List<object> argList)
                msg.Arguments = argList.ToArray();

            obj.TryGetValue("item",   out msg.Item);
            obj.TryGetValue("result", out msg.Result);

            obj.TryGetValue("error", out var error);
            msg.Error = error as string;

            if (obj.TryGetValue("allowReconnect", out var ar))
                msg.AllowReconnect = Convert.ToBoolean(ar);

            if (obj.TryGetValue("nonBlocking", out var nb))
                msg.NonBlocking = Convert.ToBoolean(nb);

            if (obj.TryGetValue("sequenceId", out var seqId))
                msg.SequenceId = Convert.ToInt64(seqId);

            return msg;
        }

        // ── Default type converter (SimpleJson / JsonUtility) ────────────────

        public static object DefaultConvertTo(Type targetType, object value)
        {
            if (value == null) return null;
            if (targetType.IsInstanceOfType(value)) return value;

            try
            {
                if (targetType == typeof(string))
                    return value.ToString();

                if (targetType.IsPrimitive || targetType == typeof(decimal))
                    return Convert.ChangeType(value, targetType,
                        System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { return null; }

            try
            {
                string json = SimpleJson.Stringify(value);
                return UnityEngine.JsonUtility.FromJson(json, targetType);
            }
            catch
            {
                Debug.LogWarning($"[SignalRLite] JsonProtocol.ConvertTo({targetType.Name}) failed. " +
                                 "Ensure the type has [Serializable] attribute.");
                return null;
            }
        }
    }
}
