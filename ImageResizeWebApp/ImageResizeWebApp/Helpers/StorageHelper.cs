using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ImageResizeWebApp.Models;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ImageResizeWebApp.Helpers
{
    public static class StorageHelper
    {

        public static bool IsImage(IFormFile file)
        {
            if (file.ContentType.Contains("image"))
            {
                return true;
            }

            string[] formats = new string[] { ".jpg", ".png", ".gif", ".jpeg" };

            return formats.Any(item => file.FileName.EndsWith(item, StringComparison.OrdinalIgnoreCase));
        }        public static async Task<bool> UploadFileToStorage(Stream fileStream, string fileName,
                                                            AzureStorageConfig _storageConfig)
        {
            // Create the container URI
            var containerUri = new Uri($"https://{_storageConfig.AccountName}.blob.core.windows.net/{_storageConfig.ImageContainer}");

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

            // Get blob client and upload
            var blobClient = containerClient.GetBlobClient(fileName);
            await blobClient.UploadAsync(fileStream, overwrite: true);

            return await Task.FromResult(true);
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
