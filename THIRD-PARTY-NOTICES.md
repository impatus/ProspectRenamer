# Third-party notices

Prospect Renamer includes source from the following projects. The original source remains under the Apache License, Version 2.0. Full license texts are included in each vendored directory and copied into published releases.

## IcarusSaveLib

- Project: <https://github.com/CrystalFerrai/IcarusSaveLib>
- Author: Crystal Ferrai
- Vendored commit: `599e0f1a5e6f06e1f4ad295766decab949bad0f2`
- Location: `third_party/IcarusSaveLib`
- License: Apache License 2.0

Integration note: the vendored `ProspectSave` uses .NET 8's built-in `System.IO.Compression.ZLibStream` in place of the upstream `Ionic.Zlib.Core` package. This keeps the same zlib prospect-blob format while avoiding an opaque package whose NuGet metadata does not declare a license or source repository.

## UeSaveGame

- Project: <https://github.com/CrystalFerrai/UeSaveGame>
- Author: Crystal Ferrai
- Vendored commit: `c716f8fcde5e01749893a5b2937e1169f98f29a3`
- Location: `third_party/UeSaveGame`
- License: Apache License 2.0

## Reference implementation studied

The focused rename behavior was confirmed against:

- Project: <https://github.com/CrystalFerrai/EditIcarusProspect>
- Inspected commit: `1d092af2b477a019933661ce199a2b3f9a305a3f`
- License: Apache License 2.0

No EditIcarusProspect application source is vendored. Its rename flow was used as the technical reference for locating and updating `ProspectInfo.ProspectID` in both representations of the prospect.

## NuGet package used by vendored projects

- `Newtonsoft.Json` 13.0.2 — JSON serialization used by `IcarusSaveLib`; MIT License, copyright James Newton-King.

Package license metadata is available through NuGet and is restored by the .NET SDK during builds.
