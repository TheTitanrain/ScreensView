using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ScreensView.Shared;

namespace ScreensView.Agent;

public class CertificateService
{
    private readonly ILogger<CertificateService> _logger;

    public CertificateService(ILogger<CertificateService> logger)
    {
        _logger = logger;
    }

    public X509Certificate2 GetOrCreateCertificate()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);

        var existing = store.Certificates
            .Find(X509FindType.FindBySubjectDistinguishedName, Constants.CertSubject, false)
            .FirstOrDefault();

        if (existing != null && existing.NotAfter > DateTime.UtcNow.AddDays(30))
        {
            _logger.LogInformation("Using existing certificate, expires {Expiry}", existing.NotAfter);
            return existing;
        }

        if (existing != null)
        {
            store.Remove(existing);
            _logger.LogInformation("Removed expired certificate");
        }

        var cert = CreateSelfSignedCertificate();
        store.Add(cert);
        _logger.LogInformation("Created new self-signed certificate, expires {Expiry}", cert.NotAfter);
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

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(10));

        return new X509Certificate2(cert.Export(X509ContentType.Pfx), (string?)null,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
    }
}
