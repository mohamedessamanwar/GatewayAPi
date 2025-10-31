namespace GatewayAPi
{
    public class RateLimitOptions
    {
        public int WindowSeconds { get; set; } = 60;
        public int MaxRequests { get; set; } = 20;
    }
}