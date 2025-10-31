using GatewayAPi;
using StackExchange.Redis;

public class RedisRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConnectionMultiplexer _redis;
    private readonly RateLimitOptions _options;
    private readonly ILogger<RedisRateLimitMiddleware> _logger;

    public RedisRateLimitMiddleware(RequestDelegate next, IConnectionMultiplexer redis, RateLimitOptions options, ILogger<RedisRateLimitMiddleware> logger)
    {
        _next = next;
        _redis = redis;
        _options = options;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // get client IP (we earlier set X-Client-IP)
        string clientIp = context.Request.Headers["X-Client-IP"].FirstOrDefault() ?? "unknown";

        // key per IP (could include route/path if you want per-endpoint limits)
        string key = $"ratelimit:{clientIp}";

        var db = _redis.GetDatabase();

        // atomic increment
        long count = await db.StringIncrementAsync(key);

        if (count == 1)
        {
            // set expiry the first time
            await db.KeyExpireAsync(key, TimeSpan.FromSeconds(_options.WindowSeconds));
        }

        if (count > _options.MaxRequests)
        {
            // Over the limit -> 429
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = _options.WindowSeconds.ToString();
            // Optional body
            await context.Response.WriteAsJsonAsync(new
            {
                error = "TooManyRequests",
                message = $"Rate limit exceeded. Allowed {_options.MaxRequests} requests per {_options.WindowSeconds} seconds."
            });
            _logger.LogWarning("IP {ip} exceeded rate limit: {count}/{limit}", clientIp, count, _options.MaxRequests);
            return;
        }

        // forward to next (YARP will proxy)
        await _next(context);
    }
}
