# Third-party dependencies

| Dependency | Version/source | Usage |
| --- | --- | --- |
| OblivionMP / ReadyM SDK and host assemblies | Supplied separately; manifest minimum SDK 0.2.0 | Mod loading, RPC, input and entities. |
| LiteNetLib | 1.3.5 | Compile-time reference; runtime supplied by host. |
| Microsoft.Extensions.Logging.Abstractions | 10.0.8 | Logging; runtime supplied by host. |
| Concentus | 2.2.2 | Managed Opus codec shipped with client. |
| NAudio.Core | 3.0.1 | Audio processing shipped with client. |
| NAudio.Wasapi | 3.0.1 | Windows audio shipped with client. |
| Transitive NuGet dependencies | Resolved by NuGet | Non-host client DLLs are included by the build script; its packaging check requires System.Numerics.Tensors.dll. |

SDK binaries are excluded from the source repository. Preserve the applicable upstream licenses and notices when distributing dependencies in compiled releases. No project license was included in the supplied source archive.
