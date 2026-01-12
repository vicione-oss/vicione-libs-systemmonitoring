using System.Text;
using FluentAssertions;
using ViciOne.SystemMonitoring.Configuration;

namespace ViciOne.SystemMonitoring;

public class Certificate_FromBase64String
{
    [Fact]
    public void Acceptance()
    {
        var data = """
            --- BEGIN ---
            some data
            --- END ---
            """;
        var base64Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(data));

        var result = Certificate.FromBase64String(base64Data);

        result.Should().Be(data);
    }
}

public class Certificate_FromPFX
{
    private const string CertificateAsBase64 = "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";

    [Fact]
    public void Creates_certificate_from_file()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        var certificateFile = Path.Combine(path, "cert.pfx");

        try
        {
            File.WriteAllBytes(certificateFile, Convert.FromBase64String(CertificateAsBase64));

            using var result = Certificate.FromPFX(certificateFile, "123");

            result.Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(path, true);
        }
    }

    [Fact]
    public void Creates_certificate_from_Base64()
    {
        using var result = Certificate.FromPFX(CertificateAsBase64, "123");

        result.Should().NotBeNull();
    }
}

public class Certificate_FromPEM
{
    private const string CertificateContent = """
        -----BEGIN CERTIFICATE-----
        MIIDWTCCAkGgAwIBAgIUGSYMXQV05/hoC7ZYObzT5Eb7U9cwDQYJKoZIhvcNAQEL
        BQAwTTFLMEkGA1UECwxCQW1hem9uIFdlYiBTZXJ2aWNlcyBPPUFtYXpvbi5jb20g
        SW5jLiBMPVNlYXR0bGUgU1Q9V2FzaGluZ3RvbiBDPVVTMB4XDTI0MTAyMzA5MTA1
        M1oXDTQ5MTIzMTIzNTk1OVowHjEcMBoGA1UEAwwTQVdTIElvVCBDZXJ0aWZpY2F0
        ZTCCASIwDQYJKoZIhvcNAQEBBQADggEPADCCAQoCggEBAMj5KrQ6pctn3Cpo1RVT
        kEUtQnMh2+upp0AenQASGM2/NP//BIJ1xXX4tAdKatcuczPkiYjQvQoZWq4QuCZ0
        QCvAjXcXjyo0oB21Dh3YMoWmvsN6V9bR9MjmFRETfdEo2XnaKODdC0PfmKTQNKnx
        8Re8/MlvR7JoUjlbDczG7zPi3R91qJV4jj8h+Fvb5Rv5W39DktDhbvt31cbFAvrR
        hzxe4oI4ZWK0Il22+hZvX+3evYK2q73NcO3L79zjoG7rDVZ6Qiy11C6OnS2VUI9L
        1Bm9j1F5Q3ZGmuqAKeo23/1jzoSqKFevBotsR8h/Th/AJYhVFhmTk3xIBRCaUFMl
        At0CAwEAAaNgMF4wHwYDVR0jBBgwFoAUSgpaNwWimE6B3o7a3Bg8kNTZq30wHQYD
        VR0OBBYEFPjOKoK+E4bLu4/NVUQVgYIOS9HXMAwGA1UdEwEB/wQCMAAwDgYDVR0P
        AQH/BAQDAgeAMA0GCSqGSIb3DQEBCwUAA4IBAQBg43JFslQ+X+fWARMYvB5w7PnS
        ybzwJV2DZi3ChmShxbRRKn0MFIJOhOTb2v8C30wMzIGQMFKpDSvCBtkIsnQafJWh
        CfccNvU0BaAOHSVpibrun7Etji4Tq8XPqCB0X5/aRecBD5azMjYrAU7hRCeqCOoV
        KdM8X5/ZJ5IqhKKCr2LUZrbrxCWWUKSwhF+dT6QTaHo/tihT/B3JMYyCd6VqPmKx
        ISRlLtJJ5nGD6YBtIq2O9mgf4K4qtLKyIXNwlIIGxtX06s7043G+0thrRtA2hsbM
        v5R/x50fJ/Isf4ykWPD8cWL0G9745hLuPoejK4Fh1c4NWBAmZt+vFcEK+FSK
        -----END CERTIFICATE-----
        """;
    private const string PrivateKey = """
        -----BEGIN RSA PRIVATE KEY-----
        MIIEpAIBAAKCAQEAyPkqtDqly2fcKmjVFVOQRS1CcyHb66mnQB6dABIYzb80//8E
        gnXFdfi0B0pq1y5zM+SJiNC9ChlarhC4JnRAK8CNdxePKjSgHbUOHdgyhaa+w3pX
        1tH0yOYVERN90SjZedoo4N0LQ9+YpNA0qfHxF7z8yW9HsmhSOVsNzMbvM+LdH3Wo
        lXiOPyH4W9vlG/lbf0OS0OFu+3fVxsUC+tGHPF7igjhlYrQiXbb6Fm9f7d69grar
        vc1w7cvv3OOgbusNVnpCLLXULo6dLZVQj0vUGb2PUXlDdkaa6oAp6jbf/WPOhKoo
        V68Gi2xHyH9OH8AliFUWGZOTfEgFEJpQUyUC3QIDAQABAoIBAE0fwZGUiBd87fKg
        dFVQ0iaZ7cw9DCZt3q8ukFV8nGQoonAzL0nZZEOXg47gsguN2cvqQhVs9X6wqCnw
        ZJrtNcsUg7VWJyytMVz1JEuzsf+0M7TUeG0J+YQ3N5JsBnaD1KF8ZP+WFEKhxfRJ
        qkwler3/Wor612rRuewk0zDojtc75EHcxr3PcdoXBIteFMKEvXHWw6LJt7fIDDfF
        og4XhiE2POybBjS2+GE+weegeXTFmhD6slTxvPGSexyfRAVNm4V04Io2nnhNrzYC
        7xv5oH/tZJ7sm52wHxKtLeXrFYGZw1mRvf88ESGa02Sz+EKd/sAWKa/nzz7oPUKc
        D0xJqokCgYEA5Tds8KAiIorTOxNzS0SYQ16WopONYMiprP2adjR38W9qRUkXDcHG
        7s3FCH++HvaZP+JRPnKC75XW+Bd8Cqu0D/VnCbeTiDnM3MGHhTE5otWYka3vy5/u
        8hjsHCnLYLEW0yHgO83J5HUczWm3A/98UK5LG5kD3sNGn+MIbHiaCMMCgYEA4HTm
        XmXvWjOJCa6UCxYBT5jOG1JDRoNsvztvtRDX9uZrYWc4j/Yh7/4MlJcOExZEBDTW
        gF+Bc8WcM14gGh+fdTcNayhp7dUUFXCH+IhD8TReRa6ZYeiqyw1th+M4yq0cF9Ul
        57lo3XbT1aje/8XvQEJIQWhsDDFtaYvaoS2dC98CgYEAqEMIoJccl7jLGOnNT+p5
        z7Km549jUDHdhJTiB3W6uqwsGdJurvWZwAGjavrDKN2jX+cV9r5+6btYA+TRdM/Y
        rYJa8iIllfHANimG/zpEcglUJ1TE2/h6opOy93Wiy0cUV0bd4WjOlPt3ej7pT3ED
        5AIeNpMb6TH2LTavH1lk0ZsCgYB3tABToZtHiKpxvmf3jZG/aGeqzHFSfq6VQdZu
        QwBaDUKBHLJenE43Fk6RRAnehgZaE++9YLHCE0YdYvpN1EWPFoASgOgVTYi7clsh
        YVKSpXINSXNFcvb6+TtLuBNGi5Gq6CKh9RCG42NP1BKxZ1cT9wVf1Jei3Q1Tt6Eu
        SgwyEwKBgQCnF1VYzptFo0WgkbEvO380u5bEm4/RtkVjaWwu+4QwEBm9mGNDfQud
        G7+oU4+9pOIwc+P1oNpkz27TLDwck37KwxQ6liv+2Nx43HZ5O+fQeYjGe6MbxTxb
        ojbY2AaT5tw2wtq0a6ZVytpd8Jthj0k8oVD2eVsd5Tmk5QxXlubDug==
        -----END RSA PRIVATE KEY-----
        """;

    [Fact]
    public void Creates_certificate_from_file()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        var certificateFile = Path.Combine(path, "cert.pem");
        var privateKeyFile = Path.Combine(path, "key.pem");

        try
        {
            File.WriteAllText(certificateFile, CertificateContent);
            File.WriteAllText(privateKeyFile, PrivateKey);

            using var result = Certificate.FromPEM(certificateFile, privateKeyFile);

            result.Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(path, true);
        }
    }

    [Fact]
    public void Creates_certificate_from_Base64()
    {
        var certificate = Convert.ToBase64String(Encoding.UTF8.GetBytes(CertificateContent));
        var privateKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(PrivateKey));

        using var result = Certificate.FromPEM(certificate, privateKey);

        result.Should().NotBeNull();
    }
}
