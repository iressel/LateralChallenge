using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CmsSync.Infrastructure.Authentication;

public static class FixedTimeCredentialVerifier
{
    private const byte InvalidRepresentationMarker = 0;
    private const byte ValidRepresentationMarker = 1;
    private const byte InvalidConfiguredRepresentationMarker = 2;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static bool Verify(
        string suppliedUsername,
        string suppliedPassword,
        CredentialIdentityOptions configuredIdentity)
    {
        ArgumentNullException.ThrowIfNull(suppliedUsername);
        ArgumentNullException.ThrowIfNull(suppliedPassword);
        ArgumentNullException.ThrowIfNull(configuredIdentity);

        var suppliedRepresentation = CreateCredentialRepresentation(
            suppliedUsername,
            suppliedPassword,
            InvalidRepresentationMarker);
        var configuredRepresentation = CreateCredentialRepresentation(
            configuredIdentity.Username,
            configuredIdentity.Password,
            InvalidConfiguredRepresentationMarker);
        var suppliedDigest = GC.AllocateUninitializedArray<byte>(SHA256.HashSizeInBytes);
        var configuredDigest = GC.AllocateUninitializedArray<byte>(SHA256.HashSizeInBytes);

        try
        {
            SHA256.HashData(suppliedRepresentation, suppliedDigest);
            SHA256.HashData(configuredRepresentation, configuredDigest);
            return CryptographicOperations.FixedTimeEquals(suppliedDigest, configuredDigest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(suppliedRepresentation);
            CryptographicOperations.ZeroMemory(configuredRepresentation);
            CryptographicOperations.ZeroMemory(suppliedDigest);
            CryptographicOperations.ZeroMemory(configuredDigest);
        }
    }

    private static byte[] CreateCredentialRepresentation(
        string? username,
        string? password,
        byte invalidMarker)
    {
        if (username is null ||
            password is null ||
            username.Length > AuthenticationConstants.MaximumUsernameLength ||
            password.Length > AuthenticationConstants.MaximumSuppliedPasswordLength)
        {
            return CreateInvalidRepresentation(invalidMarker);
        }

        try
        {
            var usernameByteCount = StrictUtf8.GetByteCount(username);
            var passwordByteCount = StrictUtf8.GetByteCount(password);

            if (usernameByteCount > AuthenticationConstants.MaximumUsernameByteLength ||
                passwordByteCount > AuthenticationConstants.MaximumSuppliedPasswordByteLength)
            {
                return CreateInvalidRepresentation(invalidMarker);
            }

            var representation = GC.AllocateUninitializedArray<byte>(
                1 + sizeof(int) + usernameByteCount + sizeof(int) + passwordByteCount);
            var offset = 0;
            representation[offset] = ValidRepresentationMarker;
            offset++;

            BinaryPrimitives.WriteInt32BigEndian(
                representation.AsSpan(offset, sizeof(int)),
                usernameByteCount);
            offset += sizeof(int);
            offset += StrictUtf8.GetBytes(username, representation.AsSpan(offset, usernameByteCount));

            BinaryPrimitives.WriteInt32BigEndian(
                representation.AsSpan(offset, sizeof(int)),
                passwordByteCount);
            offset += sizeof(int);
            _ = StrictUtf8.GetBytes(password, representation.AsSpan(offset, passwordByteCount));

            return representation;
        }
        catch (EncoderFallbackException)
        {
            return CreateInvalidRepresentation(invalidMarker);
        }
    }

    private static byte[] CreateInvalidRepresentation(byte marker)
    {
        return [marker];
    }
}
