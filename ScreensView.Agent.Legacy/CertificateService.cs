using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ScreensView.Shared;

namespace ScreensView.Agent.Legacy;

internal sealed class CertificateService
{
    public X509Certificate2 GetOrCreateCertificate()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);

        var existing = store.Certificates
            .Find(X509FindType.FindBySubjectDistinguishedName, Constants.CertSubject, false)
            .OfType<X509Certificate2>()
            .FirstOrDefault();

        if (existing != null && existing.NotAfter > DateTime.UtcNow.AddDays(30))
            return new X509Certificate2(existing);

        if (existing != null)
            store.Remove(existing);

        var cert = CreateSelfSignedCertificate();
        store.Add(cert);
        return cert;
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            Constants.CertSubject,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            false));

        var usages = new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, false));

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(10));

        return new X509Certificate2(cert.Export(X509ContentType.Pfx), string.Empty,
            X509KeyStorageFlags.MachineKeySet |
            X509KeyStorageFlags.PersistKeySet |
            X509KeyStorageFlags.Exportable);
    }
}
