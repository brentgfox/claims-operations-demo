# Claims incident handoff template

## Summary
Synthetic claims routing incident; describe measured impact, not inferred impact.

## Timeline and evidence
UTC timestamps, current revision/image, API version, HTTP status, error code,
synthetic claim/correlation identifiers, relevant tool output and source links.

## Root cause
Separate confirmed source-code mechanism from hypotheses and missing evidence.
The v2 scenario is a simulated retired routing destination, not a real queue outage.

## Mitigation and verification
Record approved changes and scope, serving `/api/demo` version, actual routing
POST result and review status. Mention in-memory reseeding on rollout.

## Follow-up
Engineering owner, corrective action, regression test and unresolved questions.
Do not imply this demo provides production claims resilience or compliance.
