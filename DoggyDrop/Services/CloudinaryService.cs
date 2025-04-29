using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System;

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
            {
                Console.WriteLine("⚠️ Profilna slika: prazna datoteka.");
                return null;
            }

            await using var stream = file.OpenReadStream();

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = "doggydrop-profile-images",
                UseFilename = true,
                UniqueFilename = true,
                Overwrite = false
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);

            Console.WriteLine("🌩️ Rezultat nalaganja (profilna slika):");
            Console.WriteLine($"StatusCode: {uploadResult.StatusCode}");
            Console.WriteLine($"SecureUrl: {uploadResult.SecureUrl}");
            Console.WriteLine($"Error: {uploadResult.Error?.Message}");

            return uploadResult.SecureUrl?.ToString();
        }

        // ✅ Nalaganje slike koša
        public async Task<string> UploadTrashBinImageAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                Console.WriteLine("⚠️ Slika koša: prazna datoteka.");
                return null;
            }

            await using var stream = file.OpenReadStream();

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = "doggydrop-trashbins",
                UseFilename = true,
                UniqueFilename = true,
                Overwrite = false
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);

            Console.WriteLine("🌩️ Rezultat nalaganja (slika koša):");
            Console.WriteLine($"StatusCode: {uploadResult.StatusCode}");
            Console.WriteLine($"SecureUrl: {uploadResult.SecureUrl}");
            Console.WriteLine($"Error: {uploadResult.Error?.Message}");

            return uploadResult.SecureUrl?.ToString();
        }
    }
}
