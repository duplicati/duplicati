$apiKey = Read-Host -Prompt "Enter your nuget.org API key"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$output = "$root/artifacts"
foreach ($package in Get-ChildItem -Path $output -Filter "*.nupkg") {
    dotnet nuget push $package.FullName --source https://api.nuget.org/v3/index.json --api-key $apiKey --skip-duplicate
}
