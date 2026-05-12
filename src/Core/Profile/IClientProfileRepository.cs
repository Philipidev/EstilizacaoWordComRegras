namespace WordComplianceValidator.Core.Profile;

public interface IClientProfileRepository
{
    Task<ClientProfile> LoadAsync(string path, CancellationToken cancellationToken = default);
    Task<string> SaveAsync(ClientProfile profile, string rootDirectory, CancellationToken cancellationToken = default);
}
