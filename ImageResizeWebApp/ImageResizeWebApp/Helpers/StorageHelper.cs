using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
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
            if (string.IsNullOrEmpty(_storageConfig.AccountKey))
            {
                var credential = new Azure.Identity.DefaultAzureCredential();
                containerClient = new BlobContainerClient(containerUri, credential);
                thumbnailContainerClient = new BlobContainerClient(thumbnailContainerUri, credential);
            }
            else
            {
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

            // Create the container URI and service URI
            var containerUri = new Uri($"https://{_storageConfig.AccountName}.blob.core.windows.net/{_storageConfig.ThumbnailContainer}");
            var serviceUri = new Uri($"https://{_storageConfig.AccountName}.blob.core.windows.net");

            BlobContainerClient containerClient;
            BlobServiceClient serviceClient;
            
            if (string.IsNullOrEmpty(_storageConfig.AccountKey))
            {
                var credential = new Azure.Identity.DefaultAzureCredential();
                containerClient = new BlobContainerClient(containerUri, credential);
                serviceClient = new BlobServiceClient(serviceUri, credential);
            }
            else
            {
                var storageCredentials = new StorageSharedKeyCredential(_storageConfig.AccountName, _storageConfig.AccountKey);
                containerClient = new BlobContainerClient(containerUri, storageCredentials);
                serviceClient = new BlobServiceClient(serviceUri, storageCredentials);
            }

            if (await containerClient.ExistsAsync())
            {
                // Set the expiry time for SAS tokens (e.g., 1 hour from now)
                var sasBuilder = new BlobSasBuilder
                {
                    BlobContainerName = _storageConfig.ThumbnailContainer,
                    Resource = "c", // 'c' for container
                    StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5), // Start 5 minutes ago to account for clock skew
                    ExpiresOn = DateTimeOffset.UtcNow.AddHours(1)
                };
                sasBuilder.SetPermissions(BlobContainerSasPermissions.Read);

                await foreach (BlobItem blobItem in containerClient.GetBlobsAsync())
                {
                    // Get a reference to the blob
                    var blobClient = containerClient.GetBlobClient(blobItem.Name);

                    string sasToken;
                    if (string.IsNullOrEmpty(_storageConfig.AccountKey))
                    {
                        // For Managed Identity scenarios, we need to get a user delegation key
                        var userDelegationKey = await serviceClient.GetUserDelegationKeyAsync(
                            DateTimeOffset.UtcNow.AddMinutes(-5),
                            DateTimeOffset.UtcNow.AddHours(1));
                        sasToken = sasBuilder.ToSasQueryParameters(userDelegationKey.Value, _storageConfig.AccountName).ToString();
                    }
                    else
                    {
                        // For storage key scenarios
                        sasToken = sasBuilder.ToSasQueryParameters(
                            new StorageSharedKeyCredential(_storageConfig.AccountName, _storageConfig.AccountKey))
                            .ToString();
                    }

                    // Generate the full URL with SAS token
                    thumbnailUrls.Add(blobClient.Uri + "?" + sasToken);
                }
            }

            return thumbnailUrls;
        }
    }
}
