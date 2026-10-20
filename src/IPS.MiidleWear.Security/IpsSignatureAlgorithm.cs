namespace IPS.MiidleWear.Security;

/// <summary>The signature algorithms, named as in JSON Web Algorithms; each hashes with SHA-256.</summary>
public enum IpsSignatureAlgorithm
{
    /// <summary>RSA, RSASSA-PKCS1-v1_5.</summary>
    RS256,

    /// <summary>RSA, RSASSA-PSS.</summary>
    PS256,

    /// <summary>ECDSA, the signature as r‖s (IEEE P1363).</summary>
    ES256
}
