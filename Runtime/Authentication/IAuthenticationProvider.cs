using System;
using UnityEngine.Networking;

namespace SignalRLite.Authentication
{
/// <summary>Delegate for successful authentication events.</summary>
public delegate void OnAuthenticationSucceededDelegate(IAuthenticationProvider provider);

    /// <summary>Delegate for failed authentication events.</summary>
    public delegate void OnAuthenticationFailedDelegate(IAuthenticationProvider provider, string reason);

    /// <summary>
    /// Interface for authentication providers.
    /// Implement this interface to add custom authentication logic
    /// (e.g. OAuth2, JWT refresh, cookie-based auth).
    /// </summary>
    public interface IAuthenticationProvider
    {
        /// <summary>
        /// Gets a value indicating whether pre-authentication is required before
        /// any request is made.
        /// <para>
        /// If <c>true</c>, the implementation MUST implement <see cref="StartAuthentication"/>
        /// and <see cref="Cancel"/>, and fire <see cref="OnAuthenticationSucceeded"/> or
        /// <see cref="OnAuthenticationFailed"/> when the pre-auth step completes.
        /// </para>
        /// </summary>
        bool IsPreAuthRequired { get; }

    /// <summary>
    /// Fire this event when pre-authentication succeeds.
    /// Ignored when <see cref="IsPreAuthRequired"/> is <c>false</c>.
    /// </summary>
    event OnAuthenticationSucceededDelegate OnAuthenticationSucceeded;

        /// <summary>
        /// Fire this event when pre-authentication fails.
        /// Ignored when <see cref="IsPreAuthRequired"/> is <c>false</c>.
        /// </summary>
        event OnAuthenticationFailedDelegate OnAuthenticationFailed;

        /// <summary>
        /// Called once, before the SignalR negotiation begins.
        /// Skipped when <see cref="IsPreAuthRequired"/> is <c>false</c>.
        /// </summary>
        void StartAuthentication();

        /// <summary>
        /// Prepares a <see cref="UnityWebRequest"/> by adding authentication information
        /// (e.g. Authorization header) before it is sent.
        /// </summary>
        void PrepareRequest(UnityWebRequest request);

        /// <summary>
        /// Modifies the provided URI if necessary
        /// (e.g. appending an <c>access_token</c> query parameter for WebSocket connections).
        /// </summary>
        /// <returns>The modified URI, or the original if no modification is needed.</returns>
        Uri PrepareUri(Uri uri);

        /// <summary>Cancels any ongoing authentication process.</summary>
        void Cancel();
    }
}
