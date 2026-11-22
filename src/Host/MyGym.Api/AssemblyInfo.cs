using System.Runtime.CompilerServices;

// Lets MyGym.Api.Tests unit-test TelegramWebhookEndpoints.SecretTokensMatch directly, as a pure
// function of two strings, without standing up a WebApplicationFactory host.
[assembly: InternalsVisibleTo("MyGym.Api.Tests")]
