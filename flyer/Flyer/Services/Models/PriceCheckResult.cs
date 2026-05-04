namespace Flyer.Services.Models;

public sealed record PriceCheckResult(
    string FlyerName,
    int FlyerPrice,
    string MasterName,
    int MasterPrice,
    int Difference,
    string Comment);
