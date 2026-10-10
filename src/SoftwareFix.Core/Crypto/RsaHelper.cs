using System;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace SoftwareFix.Core.Crypto;

public static class RsaHelper
{
    public static RSA CreateRsaFromJavaPublicKey(string base64PublicKey)
    {
        byte[] keyBytes = Convert.FromBase64String(base64PublicKey);
        RsaKeyParameters pubKey = (RsaKeyParameters)PublicKeyFactory.CreateKey(keyBytes);

        RSAParameters rsaParams = new RSAParameters
        {
            Modulus = pubKey.Modulus.ToByteArrayUnsigned(),
            Exponent = pubKey.Exponent.ToByteArrayUnsigned()
        };

        RSA rsa = RSA.Create();
        rsa.ImportParameters(rsaParams);
        return rsa;
    }

    public static string EncryptWithPublicKey(RSA rsa, string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        byte[] data = Encoding.UTF8.GetBytes(plainText);
        byte[] encrypted = rsa.Encrypt(data, RSAEncryptionPadding.Pkcs1);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>
    /// Réplica exata de WebApiHttpRequest.CreateClientRequestFinger (descompilado):
    /// payload = "{unix_ms}|{valor COMPLETO do header Authorization}|{segmento}interface",
    /// cifrado em RSA/PKCS#1 v1.5 com a chave pública de /common/rsa.jhtml.
    /// Atenção: usa o header Authorization completo (ex.: "Bearer &lt;jwt&gt;"),
    /// exatamente como o original — não apenas o token.
    /// </summary>
    public static string GenerateDeviceFingerprint(RSA rsa, string authorizationHeaderValue, string endpoint)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string cleanEndpoint = endpoint.TrimEnd('/');
        int lastSlash = cleanEndpoint.LastIndexOf('/');
        if (lastSlash >= 0)
        {
            cleanEndpoint = cleanEndpoint[(lastSlash + 1)..];
        }
        int dot = cleanEndpoint.LastIndexOf('.');
        if (dot > 0)
        {
            cleanEndpoint = cleanEndpoint[..dot] + "interface";
        }

        string rawPayload = $"{timestamp}|{authorizationHeaderValue}|{cleanEndpoint}";
        return EncryptWithPublicKey(rsa, rawPayload);
    }
}
