namespace DoggyDrop.Services
{
    public class CloudflareR2Settings
    {
        public string AccountId { get; set; } = string.Empty;

        public string AccessKeyId { get; set; } = string.Empty;

        public string SecretAccessKey { get; set; } = string.Empty;

        public string BucketName { get; set; } = string.Empty;

        public string PublicBaseUrl { get; set; } = string.Empty;

        public string? Endpoint { get; set; }

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(AccountId) &&
            !string.IsNullOrWhiteSpace(AccessKeyId) &&
            !string.IsNullOrWhiteSpace(SecretAccessKey) &&
            !string.IsNullOrWhiteSpace(BucketName) &&
            !string.IsNullOrWhiteSpace(PublicBaseUrl);
    }
}
