using System.Security.Cryptography;
using System.Text;

namespace WorkTracker.Avalonia.Services.Linux;

/// <summary>
/// The object paths and tokens of the xdg-desktop-portal request/session protocol.
/// <para>
/// A portal call returns immediately with the path of a <c>org.freedesktop.portal.Request</c>
/// object and delivers the real answer later as a <c>Response</c> signal on it. The path is
/// derived from the caller's own bus name and a token the caller picks, which lets the client
/// subscribe to the signal <em>before</em> making the call — otherwise a fast portal could answer
/// before the match rule is in place and the call would hang forever.
/// </para>
/// </summary>
internal static class PortalHandles
{
	internal const string ObjectPath = "/org/freedesktop/portal/desktop";

	/// <summary>
	/// Rewrites a unique bus name (<c>:1.234</c>) into the form the portal uses inside object
	/// paths: the leading colon dropped and the dots replaced, since neither is legal in a path.
	/// </summary>
	internal static string SenderToken(string uniqueName)
	{
		ArgumentException.ThrowIfNullOrEmpty(uniqueName);

		var start = uniqueName[0] == ':' ? 1 : 0;
		var token = new StringBuilder(uniqueName.Length - start);

		for (var i = start; i < uniqueName.Length; i++)
		{
			token.Append(uniqueName[i] == '.' ? '_' : uniqueName[i]);
		}

		return token.ToString();
	}

	/// <summary>Path of the Request object a call with <paramref name="handleToken"/> will answer on.</summary>
	internal static string RequestPath(string uniqueName, string handleToken) =>
		$"{ObjectPath}/request/{SenderToken(uniqueName)}/{handleToken}";

	/// <summary>
	/// A fresh token for a handle. Object paths accept only <c>[A-Za-z0-9_]</c> per element, so the
	/// random bytes are rendered as hex rather than base64, and the prefix keeps the element from
	/// starting with a digit.
	/// </summary>
	internal static string NewToken() =>
		"wt" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
}
