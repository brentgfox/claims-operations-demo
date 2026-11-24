# Claims Operations Workbench

A fictional, synthetic claims intake and operations demo for Azure SRE scenarios.
Not affiliated with WorkSafeBC or any claims authority. This is **not a production
claims system** and does not decide eligibility, compensation, fault, or benefits.

## What it demonstrates

- Claims dashboard with intake, awaiting-routing, and human-review states.
- New synthetic intake with explicit input validation.
- A deterministic routing regression, JSON console evidence, correlation IDs,
  and a reversible configuration mitigation.
- Two independent containers: a .NET 10 API and an nginx static frontend.

No external claims service, payment gateway, or real queue is called. All data and
destinations are fictional. Never enter personal, customer, or medical information.
The public API is intentionally unauthenticated for this isolated synthetic demo.
Before any production use, add authentication, authorization, persistence,
audit controls, data protection, network restrictions, and operational review.

## Visual accessibility

The frontend uses a high-contrast blue/amber palette, with ivory surfaces in light
mode and navy surfaces in dark mode. Pending/error states have dashed borders;
completed/review states use solid blue borders. Status and error text remain
explicit, so color is never the only indicator. Theme follows the device setting,
or can be selected with `?scoutTheme=light` or `?scoutTheme=dark`.

## Local development

Requirements: .NET 10 SDK. No Node runtime is needed.

```powershell
dotnet test .\tests\ClaimsApi.Tests\ClaimsApi.Tests.csproj
dotnet run --project .\src\ClaimsApi --urls http://localhost:5080
```

API: `GET /health`, `GET /api/demo`, `GET /api/claims`,
`GET /api/claims/{id}`, `POST /api/claims`, `POST /api/claims/{id}/route`.

For frontend development, serve `src/ClaimsFrontend` with a same-origin proxy
for `/api/` to the API. In Azure, nginx takes the backend hostname from
`CLAIMS_API_HOST`, enables upstream TLS server-name indication, and proxies `/api/`.

## Break, investigate, recover

The API setting `CLAIMS_API_VERSION` accepts `v1` or `v2` only.

| Mode | Routing destination | Result |
| --- | --- | --- |
| v1 | claims-human-review | HTTP 200; assigned for human review |
| v2 | legacy-claims-disabled | HTTP 503; CLAIM_ROUTING_UNAVAILABLE |

The failure is a deliberately simulated configuration regression, **not an
actual unavailable external queue**. Failed routing does not change claim status.
`/health` and intake still work; a healthy process probe does not prove the
business operation is available.

Set v2 through Azure Container Apps environment settings to reproduce. Investigate
the current revision/configuration, error logs and `src/ClaimsApi/Claims.cs` and
`Program.cs`. Restore v1 to mitigate; test routing, not just `/health`.

**State is in-memory.** A restart or revision rollout resets all records. Use
seeded claim **CLM-2026-0042** across revision changes. Refresh after switching
modes. Newly submitted IDs do not survive a restart. This demo does not demonstrate
durable recovery or production data-loss guarantees.

## Azure deployment

`infra/main.bicep` targets an **existing resource group**, Container Apps environment,
ACR, and Azure Monitor action group. Parameters have no tenant-specific values.
API and frontend use one replica each to keep this in-memory demo consistent.

1. Build `claims-api:<imageTag>` and `claims-frontend:<imageTag>` in the existing ACR.
2. Validate Bicep, Azure policy, scope, and what-if.
3. Deploy with `provisionApplications=false` to create a dedicated pull identity
   and ACR-scoped `AcrPull` assignment.
4. Confirm that assignment is visible before deploying application images.
5. Deploy with `provisionApplications=true`. Use the returned HTTPS URLs.
6. Test v1 intake/routing, v2 failure, and v1 routing recovery.
7. Configure SRE knowledge/agents/repository context and then enable the new alert.

Supply `environmentName`, `registryName`, `actionGroupName`, and policy-required
resource tags from your environment. Default container names are `ca-claims-api`
and `ca-claims-frontend`. The alert `alert-claims-routing-5xx` starts disabled,
evaluates every minute, and fires on **more than five** API 5xx requests in five
minutes. Do not enable it until agent instructions and response plans target this
application. Existing apps and automation are not changed by this template.

Example JSON console log query (confirm your workspace schema first):

```kusto
ContainerAppConsoleLogs_CL
| where TimeGenerated > ago(30m)
| where ContainerAppName_s == "ca-claims-api"
| where Log_s contains "CLAIM_ROUTING_UNAVAILABLE"
| project TimeGenerated, ContainerAppName_s, RevisionName_s, Log_s
| order by TimeGenerated desc
```

Logs include synthetic claim IDs, routing version/destination, and correlation
scope. Never treat this sample as compliance, residency, or access-control proof
for a real claims workload.
