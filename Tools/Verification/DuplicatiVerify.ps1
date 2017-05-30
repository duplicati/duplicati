param
(
	[Parameter(Mandatory = $true)]
	[ValidateNotNullOrEmpty()]
	[string]$FileOrDir
)

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
            Write-Host "File missing: $volFileName" -ForegroundColor Red
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

    [string]$prefix = $(Split-Path -Path $filename -Leaf) -replace "-verification.json", ""
    [array]$filesInStorage = Get-ChildItem $folder -Include "$prefix*" -Exclude $(Split-Path -Path $filename -Leaf) | `
        Select-Object -ExpandProperty Name | Sort-Object
    [array]$filesInVerification = $remoteVolumes | Select-Object -ExpandProperty Name | Sort-Object


    $filesInStorage | Where-Object {$filesInVerification -NotContains $_} | ForEach-Object { 
        Write-Host "Found extra file which is not in verification file: $_" -ForegroundColor Red 
        $errorCount++
    }
    
    if($errorCount -gt 0) {
        Write-Host "`nErrors were found" -ForegroundColor Red
    } else {
        Write-Host "`nNo errors found" -ForegroundColor Green
    }

    return $errorCount
}

Set-StrictMode -Version Latest

if(-not $(Test-Path $FileOrDir)) {
    Write-Host "No such file or directory: $FileOrDir" -ForegroundColor Red
}

if(Test-Path $FileOrDir -PathType Leaf) {
    exit Verify-Hashes -filename $FileOrDir
} else {
    [int]$files = 0
    [int]$errorCount = 0

    Get-ChildItem $FileOrDir -Filter "*-verification.json" | 
    Foreach-Object {
        $files++
        Write-Host "Verifying file: $($_.Name)"
        $errorCount = $errorCount + $(Verify-Hashes -filename $_.FullName)
    }

    if ($files -eq 0) { Write-Host "No verification files in folder: $FileOrDir" }
    exit $errorCount
}