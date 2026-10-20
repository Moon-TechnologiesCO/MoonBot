namespace MoonBot.Core.Security;

// Almacén de secretos (API keys). En Windows lo implementa Credential Manager.
public interface ISecretStore
{
    void Save(string name, string secret);
    string? Read(string name);
    void Delete(string name);
}

public static class SecretNames
{
    public const string ProviderApiKey = "provider-api-key";
}
