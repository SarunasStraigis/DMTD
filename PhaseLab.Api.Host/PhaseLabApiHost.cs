using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using PhaseLab.Api;

namespace PhaseLab.Api.Host;

public sealed class PhaseLabApiHost : IAsyncDisposable
{
    private WebApplication? _app;

    public async Task StartAsync(IModuleApiRegistry registry, PhaseLabApiOptions options, CancellationToken cancellationToken = default)
    {
        if (_app is not null)
        {
            return;
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(PhaseLabApiHost).Assembly.FullName,
            Args = Array.Empty<string>()
        });

        builder.WebHost.UseUrls($"http://127.0.0.1:{options.Port}");
        builder.Services.AddSingleton(registry);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(swagger =>
        {
            swagger.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "PhaseLab API",
                Version = "v1",
                Description = "Localhost REST API for PhaseLab measurement modules."
            });
        });

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (ModuleApiException ex)
            {
                context.Response.StatusCode = ex.StatusCode;
                await context.Response.WriteAsJsonAsync(new { detail = ex.Message });
            }
        });
        MapRoutes(app);
        app.UseSwagger(options => options.RouteTemplate = "openapi/{documentName}.json");
        app.UseSwaggerUI(swagger =>
        {
            swagger.SwaggerEndpoint("/openapi/v1.json", "PhaseLab API v1");
            swagger.RoutePrefix = "docs";
        });

        await app.StartAsync(cancellationToken);
        _app = app;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_app is null)
        {
            return;
        }

        await _app.StopAsync(cancellationToken);
        await _app.DisposeAsync();
        _app = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private static void MapRoutes(WebApplication app)
    {
        app.MapGet("/api", (IModuleApiRegistry registry) =>
            Results.Ok(new AppInfoDto
            {
                Name = "PhaseLab",
                ApiVersion = "v1",
                ModuleIds = registry.Modules.Select(m => m.Id).ToList()
            }))
            .WithName("GetAppInfo")
            .WithTags("App");

        app.MapGet("/api/modules", (IModuleApiRegistry registry) =>
            Results.Ok(registry.Modules.Select(m => m.GetInfo()).ToList()))
            .WithName("ListModules")
            .WithTags("Modules");

        app.MapGet("/api/modules/{moduleId}", (string moduleId, IModuleApiRegistry registry) =>
            Results.Ok(registry.GetRequired(moduleId).GetInfo()))
            .WithName("GetModule")
            .WithTags("Modules");

        app.MapGet("/api/modules/{moduleId}/status", (string moduleId, IModuleApiRegistry registry) =>
            Results.Ok(registry.GetRequired(moduleId).GetStatus()))
            .WithName("GetModuleStatus")
            .WithTags("Modules");

        app.MapGet("/api/modules/{moduleId}/snapshot", (string moduleId, IModuleApiRegistry registry) =>
            Results.Ok(registry.GetRequired(moduleId).GetSnapshot()))
            .WithName("GetModuleSnapshot")
            .WithTags("Modules");

        app.MapGet("/api/modules/{moduleId}/devices", (string moduleId, IModuleApiRegistry registry) =>
            Results.Ok(registry.GetRequired(moduleId).ListDevices()))
            .WithName("ListModuleDevices")
            .WithTags("Devices");

        app.MapPost("/api/modules/{moduleId}/devices/refresh", (string moduleId, IModuleApiRegistry registry) =>
        {
            registry.GetRequired(moduleId).RefreshDevices();
            return Results.Ok(new ActionResultDto { Status = "refreshed" });
        })
            .WithName("RefreshModuleDevices")
            .WithTags("Devices");

        app.MapPost("/api/modules/{moduleId}/capture/start", (
            string moduleId,
            IModuleApiRegistry registry,
            [FromBody] CaptureRequestDto? request) =>
        {
            registry.GetRequired(moduleId).StartCapture(request);
            return Results.Ok(new ActionResultDto { Status = "started" });
        })
            .WithName("StartCapture")
            .WithTags("Capture");

        app.MapPost("/api/modules/{moduleId}/capture/stop", (string moduleId, IModuleApiRegistry registry) =>
        {
            registry.GetRequired(moduleId).StopCapture();
            return Results.Ok(new ActionResultDto { Status = "stopped" });
        })
            .WithName("StopCapture")
            .WithTags("Capture");

        app.MapPost("/api/modules/{moduleId}/actions/{actionId}", (
            string moduleId,
            string actionId,
            IModuleApiRegistry registry) =>
        {
            var result = registry.GetRequired(moduleId).ExecuteAction(actionId);
            return Results.Ok(result);
        })
            .WithName("ExecuteModuleAction")
            .WithTags("Actions");
    }
}

public sealed class PhaseLabApiOptions
{
    public int Port { get; init; } = 8787;
}
