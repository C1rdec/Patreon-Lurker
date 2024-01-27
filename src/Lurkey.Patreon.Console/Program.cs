using Lurker.Patreon;
using Lurker.Patreon.Models;

var credential = new PatreonApiCredential
{
    ClientId = "__REMOVED_CLIENT_ID__",
    Ports = [8080, 8181, 8282]
};

var service = new PatreonService(credential);
await service.LoginAsync();

var isPledged = await service.CheckPledgeStatusAsync("3779584");
