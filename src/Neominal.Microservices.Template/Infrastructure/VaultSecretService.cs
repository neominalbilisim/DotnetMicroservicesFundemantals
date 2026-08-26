using VaultSharp;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.Token;

namespace Neominal.Microservices.Template.Infrastructure;

/// <summary>
/// Senaryo: HashiCorp Vault üzerinden hassas verilerin (connection string,
/// API key vb.) okunması/yazılması. Vault dev mode'da KV v2 secret engine
/// "secret/" mount point'inde varsayılan olarak açıktır.
/// </summary>
public interface IVaultSecretService
{
    Task<IDictionary<string, object>> GetSecretAsync(string path);
    Task WriteSecretAsync(string path, IDictionary<string, object> data);
}

public class VaultSecretService : IVaultSecretService
{
    private readonly IVaultClient _client;
    private const string MountPoint = "secret";

    public VaultSecretService(IConfiguration configuration)
    {
        var address = configuration["Vault:Address"] ?? "http://localhost:8200";
        var token = configuration["Vault:Token"] ?? "root";

        IAuthMethodInfo authMethod = new TokenAuthMethodInfo(token);
        var settings = new VaultClientSettings(address, authMethod);
        _client = new VaultClient(settings);
    }

    public async Task<IDictionary<string, object>> GetSecretAsync(string path)
    {
        var secret = await _client.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(path: path, mountPoint: MountPoint);

        return secret.Data.Data;
    }

    public async Task WriteSecretAsync(string path, IDictionary<string, object> data)
    {
        await _client.V1.Secrets.KeyValue.V2
            .WriteSecretAsync(path: path, data: data, mountPoint: MountPoint);
    }
}
