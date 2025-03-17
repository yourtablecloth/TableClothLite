namespace TableClothLite.Models;

public sealed record class CertPairModel(
    string Path, byte[] PublicKey, byte[] PrivateKey);
