using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ImageResizeWebApp.Models;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ImageResizeWebApp.Helpers
{
    public static class StorageHelper
    {
        private const int ThumbnailWidth = 100;
        private const int ThumbnailHeight = 100;

        public static bool IsImage(IFormFile file)
        {
            if (file.ContentType.Contains("image"))
            {
                return true;
            }

            string[] formats = new string[] { ".jpg", ".png", ".gif", ".jpeg" };

            return formats.Any(item => file.FileName.EndsWith(item, StringComparison.OrdinalIgnoreCase));
        }

        public static async Task<bool> UploadFileToStorage(Stream fileStream, string fileName,
                                                            AzureStorageConfig _storageConfig)
        {
            // Create the container URI
            var containerUri = new Uri($"https://{_storageConfig.AccountName}.blob.core.windows.net/{_storageConfig.ImageContainer}");
            var thumbnailContainerUri = new Uri($"https://{_storageConfig.AccountName}.blob.core.windows.net/{_storageConfig.ThumbnailContainer}");

            BlobContainerClient containerClient;
            BlobContainerClient thumbnailContainerClient;
            try
            {
                // Try managed identity first
                var credential = new Azure.Identity.DefaultAzureCredential();
                containerClient = new BlobContainerClient(containerUri, credential);
                thumbnailContainerClient = new BlobContainerClient(thumbnailContainerUri, credential);
                // Test the connection
                await containerClient.GetPropertiesAsync();
            }
            catch
            {
                // Fall back to account key if managed identity fails
                if (string.IsNullOrEmpty(_storageConfig.AccountKey))
                    throw new InvalidOperationException("No valid authentication method available. Configure either Managed Identity or Account Key.");

                var storageCredentials = new StorageSharedKeyCredential(_storageConfig.AccountName, _storageConfig.AccountKey);
                containerClient = new BlobContainerClient(containerUri, storageCredentials);
                thumbnailContainerClient = new BlobContainerClient(thumbnailContainerUri, storageCredentials);
            }

            // Create containers if they don't exist
            await containerClient.CreateIfNotExistsAsync();
            if (!string.IsNullOrEmpty(_storageConfig.ThumbnailContainer))
            {
                await thumbnailContainerClient.CreateIfNotExistsAsync();
            }

            // Upload original image
            var blobClient = containerClient.GetBlobClient(fileName);
            await blobClient.UploadAsync(fileStream, overwrite: true);

            // Generate and upload thumbnail if thumbnail container is configured
            if (!string.IsNullOrEmpty(_storageConfig.ThumbnailContainer))
            {
                // Reset stream position for reading
                fileStream.Position = 0;

                // Generate thumbnail
                using (var image = await Image.LoadAsync(fileStream))
                {
                    var thumbnailStream = new MemoryStream();
                    
                    // Clone and resize the image
                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Size = new Size(ThumbnailWidth, ThumbnailHeight),
                        Mode = ResizeMode.Max
                    }));                    // Save to stream as PNG
                    await image.SaveAsPngAsync(thumbnailStream);
                    thumbnailStream.Position = 0;

                    // Upload thumbnail
                    var thumbnailBlobClient = thumbnailContainerClient.GetBlobClient(fileName);
                    await thumbnailBlobClient.UploadAsync(thumbnailStream, overwrite: true);
                }
            }

            return true;
        }

        public static async Task<List<string>> GetThumbNailUrls(AzureStorageConfig _storageConfig)
        {
            List<string> thumbnailUrls = new List<string>();

            // Create the container URI
            var containerUri = new Uri($"https://{_storageConfig.AccountName}.blob.core.windows.net/{_storageConfig.ThumbnailContainer}");

            BlobContainerClient containerClient;
            try
            {
                // Try managed identity first
                containerClient = new BlobContainerClient(containerUri, new Azure.Identity.DefaultAzureCredential());
                // Test the connection
                await containerClient.GetPropertiesAsync();
            }
            catch
            {
                // Fall back to account key if managed identity fails
                if (string.IsNullOrEmpty(_storageConfig.AccountKey))
                    throw new InvalidOperationException("No valid authentication method available. Configure either Managed Identity or Account Key.");

                var storageCredentials = new StorageSharedKeyCredential(_storageConfig.AccountName, _storageConfig.AccountKey);
                containerClient = new BlobContainerClient(containerUri, storageCredentials);
            }
            if (await containerClient.ExistsAsync())
            {
                await foreach (BlobItem blobItem in containerClient.GetBlobsAsync())
                {
                    thumbnailUrls.Add(containerClient.Uri + "/" + blobItem.Name);
                }
            }

            return thumbnailUrls;
        }
    }
}
