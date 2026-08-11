using Amazon.S3;
using Amazon.S3.Model;
using Azure.Storage.Blobs;

namespace HajjVR.Services;

public interface IStorageService
{
    /// <summary>Simpan file, kembalikan URL publik yang bisa diakses browser.</summary>
    Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default);
    Task DeleteAsync(string url, CancellationToken ct = default);
    string ProviderName { get; }
}

/// <summary>Router storage: memilih provider berdasarkan setting "Storage:Provider" (bisa diubah dari UI tanpa restart).</summary>
public class StorageRouter(SettingsService settings, IWebHostEnvironment env) : IStorageService
{
    public string ProviderName => settings.Get("Storage:Provider", "FileSystem");

    private IStorageService Resolve() => ProviderName.ToLowerInvariant() switch
    {
        "azureblob" or "azure" => new AzureBlobStorageService(settings),
        "s3" => new S3StorageService(settings, minio: false),
        "minio" => new S3StorageService(settings, minio: true),
        _ => new FileSystemStorageService(env, settings)
    };

    public Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
        => Resolve().SaveAsync(content, fileName, contentType, ct);

    public Task DeleteAsync(string url, CancellationToken ct = default) => Resolve().DeleteAsync(url, ct);

    protected static string SafeName(string fileName)
        => $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}-{Path.GetFileName(fileName).Replace(' ', '_')}";
}

public class FileSystemStorageService(IWebHostEnvironment env, SettingsService settings) : IStorageService
{
    public string ProviderName => "FileSystem";

    private string RootPath
    {
        get
        {
            var sub = settings.Get("Storage:FileSystem:Path", "uploads");
            var root = Path.Combine(env.WebRootPath, sub);
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public async Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var name = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}-{Path.GetFileName(fileName).Replace(' ', '_')}";
        var full = Path.Combine(RootPath, name);
        await using var fs = File.Create(full);
        await content.CopyToAsync(fs, ct);
        var sub = settings.Get("Storage:FileSystem:Path", "uploads");
        return $"/{sub}/{name}";
    }

    public Task DeleteAsync(string url, CancellationToken ct = default)
    {
        var name = Path.GetFileName(url);
        var full = Path.Combine(RootPath, name);
        if (File.Exists(full)) File.Delete(full);
        return Task.CompletedTask;
    }
}

public class AzureBlobStorageService(SettingsService settings) : IStorageService
{
    public string ProviderName => "AzureBlob";

    private BlobContainerClient Container()
    {
        var cs = settings.Get("Storage:Azure:ConnectionString")
            ?? throw new InvalidOperationException("Storage:Azure:ConnectionString belum dikonfigurasi.");
        var container = settings.Get("Storage:Azure:Container", "hajjvr");
        var client = new BlobContainerClient(cs, container);
        client.CreateIfNotExists(Azure.Storage.Blobs.Models.PublicAccessType.Blob);
        return client;
    }

    public async Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var name = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}-{Path.GetFileName(fileName)}";
        var blob = Container().GetBlobClient(name);
        await blob.UploadAsync(content, new Azure.Storage.Blobs.Models.BlobUploadOptions
        {
            HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders { ContentType = contentType }
        }, ct);
        return blob.Uri.ToString();
    }

    public async Task DeleteAsync(string url, CancellationToken ct = default)
    {
        var name = Path.GetFileName(new Uri(url).LocalPath);
        await Container().GetBlobClient(name).DeleteIfExistsAsync(cancellationToken: ct);
    }
}

/// <summary>Provider S3; juga dipakai MinIO (S3-compatible, path-style + ServiceURL).</summary>
public class S3StorageService(SettingsService settings, bool minio) : IStorageService
{
    public string ProviderName => minio ? "MinIO" : "S3";

    private string Prefix => minio ? "Storage:MinIO" : "Storage:S3";
    private string Bucket => settings.Get($"{Prefix}:Bucket", "hajjvr");

    private AmazonS3Client Client()
    {
        var accessKey = settings.Get($"{Prefix}:AccessKey", "");
        var secretKey = settings.Get($"{Prefix}:SecretKey", "");
        var config = new AmazonS3Config();
        var serviceUrl = settings.Get($"{Prefix}:ServiceUrl");
        if (!string.IsNullOrEmpty(serviceUrl))
        {
            config.ServiceURL = serviceUrl;
            config.ForcePathStyle = true;
        }
        else
        {
            config.AuthenticationRegion = settings.Get($"{Prefix}:Region", "ap-southeast-1");
        }
        return new AmazonS3Client(accessKey, secretKey, config);
    }

    public async Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var name = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}-{Path.GetFileName(fileName)}";
        using var client = Client();
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        ms.Position = 0;
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = Bucket,
            Key = name,
            InputStream = ms,
            ContentType = contentType,
            CannedACL = S3CannedACL.PublicRead
        }, ct);
        var serviceUrl = settings.Get($"{Prefix}:ServiceUrl");
        return !string.IsNullOrEmpty(serviceUrl)
            ? $"{serviceUrl.TrimEnd('/')}/{Bucket}/{name}"
            : $"https://{Bucket}.s3.{settings.Get($"{Prefix}:Region", "ap-southeast-1")}.amazonaws.com/{name}";
    }

    public async Task DeleteAsync(string url, CancellationToken ct = default)
    {
        var name = Path.GetFileName(new Uri(url).LocalPath);
        using var client = Client();
        await client.DeleteObjectAsync(Bucket, name, ct);
    }
}
