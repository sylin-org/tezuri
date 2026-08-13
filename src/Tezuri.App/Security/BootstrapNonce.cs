using System.Security.Cryptography;

namespace Tezuri.Security;

public sealed record BootstrapNonce(string Value)
{
    public const string HeaderName = "X-Tezuri-Nonce";

    public static BootstrapNonce Create() => new(
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_'));
}
