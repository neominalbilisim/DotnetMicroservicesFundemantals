using Hangfire;
using Neominal.Microservices.Template.Infrastructure;

namespace Neominal.Microservices.Template.Endpoints;

public record FireAndForgetJobRequest(string? Message);
public record ScheduleJobRequest(string Message, int DelaySeconds = 10);
public record RecurringJobRequest(string Message, string CronExpression = "*/1 * * * *");

public static class JobsEndpoints
{
    private const string RecurringJobId = "demo-recurring-job";

    public static void MapJobsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/demo/jobs").WithTags("Demo Senaryolari - Hangfire");

        // -----------------------------------------------------------
        // 1) Fire-and-forget: hemen kuyruga alinir, arka planda calisir
        // -----------------------------------------------------------
        group.MapPost("/fire-and-forget", (FireAndForgetJobRequest? request) =>
        {
            var message = request?.Message ?? "Fire-and-forget job calisti";
            var jobId = BackgroundJob.Enqueue<DemoJobService>(job => job.Execute(message));
            return Results.Accepted(value: new { status = "queued", jobId });
        });

        // -----------------------------------------------------------
        // 2) Scheduled: belirtilen sure sonra bir kez calisir
        // -----------------------------------------------------------
        group.MapPost("/schedule", (ScheduleJobRequest request) =>
        {
            var jobId = BackgroundJob.Schedule<DemoJobService>(
                job => job.Execute(request.Message),
                TimeSpan.FromSeconds(request.DelaySeconds));

            return Results.Accepted(value: new { status = "scheduled", jobId, delaySeconds = request.DelaySeconds });
        });

        // -----------------------------------------------------------
        // 3) Recurring: cron ifadesine gore tekrarlayan job
        // -----------------------------------------------------------
        group.MapPost("/recurring", (RecurringJobRequest request) =>
        {
            RecurringJob.AddOrUpdate<DemoJobService>(
                RecurringJobId,
                job => job.Execute(request.Message),
                request.CronExpression);

            return Results.Ok(new { status = "recurring-job-registered", jobId = RecurringJobId, cron = request.CronExpression });
        });

        group.MapDelete("/recurring", () =>
        {
            RecurringJob.RemoveIfExists(RecurringJobId);
            return Results.Ok(new { status = "recurring-job-removed", jobId = RecurringJobId });
        });
    }
}
