using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.IO;

namespace ImageResizeWebApp.Functions
{
    public class GenerateThumbnailFunction
    {
        private const int ThumbnailWidth = 100;
        private const int ThumbnailHeight = 100;        [Function(nameof(GenerateThumbnail))]
        public async Task<[BlobOutput("thumbnails/{name}", Connection = "AzureStorageConnection")] Stream> GenerateThumbnail(
            [BlobTrigger("images/{name}", Connection = "AzureStorageConnection")] Stream inputBlob,
            string name)
        {
            try
            {
                using (var image = await Image.LoadAsync(inputBlob))
                {
                    var clone = image.Clone(ctx => ctx.Resize(new ResizeOptions
                    {
                        Size = new Size(ThumbnailWidth, ThumbnailHeight),
                        Mode = ResizeMode.Max
                    }));                    var outputStream = new MemoryStream();
                    await clone.SaveAsync(outputStream, image.Metadata.DecodedImageFormat ?? image.Metadata.GetFormatOrDefault());
                    outputStream.Position = 0;
                    return outputStream;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error generating thumbnail for {name}: {ex.Message}", ex);
            }
        }
    }
}
