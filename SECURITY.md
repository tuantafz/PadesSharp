# Security Policy

## Supported versions

PadesSharp is currently a public preview. No version is yet covered by a long-term
security support commitment. Security fixes are applied to the latest revision of
the `main` branch and included in the next preview release.

## Reporting a vulnerability

Please do not disclose suspected vulnerabilities in a public issue. Use GitHub's
private vulnerability reporting feature for this repository. Include affected
versions, a minimal reproducer, expected impact, and any suggested mitigation.

Maintainers will acknowledge a report within seven days and will coordinate a fix
and disclosure timeline according to severity.

## Scope and limitations

PDF signature validation is security-sensitive. During the preview period,
consumers must independently evaluate trust policy, revocation behavior, timestamp
validation, PDF parser compatibility, and interoperability for their environment.
Do not treat a single `IsValid` result as legal or regulatory assurance.
