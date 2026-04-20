using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PwcApi.Services
{
    public class R2StorageService
    {
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;
        private readonly string _publicDomain;

        public R2StorageService(IConfiguration config)
        {
            var accountId = config["R2Settings:AccountId"];
            var accessKey = config["R2Settings:AccessKey"];
            var secretKey = config["R2Settings:SecretKey"];
            
            _bucketName = config["R2Settings:BucketName"] ?? "pwc-attendance";
            _publicDomain = config["R2Settings:PublicDomain"] ?? "";

            // Point the S3 Client to Cloudflare R2
            var s3Config = new AmazonS3Config
            {
                ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com",
                AuthenticationRegion = "auto" // R2 requires "auto"
            };

            _s3Client = new AmazonS3Client(accessKey, secretKey, s3Config);
        }

       public async Task<string> UploadBase64ImageAsync(string base64Image, string prefix)
        {
            if (string.IsNullOrEmpty(base64Image) || base64Image == "string") 
            {
                // If it's dummy Swagger data or empty, skip the upload safely
                return null; 
            }

            // 1. Strip the prefix added by the Android app (data:image/jpeg:base64,)
            var cleanBase64 = Regex.Replace(base64Image, @"^data:image\/[a-zA-Z]+;?base64,", string.Empty);
            
            // 2. 🔥 BULLETPROOFING: Strip any hidden newlines, carriage returns, or spaces
            cleanBase64 = cleanBase64.Replace("\n", "").Replace("\r", "").Replace(" ", "");

            // 3. Convert to binary byte array (Wrapped in try-catch just in case)
            byte[] imageBytes;
            try 
            {
                imageBytes = Convert.FromBase64String(cleanBase64);
            }
            catch (FormatException)
            {
                // If it STILL fails, don't crash the server, just return null
                Console.WriteLine("CRITICAL: Received invalid Base64 string.");
                return null;
            }

            // 4. Generate a unique filename
            string fileName = $"{prefix}_{Guid.NewGuid()}.jpg";

            // 5. Upload to R2
            using (var stream = new MemoryStream(imageBytes))
            {
                var putRequest = new PutObjectRequest
                {
                    BucketName = _bucketName,
                    Key = fileName,
                    InputStream = stream,
                    ContentType = "image/jpeg",
                    DisablePayloadSigning = true 
                };

                await _s3Client.PutObjectAsync(putRequest);
            }

            // 6. Return the public URL
            var baseUrl = _publicDomain.TrimEnd('/');
            return $"{baseUrl}/{fileName}";
        }
    }
}