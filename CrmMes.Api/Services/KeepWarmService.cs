namespace CrmMes.Api.Services;

/// <summary>Keeps the free-tier hosting awake so the first login of the day doesn't wait for a cold
/// start. Render stops a free web service after ~15 minutes without incoming requests and Neon suspends
/// the database after ~5 minutes idle; the GitHub scheduled ping alone is not enough because GitHub runs
/// scheduled jobs late or skips them under load.
///
/// Every few minutes the service calls its own public address (Render sets RENDER_EXTERNAL_URL), which
/// counts as incoming traffic: during working hours it calls /health, which also keeps the database
/// warm; outside them /ping, which leaves the database free to sleep and save compute hours. Once the
/// service has been stopped it cannot wake itself: that is what the external ping (GitHub workflow, or
/// a free cron service) is for. Disabled when the URL is not set (local runs, tests).</summary>
public sealed class KeepWarmService : BackgroundService
{
    public const string HttpClientName = "keep-warm";
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(4);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<KeepWarmService> _logger;

    public KeepWarmService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<KeepWarmService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var baseUrl = _configuration["KeepWarm:Url"] ?? Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL");
        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var root))
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            var path = IsWorkingTime(DateTime.UtcNow) ? "health" : "ping";
            try
            {
                using var client = _httpClientFactory.CreateClient(HttpClientName);
                client.Timeout = TimeSpan.FromSeconds(30);
                using var response = await client.GetAsync(new Uri(root, path), stoppingToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Keep-warm {Path} returned {Status}", path, (int)response.StatusCode);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Keep-warm {Path} failed", path);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Monday to Saturday, 6:00 to 20:00 Italian time: when people use the program.</summary>
    public static bool IsWorkingTime(DateTime utcNow)
    {
        DateTime local;
        try
        {
            local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome"));
        }
        catch (TimeZoneNotFoundException)
        {
            local = utcNow.AddHours(1);
        }

        return local.DayOfWeek != DayOfWeek.Sunday && local.Hour is >= 6 and < 20;
    }
}
