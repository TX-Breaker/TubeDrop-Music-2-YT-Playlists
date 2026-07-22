using System.Security.Cryptography;
using System.Text;

namespace TubeDrop.InnerTube.Auth;

/// <summary>
/// Computes the <c>Authorization: SAPISIDHASH</c> header YouTube requires for
/// cookie-authenticated InnerTube calls (§4):
/// <c>SAPISIDHASH {ts}_{SHA1("{ts} {SAPISID} {origin}")}</c>.
/// </summary>
public static class SapisidHash
{
    public static string ComputeAuthorizationHeader(string sapisid, string origin, long unixTimestampSeconds)
    {
        var payload = $"{unixTimestampSeconds} {sapisid} {origin}";
        var digest = SHA1.HashData(Encoding.UTF8.GetBytes(payload));
        return $"SAPISIDHASH {unixTimestampSeconds}_{Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    public static string ComputeAuthorizationHeader(string sapisid, string origin, TimeProvider timeProvider) =>
        ComputeAuthorizationHeader(sapisid, origin, timeProvider.GetUtcNow().ToUnixTimeSeconds());
}
