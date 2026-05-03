namespace PensionCompass.Core.Models;

public sealed record PrincipalGuaranteedProduct(
    string ProductCode,
    string ProductName,
    string AssetManager,
    string AppliedRate,
    string MaturityTerm);
