# Contributing to PadesSharp

Thank you for helping improve PadesSharp.

## Source and licensing rules

- Do not copy code from iText 5, iText 7, or any AGPL/commercial source.
- Preserve original copyright and license notices in `LegacyPdfCore`.
- New modern implementations must be based on public standards or compatible
  permissively licensed sources with clear attribution.
- Never commit secrets, private keys, certificates, passwords, or token PINs.

## Development expectations

- Add tests for new behavior and every new public API.
- Add XML documentation to public APIs.
- Avoid mutable global state.
- Dispose streams, certificates, sessions, and cryptographic objects correctly.
- Run build and tests before submitting a pull request.

```bash
dotnet build PadesSharp.sln --configuration Release
dotnet test PadesSharp.sln --configuration Release --no-build
```

## Pull requests

1. Fork the repository and create a focused branch.
2. Keep unrelated formatting or generated-file changes out of the pull request.
3. Update documentation and the changelog when behavior changes.
4. Explain security, compatibility, and API-breaking implications.
5. Open the pull request against `main` and wait for CI and review.

By contributing, you confirm that you have the right to submit the work under the
project license.
