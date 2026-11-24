using System.Globalization;
using System.Text.RegularExpressions;

namespace ClaimsApi;

public sealed record ClaimInput(
    string? WorkerReference,
    string? Employer,
    string? IncidentDate,
    string? IncidentCategory,
    string? Summary)
{
    public static readonly string[] Categories = ["Manual handling", "Equipment contact", "Slip or trip", "Other workplace incident"];

    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (WorkerReference is null || !Regex.IsMatch(WorkerReference, @"^DEMO-W\d{4}$"))
            errors[nameof(WorkerReference)] = ["Use a synthetic worker reference such as DEMO-W1042."];
        if (string.IsNullOrWhiteSpace(Employer) || Employer.Length > 80)
            errors[nameof(Employer)] = ["Enter a fictional employer name, up to 80 characters."];
        if (!DateOnly.TryParseExact(IncidentDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || date > DateOnly.FromDateTime(DateTime.UtcNow))
            errors[nameof(IncidentDate)] = ["Enter a valid incident date that is not in the future."];
        if (IncidentCategory is null || !Categories.Contains(IncidentCategory))
            errors[nameof(IncidentCategory)] = ["Choose one of the listed incident categories."];
        if (string.IsNullOrWhiteSpace(Summary) || Summary.Length > 160)
            errors[nameof(Summary)] = ["Enter a synthetic summary, up to 160 characters. Do not enter personal or medical information."];
        return errors;
    }
}

public sealed record Claim(
    string Id,
    string WorkerReference,
    string Employer,
    string IncidentDate,
    string IncidentCategory,
    string Summary,
    string Status,
    DateTimeOffset SubmittedAt,
    string? ReviewQueue = null,
    DateTimeOffset? RoutedAt = null);

public sealed class ClaimRoutingService
{
    public string Version { get; }
    public string Destination { get; }

    public ClaimRoutingService(string version)
    {
        if (version is not ("v1" or "v2"))
            throw new InvalidOperationException("CLAIMS_API_VERSION must be v1 or v2.");
        Version = version;
        Destination = version == "v2" ? "legacy-claims-disabled" : "claims-human-review";
    }

    public bool TryResolveDestination(out string destination)
    {
        // The lab regression selects a retired queue. No external claims service is contacted.
        destination = Destination;
        return Destination == "claims-human-review";
    }
}

public sealed class ClaimStore
{
    private readonly object gate = new();
    private readonly Dictionary<string, Claim> claims = new(StringComparer.Ordinal);

    public ClaimStore()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in new[]
        {
            ("CLM-2026-0042", "DEMO-W1042", "Demo Manufacturing", "Equipment contact", "Synthetic intake awaiting assignment to a human reviewer."),
            ("CLM-2026-0043", "DEMO-W1043", "Demo Logistics", "Manual handling", "Fictional warehouse incident submitted for review."),
            ("CLM-2026-0044", "DEMO-W1044", "Demo Facilities", "Slip or trip", "Synthetic incident report for demonstration only."),
            ("CLM-2026-0045", "DEMO-W1045", "Demo Construction", "Other workplace incident", "Fictional workplace report awaiting routing."),
            ("CLM-2026-0046", "DEMO-W1046", "Demo Services", "Manual handling", "Synthetic example already assigned for human review.")
        })
        {
            claims[entry.Item1] = new Claim(entry.Item1, entry.Item2, entry.Item3,
                now.AddDays(-1).ToString("yyyy-MM-dd"), entry.Item4, entry.Item5,
                entry.Item1.EndsWith("46", StringComparison.Ordinal) ? "In human review" : "Awaiting routing",
                now.AddMinutes(-30),
                entry.Item1.EndsWith("46", StringComparison.Ordinal) ? "claims-human-review" : null,
                entry.Item1.EndsWith("46", StringComparison.Ordinal) ? now.AddMinutes(-20) : null);
        }
    }

    public Claim[] List()
    {
        lock (gate) return claims.Values.OrderByDescending(claim => claim.SubmittedAt).ThenBy(claim => claim.Id).ToArray();
    }

    public Claim? Get(string id)
    {
        lock (gate) return claims.GetValueOrDefault(id);
    }

    public Claim Add(ClaimInput input)
    {
        var claim = new Claim($"CLM-DEMO-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            input.WorkerReference!, input.Employer!.Trim(), input.IncidentDate!,
            input.IncidentCategory!, input.Summary!.Trim(), "Awaiting routing", DateTimeOffset.UtcNow);
        lock (gate) claims.Add(claim.Id, claim);
        return claim;
    }

    public Claim Route(string id, string destination)
    {
        lock (gate)
        {
            var claim = claims[id];
            if (claim.Status == "In human review") return claim;
            return claims[id] = claim with
            {
                Status = "In human review",
                ReviewQueue = destination,
                RoutedAt = DateTimeOffset.UtcNow
            };
        }
    }
}
