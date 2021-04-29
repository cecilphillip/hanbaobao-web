﻿using HanBaoBao;
using HanBaoBaoWeb;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Orleans.Hosting;
using System;
using System.Diagnostics;

// 1. Use the new W3C Activity Id format
Activity.DefaultIdFormat = ActivityIdFormat.W3C;

var host = Host.CreateDefaultBuilder(args)
    .UseOrleans((ctx, builder) =>
    {
        // 2. Add an incoming and outgoing filter to Orleans to create spans and propagate trace ids
        builder
            .AddOutgoingGrainCallFilter<ActivityPropagationOutgoingGrainCallFilter>()
            .AddIncomingGrainCallFilter<ActivityPropagationIncomingGrainCallFilter>();

        if (ctx.HostingEnvironment.IsDevelopment())
        {
            builder.UseLocalhostClustering();
            builder.AddMemoryGrainStorage("definitions");
        }
        else
        {
            // In Kubernetes, we use environment variables and the pod manifest
            builder.UseKubernetesHosting();

            // Use Redis for clustering & persistence
            var redisAddress = $"{Environment.GetEnvironmentVariable("REDIS")}:6379";
            builder.UseRedisClustering(options => options.ConnectionString = redisAddress);
            builder.AddRedisGrainStorage(
                "definitions",
                options => options.ConnectionString = redisAddress);
        }
    })
    .ConfigureWebHostDefaults(webBuilder =>
    {
        webBuilder.ConfigureServices(services => services.AddControllers());
        webBuilder.Configure((ctx, app) =>
        {
            if (ctx.HostingEnvironment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        });
    })
    .ConfigureServices((ctx, services) =>
    {
        // 3. Configure OpenTelemetry to sample, collect, and export traces
        services.AddOpenTelemetryTracing(telemetry =>
        {
            telemetry.SetResourceBuilder(
                ResourceBuilder.CreateDefault()
                    .AddService(ctx.HostingEnvironment.ApplicationName));
            telemetry.AddSource("orleans.runtime.graincall");

            telemetry.AddAspNetCoreInstrumentation();

            // Add console output during development
            if (ctx.HostingEnvironment.IsDevelopment())
            {
                telemetry.AddConsoleExporter();
            }

            // Enable Zipkin integration
            // Zipkin can be launched using docker:
            // > docker run -d -p 9411:9411 openzipkin/zipkin
            telemetry.AddZipkinExporter(zipkin => zipkin.Endpoint = new Uri("http://localhost:9411/api/v2/spans"));
        });

        services.AddSingleton<ReferenceDataService>();
    });

await host.RunConsoleAsync();
