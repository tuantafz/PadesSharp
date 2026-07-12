# Demo Application: Verification Workflow

## Objective

Add a verification workflow to the Windows Forms demo without presenting a single
ambiguous “valid” label. The UI must distinguish document integrity, CMS signature
validity, certificate trust, timestamp status, and revocation status.

## Proposed interface

- Add separate **Sign** and **Verify** tabs.
- Allow batch selection of PDF files.
- Show one result row per file and one detail row per signature.
- Provide filters or policy presets for integrity-only and strict validation.
- Reuse the existing language selector, status bar, and diagnostic console.
- Prefix diagnostic messages with `[SIGN]` or `[VERIFY]`.

## Result model

Display at least:

- Signature field name and signer identity
- Signed revision and modification status
- CMS cryptographic validity
- Certificate-chain validity and trust status
- Timestamp presence and validity
- Revocation status and evidence source
- Errors, warnings, and policy used for the decision

The banner should summarize the selected policy. Detailed fields must remain
visible so users can understand why a result passed or failed.

## Processing

1. Validate selected files outside the UI thread.
2. Map library results into immutable view models.
3. Update progress after each file.
4. Support cooperative cancellation between files.
5. Avoid logging PDF contents, certificates, tokens, credentials, or private data.

The current validator contains synchronous operations. Until cancellation flows
through the full API, the Stop action may mean “stop after the current file.” The
UI must describe that behavior accurately.

## Testing

- Unit-test policy-to-banner classification and result mapping without WinForms.
- Test unsigned, valid, modified, expired, untrusted, revoked, and timestamped PDFs.
- Test multiple signatures and batch cancellation.
- Do not rely on pixel-level UI tests for core validation behavior.

## Follow-up architecture

The current main form has many responsibilities. A later refactor may extract Sign
and Verify user controls and replace display-string matching with structured error
codes. Those changes should remain separate from the minimum verification workflow.
