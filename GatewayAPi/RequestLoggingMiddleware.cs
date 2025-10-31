using Serilog;
using Serilog.Context;

namespace GatewayAPi.Middlewares
{
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;

        public RequestLoggingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {

            var requestId = Guid.NewGuid().ToString();

            context.Items["RequestId"] = requestId;
            context.Request.Headers["X-Request-ID"] = requestId;


            using (LogContext.PushProperty("RequestId", requestId))
            {
                Log.Information("➡️ Incoming request {Method} {Path}",
                  context.Request.Method,
                  context.Request.Path);

                await _next(context);

                Log.Information("⬅️ Response {StatusCode}",
                               context.Response.StatusCode);
            }
        }
    }
}