namespace RegentAgent.Gateway.KeyVault;

public interface IMasterKeyProvider
{
    byte[] GetMasterKey();
}
