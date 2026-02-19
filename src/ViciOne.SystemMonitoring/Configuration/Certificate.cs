using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ViciOne.SystemMonitoring.Configuration;

internal static class Certificate
{
    internal static X509Certificate2 FromPFX(string certificateFile, string certificateFilePassword)
    {
        if (File.Exists(certificateFile))
            return X509CertificateLoader.LoadPkcs12FromFile(certificateFile, certificateFilePassword, X509KeyStorageFlags.EphemeralKeySet);
        else
            return X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(certificateFile), certificateFilePassword, X509KeyStorageFlags.EphemeralKeySet);
    }

    internal static X509Certificate2 FromPEM(string certificateFile, string certificatePrivateKeyFile)
    {
        X509Certificate2 intermediateCertificate;

        if (File.Exists(certificateFile) && File.Exists(certificatePrivateKeyFile))
            intermediateCertificate = X509Certificate2.CreateFromPemFile(certificateFile, certificatePrivateKeyFile);
        else
            intermediateCertificate = X509Certificate2.CreateFromPem(FromBase64String(certificateFile), FromBase64String(certificatePrivateKeyFile));

        // https://github.com/dotnet/runtime/issues/45680#issuecomment-739912495
        var certificate = X509CertificateLoader.LoadPkcs12(intermediateCertificate.Export(X509ContentType.Pkcs12), default);

        intermediateCertificate.Dispose();

        return certificate;
    }

    internal static string FromBase64String(string base64String)
        => Encoding.UTF8.GetString(Convert.FromBase64String(base64String));
}
