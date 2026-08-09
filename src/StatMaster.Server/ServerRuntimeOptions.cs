using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;

namespace StatMaster.Server;

public sealed class ServerRuntimeOptions //class to store the runtime options for the server like port, certificate path, certificate password, expected token
{
    public int Port { get; init; } = 50001;
    public string CertPath { get; init; } = "../../certs/statmaster-dev.pfx";
    public string CertPassword { get; init; } = "devpass123";
    public string ExpectedToken { get; init; } = "dev-token";

    public X509Certificate2 LoadCertificate() => new(CertPath, CertPassword);

    public static ServerRuntimeOptions FromConfiguration(IConfiguration configuration)
    {
        return new ServerRuntimeOptions
        {
            Port = configuration.GetValue("Server:Port", 50001),
            CertPath = configuration["Server:CertPath"] ?? "../../certs/statmaster-dev.pfx",
            CertPassword = configuration["Server:CertPassword"] ?? "devpass123",
            ExpectedToken = configuration["Server:ExpectedToken"] ?? "dev-token"
        };
    }
}
