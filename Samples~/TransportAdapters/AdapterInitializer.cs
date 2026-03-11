#if SIGNALRLITE_UNITYWSSOCKET

using UnityEngine;
using SignalRLite;

namespace SignalRLite.Adapters
{
    /// <summary>
    /// Automatically registers <see cref="UnityWebSocketAdapter"/> as the default
    /// WebSocket factory when the application starts in Play Mode.
    /// No user code required — just define <c>SIGNALRLITE_UNITYWSSOCKET</c>.
    /// </summary>
    internal static class AdapterInitializer
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            SignalRLiteConfig.DefaultWebSocketFactory = url => new UnityWebSocketAdapter(url);
        }
    }
}

#endif
