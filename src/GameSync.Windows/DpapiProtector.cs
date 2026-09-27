using System.Security.Cryptography;
using GameSync.Core.Safety;

namespace GameSync.Windows;

/// <summary>R10: DPAPI for the current Windows user. Another user, or another PC, can't decrypt what this writes.</summary>
public sealed class DpapiProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "GameSync.secrets.v1"u8.ToArray();

    public byte[] Protect(byte[] plain) => ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] protectedBytes) => ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
}
