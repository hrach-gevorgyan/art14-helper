using System.Runtime.InteropServices;
using X509 = System.Security.Cryptography.X509Certificates;

namespace Art14Helper.Native;

/// <summary>Checks an executable's Authenticode signature with WinVerifyTrust.</summary>
internal static unsafe partial class Authenticode
{
    /// <summary>
    /// Throws unless <paramref name="path"/> has a valid signature; with
    /// <paramref name="requireHonorSigner"/>, the signer must also be HONOR (or Huawei,
    /// which signed older HONOR drivers).
    /// </summary>
    internal static void Verify(string path, bool requireHonorSigner)
    {
        var problem = !HasValidSignature(path) ? "Windows doesn't trust this installer's signature, so it wasn't saved."
            : requireHonorSigner && !IsHonorSigner(SignerOrganization(path)) ? "This installer isn't signed by HONOR, so it wasn't saved."
            : null;
        if (problem is not null)
            throw new InvalidDataException(problem);
    }

    /// <summary>
    /// The organizations (the certificate's O field, compared without spaces and punctuation)
    /// accepted for HONOR drivers: exactly these, not any subject that merely contains the name.
    /// </summary>
    private static readonly string[] TrustedOrganizations = ["HONORDEVICECOLTD", "HUAWEITECHNOLOGIESCOLTD"];

    internal static bool IsHonorSigner(string? organization)
        => organization is not null
           && TrustedOrganizations.Contains(string.Concat(organization.Where(char.IsLetterOrDigit)).ToUpperInvariant());

    /// <summary>The O (organization) field of the file's signing certificate, or null.</summary>
    private static string? SignerOrganization(string path)
    {
#pragma warning disable SYSLIB0057 // The only API that reads the signer of a signed file; the file is already verified.
        using var certificate = new X509.X509Certificate2(X509.X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
        foreach (var part in certificate.SubjectName.EnumerateRelativeDistinguishedNames())
            if (part.GetSingleElementType().Value == "2.5.4.10")   // organizationName
                return part.GetSingleElementValue();
        return null;
    }

    internal static bool HasValidSignature(string path)
    {
        fixed (char* filePath = path)
        {
            var file = new FileInfo
            {
                Size = (uint)sizeof(FileInfo),
                FilePath = filePath
            };
            var request = new TrustData
            {
                Size = (uint)sizeof(TrustData),
                UiChoice = UiNone,
                RevocationChecks = RevokeNone,
                UnionChoice = ChoiceFile,
                File = &file,
                StateAction = StateIgnore,
                ProviderFlags = RevocationCheckChain
            };
            var action = GenericVerifyV2;
            return WinVerifyTrust(IntPtr.Zero, &action, &request) == 0;
        }
    }

    // WINTRUST_ACTION_GENERIC_VERIFY_V2
    private static readonly Guid GenericVerifyV2 = new(0x00AAC56B, 0xCD44, 0x11D0, 0x8C, 0xC2, 0x00, 0xC0, 0x4F, 0xC2, 0x95, 0xEE);

    private const uint UiNone = 2;               // WTD_UI_NONE
    private const uint RevokeNone = 0;           // WTD_REVOKE_NONE
    private const uint ChoiceFile = 1;           // WTD_CHOICE_FILE
    private const uint StateIgnore = 0;          // WTD_STATEACTION_IGNORE
    private const uint RevocationCheckChain = 0x40; // WTD_REVOCATION_CHECK_CHAIN

    /// <summary>WINTRUST_FILE_INFO</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo
    {
        public uint Size;
        public char* FilePath;
        public IntPtr FileHandle;
        public Guid* KnownSubject;
    }

    /// <summary>WINTRUST_DATA</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size;
        public void* PolicyCallbackData;
        public void* SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public FileInfo* File;
        public uint StateAction;
        public IntPtr StateData;
        public char* UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public void* SignatureSettings;
    }

    [LibraryImport(Libraries.WinTrust)]
    private static partial int WinVerifyTrust(IntPtr window, Guid* action, TrustData* data);
}
