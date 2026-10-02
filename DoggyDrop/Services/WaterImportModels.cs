using System.ComponentModel.DataAnnotations;
using DoggyDrop.Models;
using DoggyDrop.ViewModels;
namespace DoggyDrop.Services;

public sealed record WaterImportMapping(int Latitude, int Longitude, int? Name = null, int? Access = null,
    int? Seasonality = null, int? DogAccess = null, int? Potability = null)
{
    public void Validate(int columns) {
        var used = new int?[] { Latitude, Longitude, Name, Access, Seasonality, DogAccess, Potability }.Where(x=>x.HasValue).Select(x=>x!.Value).ToArray();
        if (used.Any(i=>i<0 || i>=columns) || used.Distinct().Count()!=used.Length) throw new BinImportException("Preslikaj različne veljavne stolpce.");
    }
    public static WaterImportMapping Suggest(string[] headers) {
        int Find(string name)=>Array.FindIndex(headers,h=>h.Trim().Equals(name,StringComparison.OrdinalIgnoreCase));
        int? Optional(string name)=>Find(name)<0?null:Find(name);
        return new(Find("Latitude"),Find("Longitude"),Optional("Name"),Optional("Access"),Optional("Seasonality"),Optional("DogAccess"),Optional("Potability"));
    }
}
public sealed record WaterImportCandidate(int? WaterPointId, int? RowNumber, string Name, double Metres);
public sealed record WaterImportRow(int Number, string Name, double? Latitude, double? Longitude,
    WaterAccess Access, WaterSeasonality Seasonality, WaterDogAccess DogAccess, WaterPotability Potability,
    string? Error, ImportRowStatus Status, IReadOnlyList<WaterImportCandidate> Candidates)
{
    public WaterPointInput Input()=>new(){Name=Name,Latitude=Latitude,Longitude=Longitude,Access=Access,
        Seasonality=Seasonality,DogAccess=DogAccess,Potability=Potability,IsApproved=true};
}
public static class WaterImportMappingRules
{
    // Upload confirmation explicitly declares that the source identifies drinking-water infrastructure.
    // An absent evidence column is that declaration; a contradictory mapped value is never discarded.
    public static IReadOnlyList<WaterImportRow> Map(ImportCsv csv, WaterImportMapping m) {
        m.Validate(csv.Headers.Length);
        return csv.Rows.Select((cells,i)=> {
            if(cells.Length!=csv.Headers.Length) return new WaterImportRow(i+2,"",null,null,0,0,0,0,"Število polj se ne ujema.",ImportRowStatus.Invalid,[]);
            string Text(int? index)=>index.HasValue?cells[index.Value].Trim():"";
            T Evidence<T>(int? index,T fallback) where T:struct,Enum => Text(index)==""?fallback:
                Enum.GetNames<T>().Contains(Text(index),StringComparer.OrdinalIgnoreCase) && Enum.TryParse<T>(Text(index),true,out var v)?v:(T)Enum.ToObject(typeof(T),-1);
            var row=new WaterImportRow(i+2,Text(m.Name),BinImportMapping.Coordinate(cells[m.Latitude]),BinImportMapping.Coordinate(cells[m.Longitude]),
                Evidence(m.Access,WaterAccess.Unknown),Evidence(m.Seasonality,WaterSeasonality.Unknown),Evidence(m.DogAccess,WaterDogAccess.Unknown),
                Evidence(m.Potability,WaterPotability.SourceReportedDrinking),null,ImportRowStatus.Ready,[]);
            var errors=Errors(row).ToArray();return errors.Length==0?row:row with{Status=ImportRowStatus.Invalid,Error=string.Join(" ",errors)};
        }).ToArray();
    }
    public static IEnumerable<string> Errors(WaterImportRow row) {
        var input=row.Input(); var errors=new List<ValidationResult>();
        Validator.TryValidateObject(input,new ValidationContext(input),errors,true);
        return errors.Select(e=>e.ErrorMessage!);
    }
}
public sealed record WaterImportPage(string Id,int Version,string FileName,ImportSource Source,DateTimeOffset ExpiresAt,
    string[]? Headers,WaterImportMapping? Mapping,IReadOnlyList<WaterImportRow> Rows,IReadOnlySet<int> Selected,
    int Page,int Total,int Ready,int Duplicates,int Invalid,ImportResult? Result=null);
