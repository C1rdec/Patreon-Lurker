using Lurker.Patreon;

using var service = new PatreonService(new int[] { 8080, 8181, 8282 }, "<ClientId>");

var tokenResult = await service.GetAccessTokenAsync();

await service.IsPledging("<CampaignId>", tokenResult.AccessToken);
