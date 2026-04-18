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
            if (string.IsNullOrEmpty(base64Image)) return null;

            // 1. Strip the prefix added by the Android app (data:image/jpeg:base64,)
            var cleanBase64 = Regex.Replace(base64Image, @"^data:image\/[a-zA-Z]+;?base64,", string.Empty);
            
            // 2. Convert to binary byte array
            byte[] imageBytes = Convert.FromBase64String(cleanBase64);

            // 3. Generate a unique filename
            string fileName = $"{prefix}_{Guid.NewGuid()}.jpg";

            // 4. Upload to R2
            using (var stream = new MemoryStream(imageBytes))
            {
                var putRequest = new PutObjectRequest
                {
                    BucketName = _bucketName,
                    Key = fileName,
                    InputStream = stream,
                    ContentType = "image/jpeg",
                    DisablePayloadSigning = true // Recommended for R2 performance
                };

                await _s3Client.PutObjectAsync(putRequest);
            }

            // 5. Return the public URL to save in MySQL
            return $"{_publicDomain}/{fileName}";
        }
    }
}