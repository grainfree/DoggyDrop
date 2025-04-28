using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace DoggyDrop.Services
{
    public interface ICloudinaryService
    {
        Task<string> UploadImageAsync(IFormFile file);
        Task<string> UploadTrashBinImageAsync(IFormFile file);
    }
}
