# Claims routing incident runbook

This fictional demo uses synthetic records only. No medical/customer information,
benefits decision, or real claims backend is involved.

## Observable symptom

Dashboard and intake work. `POST /api/claims/{id}/route` returns HTTP 503 with
`CLAIM_ROUTING_UNAVAILABLE`. Intake remains Awaiting routing in the current
process. `/health` can still return 200.

## Investigation

1. Confirm the application, resource group, current active revision, image, and
   `CLAIMS_API_VERSION` from real Azure tool output.
2. Query recent Container Apps console logs. Confirm actual workspace/table
   schema; `ContainerAppConsoleLogs_CL` usually stores JSON console payload inside
   `Log_s`. Preserve timestamps, revision and correlation identifiers.
3. Look for `CLAIM_ROUTING_UNAVAILABLE`, `version=v2`,
   `destination=legacy-claims-disabled`, and `DEMO_CONFIGURATION_REGRESSION`.
4. Inspect `src/ClaimsApi/Claims.cs`, `ClaimRoutingService`, and
   `src/ClaimsApi/Program.cs` in the connected claims repository. v2 selects a
   retired simulated destination; only claims-human-review resolves successfully.
5. Do not claim an actual queue outage or external dependency call. Do not infer
   durable data retention from this in-memory demo.

## Mitigation, only when explicitly authorized

Restore `CLAIMS_API_VERSION=v1` on the claims API container only. Do not modify the
frontend, Grubify fallback, identities, permissions, resource group, or other apps.
Changing this setting creates a new revision and resets in-memory records.
Wait until `/api/demo` actually reports v1; the CLI update can return while the
previous revision is still serving traffic. Refresh the workbench.

## Verification

Use seeded claim CLM-2026-0042, not an ID created before the revision change.
Verify a routing POST returns 200, status In human review, destination
claims-human-review. Check recent logs and active revision. A green homepage,
healthy probe, or successful configuration command alone is insufficient.
Human review assignment is not a benefits decision.

## Communication

Separate observations, hypotheses, source-code evidence, mitigation and measured
verification. Include timestamps, synthetic correlation identifiers, affected
business operation, scope, and in-memory-data limitation. Ask before creating a
GitHub incident issue or changing cloud resources in a manual demo conversation.
