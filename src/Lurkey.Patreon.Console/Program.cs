using Lurker.Patreon;
using Lurker.Patreon.Models;

var credential = new PatreonApiCredential
{
    ClientId = "4fpNWVu2TEB5GaymlD5ENgxfiXNkLR48zCJ-a8wankXh0dtn-BEgYRibTFLDEkDo",
    Ports = [8080, 8181, 8282],
};

var service = new PatreonService(credential);
await service.LoginAsync();

var isPledged = await service.CheckPledgeStatusAsync("3779584");
Console.WriteLine(isPledged);
