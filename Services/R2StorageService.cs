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
        private readonly string _publicBaseUrl;

        public R2StorageService(IConfiguration config)
        {
            // Read exact keys from your updated appsettings.json
            var serviceUrl = config["CloudflareR2:ServiceUrl"];
            var accessKey = config["CloudflareR2:AccessKey"];
            var secretKey = config["CloudflareR2:SecretKey"];
            
            _bucketName = config["CloudflareR2:BucketName"] ?? "pegasus-resources";
            _publicBaseUrl = config["CloudflareR2:PublicBaseUrl"] ?? "";

            // Point the S3 Client directly to your full Service URL
            var s3Config = new AmazonS3Config
            {
                ServiceURL = serviceUrl,
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
            // TrimEnd('/') ensures we don't accidentally get double slashes like ".dev/pegasus-resources//checkin_..."
            var baseUrl = _publicBaseUrl.TrimEnd('/');
            return $"{baseUrl}/{fileName}";
        }
    }
}