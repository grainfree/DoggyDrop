using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace DoggyDrop.Services
{
    public interface ICloudinaryService
    {
        Task<string> UploadImageAsync(IFormFile file);           // za profilne slike
        Task<string> UploadTrashBinImageAsync(IFormFile file);   // za slike košev
    }
}
