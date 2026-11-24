using Lurker.Patreon;
using Lurker.Patreon.Models;

var clientId = Environment.GetEnvironmentVariable("PATREON_CLIENT_ID")
    ?? throw new InvalidOperationException("PATREON_CLIENT_ID is not set.");
var clientSecret = Environment.GetEnvironmentVariable("PATREON_CLIENT_SECRET")
    ?? throw new InvalidOperationException("PATREON_CLIENT_SECRET is not set.");

var credential = new PatreonApiCredential
{
    ClientId = clientId,
    ClientSecret = clientSecret,
    Ports = [8080, 8181, 8282],
};

var service = new PatreonService(credential);
await service.LoginAsync();

var isPledged = await service.CheckPledgeStatusAsync("3779584");
Console.WriteLine(isPledged);
