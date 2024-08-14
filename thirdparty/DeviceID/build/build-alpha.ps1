$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$output = "$root/artifacts"
$projects = @(
    "$root/src/DeviceId/DeviceId.csproj",
    "$root/src/DeviceId.Windows/DeviceId.Windows.csproj",
    "$root/src/DeviceId.Windows.Wmi/DeviceId.Windows.Wmi.csproj",
    "$root/src/DeviceId.Windows.Mmi/DeviceId.Windows.Mmi.csproj",
    "$root/src/DeviceId.SqlServer/DeviceId.SqlServer.csproj",
    "$root/src/DeviceId.Linux/DeviceId.Linux.csproj",
    "$root/src/DeviceId.Mac/DeviceId.Mac.csproj"
)

$timestamp = git log -1 --format=%ct

foreach ($project in $projects) {
    dotnet pack $project --configuration Release --output $output --version-suffix "alpha.$timestamp" -p:ContinuousIntegrationBuild=true
}
