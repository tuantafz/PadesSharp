# Third-party notices

PadesSharp includes and depends on third-party software. This file supplements,
but does not replace, the license texts and copyright notices shipped with those
components.

## LegacyPdfCore / iTextSharp 4.1.6

`src/LegacyPdfCore` is a source fork of iTextSharp 4.1.6, licensed under the GNU
Lesser General Public License version 2.1 or later. PadesSharp changes include
multi-targeting and compatibility updates for current Bouncy Castle APIs. Original
copyright and license notices must be preserved when redistributing this code.

## NuGet dependencies

The project also references BouncyCastle.Cryptography, Pkcs11Interop,
Microsoft.Extensions packages, System.Drawing.Common, and test-only packages.
Their respective license terms apply. Before publishing a binary package, review
the resolved dependency versions and licenses in the generated dependency graph.

See [LICENSE](LICENSE) and [docs/license-provenance.md](docs/license-provenance.md).
