namespace ITBees.MediaStorage.Interfaces;

public interface ISecurityCheckService
{
    bool CanAcceesForUpload(string auth, string? sourceIp, Guid agentGuid);
}