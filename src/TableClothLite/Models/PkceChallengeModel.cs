namespace TableClothLite.Models;

public sealed record class PkceChallengeModel(
    string CodeChallenge, string CodeVerifier);
