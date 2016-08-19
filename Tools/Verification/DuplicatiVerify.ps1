Set-StrictMode -Version 5

function HexToBase64
{
    param
    (
        [Parameter(Mandatory=$true, Position=0, ValueFromPipeline=$true)]
        [string]$s
    )
    [array]$return = @()
 
    for ($i = 0; $i -lt $s.Length ; $i += 2) {
        $return += [Byte]::Parse($s.Substring($i, 2), [System.Globalization.NumberStyles]::HexNumber)
    }
 
    Write-Output $([Convert]::ToBase64String($return))
}

function Verify-Hashes
{
	[CmdletBinding(PositionalBinding=$false)]
	param
	(
		[Parameter(Mandatory=$true)]
		[ValidateNotNullOrEmpty()]
		[string]$filename
	)
	
    [int]$errorCount = 0
    [int]$checked = 0

    if(-not $(Test-Path $filename)) {
        Write-Host "Specified file does not exist: $filename"
        return -1
    }
    
    [string]$folder = Split-Path -Path $filename

    $remoteVolumes = (Get-Content $filename) -Join "`n" | ConvertFrom-Json

    foreach ($remoteVolume in $remoteVolumes) {
        [string]$volFileName = $remoteVolume.Name
        [long]$volFileSize = $remoteVolume.Size
        [string]$volFilePath = Join-Path -Path $folder -ChildPath $volFileName

        if(-not $(Test-Path $volFilePath)) {
            Write-Host "File missing: $volFilePath" -ForegroundColor Red
            $errorCount++
        } else {
            $checked++
        
            Write-Host "Verifying file $volFileName"

            $shaHash = Get-FileHash -Path $volFilePath -Algorithm SHA256

            if($remoteVolume.Hash -ne $(HexToBase64 $shaHash.Hash)) {
                Write-Host "*** Hash check failed for file: $volFileName" -ForegroundColor Red
                $errorCount++
            }
        }
    }

    if($errorCount -gt 0) {
        Write-Host "Errors were found" -ForegroundColor Red
    } else {
        Write-Host "No errors found" -ForegroundColor Green
    }

    return $errorCount
}

[string]$argument = ""
if ($args.Length -eq 1) { $argument = $args[0] }
else { $argument = $PSScriptRoot }


if(-not $(Test-Path $argument)) {
    Write-Host "No such file or directory: $argument" -ForegroundColor Red
}

if(Test-Path $argument -PathType Leaf) {
    Verify-Hashes -filename $argument
} else {
    [int] $files = 0

    Get-ChildItem $argument -Filter "*-verification.json" | 
    Foreach-Object {
        $files++
        Write-Host "Verifying file: $_.Name"
        Verify-Hashes -filename $_.FullName
    }

    if ($files -eq 0) { Write-Host "No verification files in folder: $argument" }
}