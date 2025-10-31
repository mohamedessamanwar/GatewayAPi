using GatewayAPi.Middlewares;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Context;
using StackExchange.Redis;
using System.Text;

namespace GatewayAPi
{
    public class Program
    {
        public static void Main(string[] args)
        {



            var builder = WebApplication.CreateBuilder(args);
            Log.Logger = new LoggerConfiguration()
                  .ReadFrom.Configuration(builder.Configuration)
                  .Enrich.FromLogContext()
                  .Enrich.WithMachineName()
                  .CreateLogger();

            // Use Serilog for logging
            builder.Host.UseSerilog();

            // Add services to the container.
            var config = builder.Configuration;
            builder.Services.AddControllers();
            builder.Services.AddOpenApi();
            // Redis connection multiplexer (singleton)
            builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
              ConnectionMultiplexer.Connect(config["Redis:Configuration"]));

            // Optionally register IDistributedCache backed by Redis (not required for our simple counter)
            builder.Services.AddStackExchangeRedisCache(options =>
                        {
                            options.Configuration = config["Redis:Configuration"];
                        });

            #region Authentication
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                       .AddJwtBearer(options =>
                    {
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidateAudience = true,
                            ValidateLifetime = true,
                            ValidateIssuerSigningKey = true,
                            ValidIssuer = "https://your-auth-server",
                            ValidAudience = "your-audience",
                            IssuerSigningKey = new SymmetricSecurityKey(
        Encoding.UTF8.GetBytes("supersecretkey123"))
                        };
                    });
            #endregion

            // YARP
            builder.Services.AddReverseProxy()
                   .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

            // Register RateLimiter middleware dependencies if needed (we access Redis via IConnectionMultiplexer inside middleware)
            builder.Services.AddSingleton<RateLimitOptions>(sp =>
                     {
                         return new RateLimitOptions
                         {
                             WindowSeconds = config.GetValue<int>("RateLimit:WindowSeconds", 60),
                             MaxRequests = config.GetValue<int>("RateLimit:MaxRequestsPerWindow", 20)
                         };
                     });

            #region AddOpenTelemetry
            const string serviceName = "gateway-api";
            builder.Logging.AddOpenTelemetry(options =>
             {
                 options
       .SetResourceBuilder(
        ResourceBuilder.CreateDefault()
  .AddService(serviceName));
                 options.AddOtlpExporter();
             });
            builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource.AddService(serviceName))
         .WithTracing(tracing => tracing
             .AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddOtlpExporter()
                    )
                     .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddOtlpExporter()
           );
            #endregion

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

			// Enrich Serilog scope with RequestId for every HTTP request
			app.Use(async (context, next) =>
			{
				using (LogContext.PushProperty("RequestId", context.TraceIdentifier))
				{
					await next();
				}
			});

            // Request logging middleware should be first to track all requests
            app.UseMiddleware<RequestLoggingMiddleware>();
            app.UseMiddleware<RedisRateLimitMiddleware>(); // Custom rate limiting                                                           // This ordering is crucial:
            app.UseAuthentication();  // Authentication second  
            app.MapControllers();           // Specific controller routes
            app.MapGet("/health", () => Results.Ok(new { status = "gateway healthy" }));     // Specific endpoints   
            app.MapReverseProxy();          // Catch-all proxy - MUST BE LAST
            app.Run();

        }
    }
}
