using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Telegram.Bot;

namespace MyGym.IntegrationTests;

public sealed class ApiFixture(PostgresFixture postgres) : WebApplicationFactory<Program>
{
    public const string BotToken = "123456:TEST-BOT-TOKEN-FOR-INTEGRATION";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", postgres.ConnectionString);
        builder.UseSetting("Telegram:BotToken", BotToken);
        builder.UseSetting("Telegram:WebhookSecretToken", "test-webhook-secret");
        builder.UseSetting("Jwt:Issuer", "mygym-tests");
        builder.UseSetting("Jwt:Audience", "mygym-tests");
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-at-least-32-bytes-long!!");
        builder.UseSetting("RunMigrationsOnStartup", "false");

        // Program.cs registers ITelegramBotClient with the (fake, but real-shaped) bot token
        // above. Replaced here with a no-op fake so WebhookTests' "/start" case never attempts a
        // genuine outbound call to api.telegram.org — see FakeTelegramBotClient's own comment.
        builder.ConfigureTestServices(services =>
            services.AddSingleton<ITelegramBotClient>(new FakeTelegramBotClient()));
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync(long telegramUserId, string firstName = "Akmal")
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/telegram", new { initData = BuildInitData(telegramUserId, firstName) });

        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<AuthTokens>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        return client;
    }

    public static string BuildInitData(long telegramUserId, string firstName = "Akmal")
    {
        var user = $"{{\"id\":{telegramUserId},\"first_name\":\"{firstName}\",\"language_code\":\"uz\"}}";
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ["user"] = user
        };

        var dataCheckString = string.Join('\n', fields.Select(f => $"{f.Key}={f.Value}"));
        using var secretKeyHmac = new HMACSHA256(Encoding.UTF8.GetBytes("WebAppData"));
        var secretKey = secretKeyHmac.ComputeHash(Encoding.UTF8.GetBytes(BotToken));
        using var signatureHmac = new HMACSHA256(secretKey);
        var hash = Convert.ToHexStringLower(
            signatureHmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString)));

        var parts = fields.Select(f => $"{f.Key}={WebUtility.UrlEncode(f.Value)}").ToList();
        parts.Add($"hash={hash}");
        return string.Join('&', parts);
    }

    public sealed record AuthTokens(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);
}
