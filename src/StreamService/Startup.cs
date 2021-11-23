using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LT.DigitalOffice.StreamService.Data.Provider.MsSql.Ef;
using HealthChecks.UI.Client;
using LT.DigitalOffice.Kernel.Configurations;
using LT.DigitalOffice.Kernel.Extensions;
using LT.DigitalOffice.Kernel.Middlewares.ApiInformation;
using LT.DigitalOffice.Kernel.Middlewares.Token;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Serilog;
using StackExchange.Redis;
using LT.DigitalOffice.Kernel.RedisSupport.Configurations;
using LT.DigitalOffice.StreamService.Models.Dto.Configuration;
using LT.DigitalOffice.Kernel.RedisSupport.Helpers.Interfaces;
using LT.DigitalOffice.Kernel.RedisSupport.Helpers;

namespace LT.DigitalOffice.StreamService
{
  public class Startup : BaseApiInfo
    {
      public const string CorsPolicyName = "LtDoCorsPolicy";

    private readonly RabbitMqConfig _rabbitMqConfig;
    private readonly BaseServiceInfoConfig _serviceInfoConfig;

    private IConfiguration Configuration { get; }

    public Startup(IConfiguration configuration)
    {
      Configuration = configuration;

      _rabbitMqConfig = Configuration
        .GetSection(BaseRabbitMqConfig.SectionName)
        .Get<RabbitMqConfig>();

      _serviceInfoConfig = Configuration
        .GetSection(BaseServiceInfoConfig.SectionName)
        .Get<BaseServiceInfoConfig>();

      Version = "1.0.0.0";
      Description = "StreamService is an API that intended to work with voice and streaming additions.";
      StartTime = DateTime.UtcNow;
      ApiName = $"LT Digital Office - {_serviceInfoConfig.Name}";
    }

    #region public methods

    public void ConfigureServices(IServiceCollection services)
    {
      services.AddCors(options =>
      {
        options.AddPolicy(
          CorsPolicyName,
          builder =>
          {
            builder
              .AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
          });
      });

      if (int.TryParse(Environment.GetEnvironmentVariable("RedisCacheLiveInMinutes"), out int redisCacheLifeTime))
      {
        services.Configure<RedisConfig>(options =>
        {
          options.CacheLiveInMinutes = redisCacheLifeTime;
        });
      }
      else
      {
        services.Configure<RedisConfig>(Configuration.GetSection(RedisConfig.SectionName));
      }

      services.Configure<TokenConfiguration>(Configuration.GetSection("CheckTokenMiddleware"));
      services.Configure<BaseRabbitMqConfig>(Configuration.GetSection(BaseRabbitMqConfig.SectionName));
      services.Configure<BaseServiceInfoConfig>(Configuration.GetSection(BaseServiceInfoConfig.SectionName));

      services.AddHttpContextAccessor();
      services
        .AddControllers()
        .AddJsonOptions(options =>
        {
          options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        })
        .AddNewtonsoftJson();

      string connStr = Environment.GetEnvironmentVariable("ConnectionString");
      if (string.IsNullOrEmpty(connStr))
      {
        connStr = Configuration.GetConnectionString("SQLConnectionString");

        Log.Information($"SQL connection string from appsettings.json was used. Value '{HidePassord(connStr)}'.");
      }
      else
      {
        Log.Information($"SQL connection string from environment was used. Value '{HidePassord(connStr)}'.");
      }

      services.AddDbContext<StreamServiceDbContext>(options =>
      {
        options.UseSqlServer(connStr);
      });

      services.AddHealthChecks()
        .AddSqlServer(connStr)
        .AddRabbitMqCheck();

      string redisConnStr = Environment.GetEnvironmentVariable("RedisConnectionString");
      if (string.IsNullOrEmpty(redisConnStr))
      {
        redisConnStr = Configuration.GetConnectionString("Redis");

        Log.Information($"Redis connection string from appsettings.json was used. Value '{HidePassord(redisConnStr)}'");
      }
      else
      {
        Log.Information($"Redis connection string from environment was used. Value '{HidePassord(redisConnStr)}'");
      }

      services.AddSingleton<IConnectionMultiplexer>(
        x => ConnectionMultiplexer.Connect(redisConnStr));

      services.AddTransient<ICacheNotebook, CacheNotebook>();
      services.AddTransient<IRedisHelper, RedisHelper>();

      services.AddBusinessObjects();

      ConfigureMassTransit(services);
    }

    public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory)
    {
      UpdateDatabase(app);

      app.UseForwardedHeaders();

      app.UseExceptionsHandler(loggerFactory);

      app.UseApiInformation();

      app.UseRouting();

      app.UseMiddleware<TokenMiddleware>();

      app.UseCors(CorsPolicyName);

      app.UseEndpoints(endpoints =>
      {
        endpoints.MapControllers().RequireCors(CorsPolicyName);

        endpoints.MapHealthChecks($"/{_serviceInfoConfig.Id}/hc", new HealthCheckOptions
        {
          ResultStatusCodes = new Dictionary<HealthStatus, int>
          {
            { HealthStatus.Unhealthy, 200 },
            { HealthStatus.Healthy, 200 },
            { HealthStatus.Degraded, 200 },
          },
          Predicate = check => check.Name != "masstransit-bus",
          ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        });
      });
    }

    #endregion

    #region private methods

    private (string username, string password) GetRabbitMqCredentials()
    {
      static string GetString(string envVar, string formAppsettings, string generated, string fieldName)
      {
        string str = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrEmpty(str))
        {
          str = formAppsettings ?? generated;

          Log.Information(
            formAppsettings == null
              ? $"Default RabbitMq {fieldName} was used."
              : $"RabbitMq {fieldName} from appsetings.json was used.");
        }
        else
        {
          Log.Information($"RabbitMq {fieldName} from environment was used.");
        }

        return str;
      }

      return (GetString("RabbitMqUsername", _rabbitMqConfig.Username, $"{_serviceInfoConfig.Name}_{_serviceInfoConfig.Id}", "Username"),
        GetString("RabbitMqPassword", _rabbitMqConfig.Password, _serviceInfoConfig.Id, "Password"));
    }

    private void ConfigureMassTransit(IServiceCollection services)
    {
      (string username, string password) = GetRabbitMqCredentials();

      services.AddMassTransit(x =>
      {

        x.UsingRabbitMq((context, cfg) =>
        {
          cfg.Host(_rabbitMqConfig.Host, "/", host =>
          {
            host.Username(username);
            host.Password(password);
          });

        });

        x.AddRequestClients(_rabbitMqConfig);
      });

      services.AddMassTransitHostedService();
    }


    private void UpdateDatabase(IApplicationBuilder app)
    {
      using var serviceScope = app.ApplicationServices
        .GetRequiredService<IServiceScopeFactory>()
        .CreateScope();

      using var context = serviceScope.ServiceProvider.GetService<StreamServiceDbContext>();

      context.Database.Migrate();
    }

    private string HidePassord(string line)
    {
      string password = "Password";

      int index = line.IndexOf(password, 0, StringComparison.OrdinalIgnoreCase);

      if (index != -1)
      {
        string[] words = Regex.Split(line, @"[=,; ]");

        for (int i = 0; i < words.Length; i++)
        {
          if (string.Equals(password, words[i], StringComparison.OrdinalIgnoreCase))
          {
            line = line.Replace(words[i + 1], "****");
            break;
          }
        }
      }

      return line;
    }

    #endregion
    }
}
