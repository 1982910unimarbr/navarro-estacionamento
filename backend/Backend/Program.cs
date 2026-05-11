using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Services;
using System.Text.Json;
using System;
using Scalar.AspNetCore;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
});
// use minimal built-in OpenAPI helper
builder.Services.AddOpenApi();
builder.Services.AddHostedService<IncidentMonitor>();
builder.Services.AddHostedService<MqttIngestService>();
builder.Services.AddScoped<IParkingIngestService, ParkingIngestService>();
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();

    var jobKey = new Quartz.JobKey("SectorSnapshotJob");
    q.AddJob<SectorSnapshotJob>(opts => opts.WithIdentity(jobKey));
    q.AddTrigger(opts => opts
        .ForJob(jobKey)
        .WithIdentity("SectorSnapshotTrigger")
        .WithSimpleSchedule(x => x.WithIntervalInSeconds(3).RepeatForever()));

    var autoFreeKey = new Quartz.JobKey("SpotAutoFreeJob");
    q.AddJob<SpotAutoFreeJob>(opts => opts.WithIdentity(autoFreeKey));
    q.AddTrigger(opts => opts
        .ForJob(autoFreeKey)
        .WithIdentity("SpotAutoFreeTrigger")
        .WithSimpleSchedule(x => x.WithIntervalInSeconds(3).RepeatForever()));
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

var conn = builder.Configuration.GetConnectionString("Default") ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? "Host=postgres;Database=parking;Username=parking_user;Password=parking_pass";

builder.Services.AddDbContext<ParkingContext>(opt => opt.UseNpgsql(conn));

var app = builder.Build();



app.MapOpenApi();
app.MapScalarApiReference();
app.MapControllers();





// ensure database exists and seed initial spots for sectors A,B,C
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ParkingContext>();
    // create database schema if missing with retry loop for DB readiness
    var attempts = 0;
    var maxAttempts = 20;
    var created = false;
    while (!created && attempts < maxAttempts)
    {
        try
        {
            db.Database.EnsureCreated();
            created = true;
        }
        catch (Exception ex)
        {
            attempts++;
            Console.WriteLine($"Database not ready (attempt {attempts}): {ex.Message}");
            System.Threading.Thread.Sleep(2000);
        }
    }

    if (!created)
    {
        Console.WriteLine("Failed to initialize database after retries.");
    }

    // seed spots if empty
    if (created && !db.Spots.Any())
    {
        var sectors = new[] { "A", "B", "C" };
        foreach (var s in sectors)
        {
            for (int i = 1; i <= 30; i++)
            {
                var id = $"{s}-{i.ToString().PadLeft(2, '0')}";
                db.Spots.Add(new Backend.Models.Spot { SpotId = id, SectorId = s, CurrentState = "FREE", LastChangeTs = DateTime.UtcNow, LastEventId = null });
            }
        }
        db.SaveChanges();
    }
}

app.Run();