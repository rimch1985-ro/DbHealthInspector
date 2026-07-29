using System.Runtime.CompilerServices;

// Exposes the internal canonical-field encoding operation (see
// DbHealthInspector.Core.Fingerprinting.FindingFingerprintGenerator) to the unit test project
// only, so the null-vs-empty byte-level distinction in fingerprint canonicalization can be
// tested directly without weakening any public domain contract to hold an otherwise-invalid
// value. See docs/design/core-domain-contracts.md §9.5/§11.
[assembly: InternalsVisibleTo("DbHealthInspector.UnitTests")]
