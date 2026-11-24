using System.Net;
using System.Net.Http.Json;
using ClaimsApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ClaimsApi.Tests;

public sealed class ClaimsFlowTests
{
    [Fact]
    public async Task IntakeAndRoutingInV1ReturnClaimForHumanReview()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var intake = await client.PostAsJsonAsync("/api/claims",
            new ClaimInput("DEMO-W1999", "Demo Employer", "2026-01-01", "Manual handling", "Synthetic example."));
        Assert.Equal(HttpStatusCode.Created, intake.StatusCode);
        var claim = await intake.Content.ReadFromJsonAsync<Claim>();
        Assert.NotNull(claim);
        Assert.Equal("Awaiting routing", claim.Status);
        var route = await client.PostAsync($"/api/claims/{claim.Id}/route", null);
        Assert.Equal(HttpStatusCode.OK, route.StatusCode);
        var routed = await route.Content.ReadFromJsonAsync<Claim>();
        Assert.NotNull(routed);
        Assert.Equal("In human review", routed.Status);
        Assert.Equal("claims-human-review", routed.ReviewQueue);
        var repeat = await client.PostAsync($"/api/claims/{claim.Id}/route", null);
        Assert.Equal(routed.RoutedAt, (await repeat.Content.ReadFromJsonAsync<Claim>())!.RoutedAt);
    }

    [Fact]
    public async Task V2FailsRoutingWithoutLosingClaimAndReturnsCorrelation()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("CLAIMS_API_VERSION", "v2"));
        using var client = factory.CreateClient();
        var response = await client.PostAsync("/api/claims/CLM-2026-0042/route", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("CLAIM_ROUTING_UNAVAILABLE", body.GetProperty("code").GetString());
        Assert.Equal(response.Headers.GetValues("X-Correlation-ID").Single(), body.GetProperty("correlationId").GetString());
        var claim = await client.GetFromJsonAsync<Claim>("/api/claims/CLM-2026-0042");
        Assert.Equal("Awaiting routing", claim!.Status);
        Assert.Null(claim.ReviewQueue);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task InvalidIntakeAndUnknownClaimReturnExplicitErrors()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var invalid = await client.PostAsJsonAsync("/api/claims", new ClaimInput("", "", "bad-date", "unknown", ""));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var problem = await invalid.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(problem.GetProperty("errors").EnumerateObject().Count() >= 4);
        var missing = await client.PostAsync("/api/claims/not-a-claim/route", null);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
