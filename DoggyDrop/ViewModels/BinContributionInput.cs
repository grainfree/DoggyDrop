using System.ComponentModel.DataAnnotations;
using DoggyDrop.Models;

namespace DoggyDrop.ViewModels;

public sealed class BinContributionInput
{
    public int BinId { get; set; }
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public BinContributionType Type { get; set; }
    public BinIssueReason? Reason { get; set; }
    [StringLength(1000)] public string? Description { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinder(BinderType = typeof(DoggyDrop.Services.BinCoordinateBinder))] public double? Latitude { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinder(BinderType = typeof(DoggyDrop.Services.BinCoordinateBinder))] public double? Longitude { get; set; }
    public int? DuplicateBinId { get; set; }
    public IFormFile? Photo { get; set; }
}
public sealed record BinContributionPage(BinContributionInput Input, int Id, string Name, double Latitude, double Longitude,
    string? Image, string? Source, IReadOnlyList<BinNeighbour> Neighbours);
public sealed record BinNeighbour(int Id, string Name, double Metres);
