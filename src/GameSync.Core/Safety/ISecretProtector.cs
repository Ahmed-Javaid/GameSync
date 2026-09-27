namespace GameSync.Core.Safety;

/// <summary>R10: encrypts a secret so only this Windows user can read it back (DPAPI on Windows).</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plain);

    /// <summary>Throws <see cref="System.Security.Cryptography.CryptographicException"/> when this user can't read it.</summary>
    byte[] Unprotect(byte[] protectedBytes);
}
