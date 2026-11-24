using ClaimsApi;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<ClaimStore>();
builder.Services.AddSingleton(serviceProvider => new ClaimRoutingService(
    serviceProvider.GetRequiredService<IConfiguration>()["CLAIMS_API_VERSION"] ?? "v1"));

var app = builder.Build();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    var correlationId = Guid.NewGuid().ToString("N");
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    using var scope = app.Logger.BeginScope(new Dictionary<string, object>
    {
        ["CorrelationId"] = correlationId
    });
    await next(context);
    app.Logger.LogInformation("Request completed: {Method} {Path} HTTP {StatusCode}",
        context.Request.Method, context.Request.Path, context.Response.StatusCode);
});

var router = app.Services.GetRequiredService<ClaimRoutingService>();
app.Logger.LogInformation("Claims routing started: version={Version} destination={Destination} simulation=true",
    router.Version, router.Destination);

app.MapGet("/health", () => Results.Ok(new { status = "healthy", application = "claims-operations-demo" }));
app.MapGet("/api/demo", () => Results.Ok(new
{
    application = "Claims Operations Workbench",
    version = router.Version,
    syntheticDataOnly = true,
    persistence = "In-memory demo data resets when the API restarts.",
    routingIsSimulated = true
}));
app.MapGet("/api/claims", (ClaimStore store) => Results.Ok(store.List()));
app.MapGet("/api/claims/{id}", (string id, ClaimStore store) =>
    store.Get(id) is { } claim
        ? Results.Ok(claim)
        : Results.NotFound(new { code = "CLAIM_NOT_FOUND", message = "Demo claim not found. It may have reset after a restart." }));
app.MapPost("/api/claims", (ClaimInput input, ClaimStore store, HttpContext context, ILogger<Program> logger) =>
{
    var errors = input.Validate();
    if (errors.Count > 0)
    {
        logger.LogWarning("Claim intake rejected: {InvalidFields}", string.Join(", ", errors.Keys));
        return Results.ValidationProblem(errors);
    }
    var claim = store.Add(input);
    logger.LogInformation("Synthetic claim received: claimId={ClaimId} status={ClaimStatus}", claim.Id, claim.Status);
    return Results.Created($"/api/claims/{claim.Id}", claim);
});
app.MapPost("/api/claims/{id}/route", (string id, ClaimStore store, HttpContext context, ILogger<Program> logger) =>
{
    if (store.Get(id) is not { } claim)
    {
        return Results.NotFound(new { code = "CLAIM_NOT_FOUND", message = "Demo claim not found. Refresh the workbench after a restart." });
    }
    if (claim.Status == "In human review")
    {
        return Results.Ok(claim);
    }
    if (!router.TryResolveDestination(out var destination))
    {
        logger.LogError(
            "CLAIM_ROUTING_UNAVAILABLE claimId={ClaimId} version={Version} destination={Destination} reason=DEMO_CONFIGURATION_REGRESSION simulation=true",
            id, router.Version, router.Destination);
        return Results.Json(new
        {
            code = "CLAIM_ROUTING_UNAVAILABLE",
            message = "Claim intake is available, but routing to a human reviewer is temporarily unavailable.",
            claimId = id,
            correlationId = context.Response.Headers["X-Correlation-ID"].ToString(),
            simulation = true
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    var routed = store.Route(id, destination);
    logger.LogInformation("Claim routed for human review: claimId={ClaimId} version={Version} destination={Destination}",
        id, router.Version, destination);
    return Results.Ok(routed);
});

app.Run();

public partial class Program;
