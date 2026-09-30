# Security Policy

## Reporting a Vulnerability

Please **do not** open a public GitHub issue for security vulnerabilities,
including in a dependency.

Instead, use GitHub's private vulnerability reporting for this repository:
open the [Security tab](../../security) and click **"Report a vulnerability"**.
This opens a private advisory visible only to you and the maintainer, with no
public disclosure until a fix is ready.

If you're unable to use that flow, contact [@maik-hasler](https://github.com/maik-hasler)
directly via GitHub.

## What to Include

- A description of the vulnerability and its impact
- Steps to reproduce (a minimal example helps most)
- Affected component (backend, frontend, keycloak) and version/commit if known

## Response

This is a volunteer-run open source project without a fixed SLA, but reports
are reviewed as soon as possible. You'll get an acknowledgment, and a fix or
mitigation once the issue is understood. Credit is given in the fix's release
notes unless you'd prefer to stay anonymous.

## Scope

Use this policy for vulnerabilities in Afunto's own code (backend,
frontend, or the Keycloak realm configuration) as well as in a dependency -
there is currently no automated dependency vulnerability scan, so a report
is the only way a dependency CVE affecting this project gets noticed.

Static application security testing (CodeQL, `.github/workflows/codeql.yml`)
and container image scanning (Trivy, in `publish.yml`) both run, but neither
gates anything - a finding is uploaded to the [Security tab](../../security)
and nothing else, so a report is still the fastest way to get a vulnerability
looked at rather than waiting on either scan to surface it.

Known gap, not yet covered by any workflow: dependency vulnerability scanning
(no NuGet/npm audit step).
