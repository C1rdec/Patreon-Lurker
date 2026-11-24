using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;

namespace Lurker.Patreon;

internal class PatreonApiHandler : IDisposable
{
    public static readonly string DefaultSuccessHtml = "<body style='background-color: dimgray;'><div style='text-align: center;color: lightgray;'><h1>Happy lurking</h1><span>You can close this window</span><div></body>";

    private const string AuthorizationUrl = "https://www.patreon.com/oauth2/authorize";
    private const string TokenUrl = "https://www.patreon.com/api/oauth2/token";
    private const string IdentityUrl = "https://www.patreon.com/api/oauth2/v2/identity?include=memberships&fields%5Bmember%5D=patron_status";

    private readonly HttpClient _client;
    private readonly int[] _ports;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _whiteListUrl;

    public PatreonApiHandler(int[] ports, string clientId, string clientSecret)
        : this(ports, clientId, clientSecret, null)
    {
    }

    public PatreonApiHandler(int[] ports, string clientId, string clientSecret, string whiteListUrl)
    {
        _ports = ports;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _whiteListUrl = whiteListUrl;
        _client = new HttpClient();
    }

    public Task<TokenResult> GetAccessTokenAsync()
        => GetAccessTokenAsync(null);

    public async Task<TokenResult> GetAccessTokenAsync(string refreshToken)
    {
        if (!string.IsNullOrEmpty(refreshToken))
        {
            return await RefreshTokens(refreshToken);
        }

        foreach (var port in _ports) 
        {

            try
            {
                var tcpListener = new TcpListener(IPAddress.Loopback, port);
                tcpListener.Start();
                tcpListener.Stop();
            }
            catch
            {
                continue;
            }
            var redirectUrl = GetRedirectUrl(port);
            var http = new HttpListener();
            http.Prefixes.Add(redirectUrl);
            http.Start();

            var state = Guid.NewGuid();
            var encodedRedirectUrl = Uri.EscapeDataString(redirectUrl);
            Process.Start(new ProcessStartInfo
            {
                FileName = $"{AuthorizationUrl}?response_type=code&client_id={_clientId}&redirect_uri={encodedRedirectUrl}&scope=identity%20identity.memberships&state={state}",
                UseShellExecute = true
            });

            var context = await http.GetContextAsync();
            var actualState = context.Request.QueryString.Get("state");

            if (state != Guid.Parse(actualState))
            {
                throw new InvalidOperationException();
            }

            var response = context.Response;
            var buffer = Encoding.UTF8.GetBytes(SuccessHtml);
            response.ContentLength64 = buffer.Length;
            var responseOutput = response.OutputStream;
            var responseTask = responseOutput.WriteAsync(buffer, 0, buffer.Length).ContinueWith((task) =>
            {
                responseOutput.Close();
                http.Stop();
            });

            return await GetTokenResultAsync(context.Request.QueryString.Get("code"), redirectUrl);
        }

        throw new InvalidOperationException();
    }

    public async Task<bool> IsPledging(string campaignId, string token)
    {
        var json = await Get(IdentityUrl, token);
        if (string.IsNullOrEmpty(json))
        {
            return false;
        }

        var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("included", out var included))
        {
            var isPatron = included.EnumerateArray().Any(member =>
                member.GetProperty("type").GetString() == "member"
                && member.TryGetProperty("attributes", out var attributes)
                && attributes.TryGetProperty("patron_status", out var patronStatus)
                && patronStatus.GetString() == "active_patron"
                && member.TryGetProperty("relationships", out var relationships)
                && relationships.TryGetProperty("campaign", out var campaign)
                && campaign.TryGetProperty("data", out var campaignData)
                && campaignData.GetProperty("id").GetString() == campaignId);

            if (isPatron)
            {
                return true;
            }
        }

        if (string.IsNullOrEmpty(_whiteListUrl))
        {
            return false;
        }

        var whiteList = await Get($@"{_whiteListUrl}?{Guid.NewGuid()}");
        var patrons = whiteList.Trim().Split(';');
        var patronId = document.RootElement.GetProperty("data").GetProperty("id").GetString();

        return patrons.Any(p => p == patronId);
    }

    public async Task<string> GetPatronId(string token)
    {
        var json = await Get(IdentityUrl, token);
        if (string.IsNullOrEmpty(json))
        {
            return string.Empty;
        }

        var document = JsonDocument.Parse(json);

        return document.RootElement.GetProperty("data").GetProperty("id").GetString();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual string SuccessHtml => DefaultSuccessHtml;

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _client.Dispose();
        }
    }

    private static string GetRedirectUrl(int port)
    {
        return $"http://localhost:{port}/";
    }

    private async Task<TokenResult> GetTokenResultAsync(string code, string redirectUrl)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
        var values = new[]
        {
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", redirectUrl),
            new KeyValuePair<string, string>("client_id", _clientId),
            new KeyValuePair<string, string>("client_secret", _clientSecret),
        };

        using var content = new FormUrlEncodedContent(values);
        request.Content = content;

        var response = await _client.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            var value = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<TokenResult>(value);
        }
        else
        {
            throw new AuthenticationException();
        }
    }

    private async Task<TokenResult> RefreshTokens(string refreshToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);

        // Body
        var values = new[]
        {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshToken),
            new KeyValuePair<string, string>("client_id", _clientId),
            new KeyValuePair<string, string>("client_secret", _clientSecret),
        };

        using var content = new FormUrlEncodedContent(values);
        request.Content = content;

        var response = await _client.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            var value = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<TokenResult>(value);
        }

        throw new AuthenticationException();
    }

    private Task<string> Get(string url)
        => Get(url, null);

    private async Task<string> Get(string url, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        var response = await _client.SendAsync(request);

        if (!response.IsSuccessStatusCode) 
        {
            return string.Empty;
        }

        return await response.Content.ReadAsStringAsync();
    }
}