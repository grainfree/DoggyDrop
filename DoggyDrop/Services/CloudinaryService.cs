using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace DoggyDrop.Services
{
    public class CloudinaryService : ICloudinaryService
    {
        private readonly Cloudinary _cloudinary;

        public CloudinaryService(Cloudinary cloudinary)
        {
            _cloudinary = cloudinary;
        }

        // ✅ Nalaganje profilne slike
        public async Task<string> UploadImageAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return null;

            await using var stream = file.OpenReadStream();

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = "doggydrop-profile-images" // ✅ mapa za profilne slike
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);

            // 🌩️ Diagnostika za preverjanje
            Console.WriteLine("🌩️ Rezultat nalaganja (profilna slika):");
            Console.WriteLine($"StatusCode: {uploadResult.StatusCode}");
            Console.WriteLine($"PublicId: {uploadResult.PublicId}");
            Console.WriteLine($"SecureUrl: {uploadResult.SecureUrl}");

            if (uploadResult.StatusCode == System.Net.HttpStatusCode.OK)
            {
                return uploadResult.SecureUrl?.ToString();
            }

            Console.WriteLine("❌ Upload profilne slike na Cloudinary NI uspel!");
            return null;
        }

        // ✅ Nalaganje slike koša
        public async Task<string> UploadTrashBinImageAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return null;

            await using var stream = file.OpenReadStream();

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = "doggydrop-trashbins" // ✅ mapa za slike košev
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);

            // 🌩️ Diagnostika za preverjanje
            Console.WriteLine("🌩️ Rezultat nalaganja (slika koša):");
            Console.WriteLine($"StatusCode: {uploadResult.StatusCode}");
            Console.WriteLine($"PublicId: {uploadResult.PublicId}");
            Console.WriteLine($"SecureUrl: {uploadResult.SecureUrl}");

            if (uploadResult.StatusCode == System.Net.HttpStatusCode.OK)
            {
                return uploadResult.SecureUrl?.ToString();
            }

            Console.WriteLine("❌ Upload slike koša na Cloudinary NI uspel!");
            return null;
        }
    }
}
