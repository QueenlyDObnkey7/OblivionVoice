$modName = "OblivionVoice"

$clientProject = "OblivionVoice.Client"
$serverProject = "OblivionVoice.Server"

$clientBuildFiles = @(
    "OblivionVoice.Client.dll",
    "OblivionVoice.Common.dll",
    "Concentus.dll",
    "NAudio.Core.dll",
    "NAudio.Wasapi.dll"
)

$serverBuildFiles = @(
    "OblivionVoice.Server.dll",
    "OblivionVoice.Common.dll"
)

$manifestFiles = @(
    "manifest.json"
)

$clientContentFiles = @(
)

$serverContentFiles = @(
    "voiceconfig.json"
)

$clientDebugBuildFiles = @(
    "OblivionVoice.Client.pdb",
    "OblivionVoice.Common.pdb"
)

$serverDebugBuildFiles = @(
    "OblivionVoice.Server.pdb",
    "OblivionVoice.Common.pdb"
)
