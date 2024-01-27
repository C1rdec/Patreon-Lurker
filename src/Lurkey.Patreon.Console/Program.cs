using Lurker.Patreon;
using Lurker.Patreon.Models;

var credential = new PatreonApiCredential
{
    ClientId = "uI0ZqaEsUckHlpQdOgnJfGtA9tjdKy4A9IpfJj9M2ZIMRkZrRZSemBJ2DtNxbPJm",
    Ports = [8080, 8181, 8282]
};

var service = new PatreonService(credential);
await service.LoginAsync();

var isPledged = await service.CheckPledgeStatusAsync("3779584");
