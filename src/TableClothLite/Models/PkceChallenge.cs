namespace TableClothLite.Models;

public sealed record class PkceChallenge(
    string CodeChallenge, string CodeVerifier);
