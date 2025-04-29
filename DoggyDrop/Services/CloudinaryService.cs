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
            {
                Console.WriteLine("⚠️ Napaka: datoteka je prazna ali ni bila poslana (profilna slika).");
                return null;
            }

            await using var stream = file.OpenReadStream();

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = "doggydrop-profile-images" // 📁 mapa za profilne slike
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);

            // 🌩️ Diagnostika rezultata
            Console.WriteLine("🌩️ Rezultat nalaganja (profilna slika):");
            Console.WriteLine($"StatusCode: {uploadResult.StatusCode}");
            Console.WriteLine($"PublicId: {uploadResult.PublicId}");
            Console.WriteLine($"SecureUrl: {uploadResult.SecureUrl}");
            Console.WriteLine($"Error: {uploadResult.Error?.Message}");

            if (uploadResult.StatusCode == System.Net.HttpStatusCode.OK && uploadResult.SecureUrl != null)
            {
                return uploadResult.SecureUrl.ToString();
            }
            else
            {
                Console.WriteLine("❌ Upload profilne slike na Cloudinary NI uspel!");
                return null;
            }
        }

        // ✅ Nalaganje slike koša
        public async Task<string> UploadTrashBinImageAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                Console.WriteLine("⚠️ Napaka: datoteka je prazna ali ni bila poslana.");
                return null;
            }

            await using var stream = file.OpenReadStream();

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream)
                // 🚫 NE dodajaj Folder tukaj
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);

            // Diagnostika rezultata
            Console.WriteLine("🌩️ Rezultat nalaganja (slika koša):");
            Console.WriteLine($"StatusCode: {uploadResult.StatusCode}");
            Console.WriteLine($"PublicId: {uploadResult.PublicId}");
            Console.WriteLine($"SecureUrl: {uploadResult.SecureUrl}");
            Console.WriteLine($"Error: {uploadResult.Error?.Message}");

            if (uploadResult.StatusCode == System.Net.HttpStatusCode.OK && uploadResult.SecureUrl != null)
            {
                return uploadResult.SecureUrl.ToString();
            }
            else
            {
                Console.WriteLine("❌ Upload slike koša na Cloudinary NI uspel!");
                return null;
            }
        }

    }
}
