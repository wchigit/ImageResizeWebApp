namespace ImageResizeWebApp.Models
{    public class AzureStorageConfig
    {
        public required string AccountName { get; set; }
        public string? AccountKey { get; set; }
        public required string ImageContainer { get; set; }
        public required string ThumbnailContainer { get; set; }
    }
}
