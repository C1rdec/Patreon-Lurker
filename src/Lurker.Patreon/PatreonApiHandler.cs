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
    private readonly HttpClient _client;
    private readonly int[] _ports;
    private readonly string _clientId;
    private readonly string _whiteListUrl;

    public PatreonApiHandler(int[] ports, string clientId)
        : this(ports, clientId, null)
    {
    }

    public PatreonApiHandler(int[] ports, string clientId, string whiteListUrl)
    {
        _ports = ports;
        _clientId = clientId;
        _whiteListUrl = whiteListUrl;
        _client = new HttpClient();
    }

    public Task<TokenResult> GetAccessTokenAsync()
        => GetAccessTokenAsync(null);

    public async Task<TokenResult> GetAccessTokenAsync(TokenResult tokenResult)
    {
        if (tokenResult != null)
        {
            return await RefreshTokens(tokenResult);
        }

        foreach (var port in _ports) 
        {
            var redirectUrl = GetRedirectUrl(port);

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

            var http = new HttpListener();
            http.Prefixes.Add(redirectUrl);
            http.Start();

            var state = Guid.NewGuid();
            Process.Start(new ProcessStartInfo
            {
                FileName = $"https://www.patreon.com/oauth2/authorize?response_type=code&client_id={_clientId}&redirect_uri={redirectUrl}&state={state}",
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
        var json = await Get("https://www.patreon.com/api/oauth2/api/current_user", token);
        if (string.IsNullOrEmpty(json))
        {
            return false;
        }

        var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("included", out var included))
        {
            var isPatron =  included.EnumerateArray().Any(e => e.GetProperty("type").GetString() == "campaign" && e.GetProperty("id").GetString() == campaignId);
            if (isPatron)
            {
                return isPatron;
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
        var json = await Get("https://www.patreon.com/api/oauth2/api/current_user", token);
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

    protected virtual string SuccessHtml => "<div style='text-align: center;'><h1>Happy lurking</h1><span>You can close this window</span><div>";

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
        var request = new HttpRequestMessage(HttpMethod.Post, "https://www.patreon.com/api/oauth2/token");
        var values = new[]
        {
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", redirectUrl),
            new KeyValuePair<string, string>("client_id", _clientId),
            new KeyValuePair<string, string>("client_secret", "invalid"),
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

    private async Task<TokenResult> RefreshTokens(TokenResult oldToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://www.patreon.com/api/oauth2/token");

        // Body
        var values = new[]
        {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", oldToken.RefreshToken),
            new KeyValuePair<string, string>("client_id", _clientId),
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