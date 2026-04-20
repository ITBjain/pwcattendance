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
            // 🔥 FIXED: Now reading from your new "CloudflareR2" JSON keys
            var serviceUrl = config["CloudflareR2:ServiceUrl"];
            var accessKey = config["CloudflareR2:AccessKey"];
            var secretKey = config["CloudflareR2:SecretKey"];
            
            _bucketName = config["CloudflareR2:BucketName"] ?? "pegasus-resources";
            _publicDomain = config["CloudflareR2:PublicBaseUrl"] ?? "";

            var s3Config = new AmazonS3Config
            {
                ServiceURL = serviceUrl,
                AuthenticationRegion = "auto" 
            };

            _s3Client = new AmazonS3Client(accessKey, secretKey, s3Config);
        }

        // 🔥 FIXED: Removed the extra 'f' in the name
        // 🔥 FIXED: Added '?' to Task<string?> to safely allow returning null
        public async Task<string?> UploadBase64ImageAsync(string base64Image, string prefix)
        {
            if (string.IsNullOrEmpty(base64Image) || base64Image == "string") 
            {
                return null; 
            }

            // 1. Strip the prefix added by the Android app
            var cleanBase64 = Regex.Replace(base64Image, @"^data:image\/[a-zA-Z]+;?base64,", string.Empty);
            
            // 2. Strip any hidden newlines or spaces
            cleanBase64 = cleanBase64.Replace("\n", "").Replace("\r", "").Replace(" ", "");

            // 3. Convert to binary byte array
            byte[] imageBytes;
            try 
            {
                imageBytes = Convert.FromBase64String(cleanBase64);
            }
            catch (FormatException)
            {
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