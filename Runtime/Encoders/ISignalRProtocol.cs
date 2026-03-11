// Wire protocol contract for SignalRLite.
//
// The default implementation is JsonProtocol (no extra dependencies).
// Binary protocols are available via scripting define symbols:
//   SIGNALRLITE_MESSAGEPACK_CSHARP          → MessagePackCSharpProtocol
//   SIGNALRLITE_GAMEDEVWARE_MESSAGEPACK     → MessagePackProtocol

using System;
using System.Collections.Generic;
using SignalRLite.Messages;

namespace SignalRLite.Encoders
{
    /// <summary>
    /// Contract for a SignalR wire protocol.
    /// Implement this interface to add custom protocol support.
    /// </summary>
    public interface ISignalRProtocol
    {
        /// <summary>Protocol name sent in the handshake: "json" or "messagepack".</summary>
        string Name { get; }

        /// <summary>True for binary (MessagePack) protocols, false for text (JSON).</summary>
        bool IsBinary { get; }

        /// <summary>
        /// The JSON text frame sent as the SignalR handshake request.
        /// Always text, even for binary protocols.
        /// </summary>
        string HandshakeRequest { get; }

        /// <summary>
        /// Injected by <see cref="SignalRLite.HubConnection"/> before connecting.
        /// Resolves argument types for a hub method target name.
        /// Used by binary protocols for type-aware deserialization.
        /// </summary>
        Func<string, Type[]> GetArgTypes { set; }

        /// <summary>
        /// Injected by <see cref="SignalRLite.HubConnection"/> before connecting.
        /// Resolves the return type for an invocation-id string.
        /// Used by binary protocols for type-aware deserialization.
        /// </summary>
        Func<string, Type> GetReturnType { set; }

        /// <summary>
        /// Encodes a <see cref="SignalRMessage"/> as a UTF-8 text frame.
        /// Returns <c>null</c> for binary-only protocols.
        /// </summary>
        string EncodeText(SignalRMessage msg);

        /// <summary>
        /// Encodes a <see cref="SignalRMessage"/> as a binary frame.
        /// Returns <c>null</c> for text-only protocols.
        /// </summary>
        byte[] EncodeBytes(SignalRMessage msg);

        /// <summary>Parses one or more messages from a text (JSON) frame.</summary>
        List<SignalRMessage> ParseText(string text);

        /// <summary>Parses one or more messages from a binary (MessagePack) frame.</summary>
        List<SignalRMessage> ParseBytes(byte[] data, int offset, int length);

        /// <summary>
        /// Converts a raw parsed value (e.g. boxed long/string/Dictionary from SimpleJson)
        /// to the requested <paramref name="toType"/>.
        /// </summary>
        object ConvertTo(Type toType, object obj);
    }
}
