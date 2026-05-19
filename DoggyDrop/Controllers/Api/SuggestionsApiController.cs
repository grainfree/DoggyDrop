using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DoggyDrop.Controllers.Api
{
    [ApiController]
    [Route("api/suggestions")]
    public class SuggestionsApiController : ControllerBase
    {
        [HttpGet("walks")]
        public IActionResult Walks(string? dogName = null)
        {
            var bestFor = string.IsNullOrWhiteSpace(dogName) ? "sproscen sprehod" : dogName.Trim();
            IReadOnlyList<WalkSuggestionItem> suggestions =
            [
                new WalkSuggestionItem
                {
                    Title = "Mestni krog z vodo",
                    Area = "Maribor center",
                    Description = "Kratek mestni krog mimo parka, pitnika in dog-friendly postanka.",
                    DistanceKm = 2.4,
                    Difficulty = "Lahko",
                    BestFor = bestFor
                },
                new WalkSuggestionItem
                {
                    Title = "Park + social sniff",
                    Area = "Tivoli / park",
                    Description = "Primeren za miren sprehod, srecanja z drugimi psi in pocasnejse raziskovanje.",
                    DistanceKm = 3.2,
                    Difficulty = "Srednje",
                    BestFor = bestFor
                },
                new WalkSuggestionItem
                {
                    Title = "Trail master mini",
                    Area = "Rob mesta",
                    Description = "Daljsi sprehod za aktivne pse, z vec prostora in manj mestnega hrupa.",
                    DistanceKm = 5.8,
                    Difficulty = "Aktivno",
                    BestFor = bestFor
                }
            ];

            return Ok(suggestions);
        }
    }
}
