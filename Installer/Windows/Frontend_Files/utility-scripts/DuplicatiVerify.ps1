#Requires -Version 5.1
param
(
	[Parameter(Mandatory = $true)]
	[ValidateNotNullOrEmpty()]
	[string]$FileOrDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

enum RemoteVolumeState
{
        # Indicates that the remote volume is being created
        Temporary
        # Indicates that the remote volume is being uploaded
        Uploading
        # Indicates that the remote volume has been uploaded
        Uploaded
        # Indicates that the remote volume has been uploaded, and seen by a list operation
        Verified
        # Indicattes that the remote volume should be deleted
        Deleting
        # Indicates that the remote volume was successfully deleted from the remote location
        Deleted
}

enum RemoteVolumeType
{
    # Contains data blocks
    Blocks
    # Contains file lists
    Files
    # Contains redundant lookup information
    Index
}

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
		[System.IO.FileInfo]$filename
	)
	
    [int]$errorCount = 0
    [int]$checked = 0
    $remoteVolumes = $((Get-Content $filename) -Join "`n" | ConvertFrom-Json) | Sort-Object Name

    $statsState = @{}
    foreach ($n in 0..5) { $statsState.Add($n, $(New-Object PSObject -Property @{SumSize=0;SumCount=0})) }
    $statsType = @{}
    foreach ($n in 0..2) { $statsType.Add($n, $(New-Object PSObject -Property @{SumSize=0;SumCount=0})) }
    
    foreach ($remoteVolume in $remoteVolumes) {
        [string]$volFileName = $remoteVolume.Name
        [string]$volFilePath = Join-Path -Path $filename.DirectoryName -ChildPath $volFileName

        if(-not $(Test-Path $volFilePath)) {
            if($remoteVolume.State -eq $([RemoteVolumeState]::Deleted -as [int])) {
                $checked++
                $statsState[$($remoteVolume.State)].SumCount += 1
                $statsState[$($remoteVolume.State)].SumSize += $remoteVolume.Size
                $statsType[$($remoteVolume.Type)].SumCount += 1
                $statsType[$($remoteVolume.Type)].SumSize += $remoteVolume.Size
            } else {
                Write-Host "File missing: $volFileName" -ForegroundColor Red
                $errorCount++
            }
        } else {
            Write-Host "Verifying file $volFileName"

            $checked++
            $statsState[$($remoteVolume.State)].SumCount += 1
            $statsState[$($remoteVolume.State)].SumSize += $remoteVolume.Size
            $statsType[$($remoteVolume.Type)].SumCount += 1
            $statsType[$($remoteVolume.Type)].SumSize += $remoteVolume.Size

            $shaHash = Get-FileHash -Path $volFilePath -Algorithm SHA256

            if($remoteVolume.Hash -ne $(HexToBase64 $shaHash.Hash)) {
                Write-Host "*** Hash check failed for file: $volFileName" -ForegroundColor Red
                $errorCount++
            }
        }
    }

    [string]$prefix = $filename.Name -replace "-verification.json", ""
    [array]$filesInStorage = Get-ChildItem $(Join-Path $filename.DirectoryName "$prefix*") -Exclude $filename.Name -File | `
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

    Write-Host "`nSTATISTICS:"

    foreach ($n in 0..5) {
        Write-Host "`t$([Enum]::ToObject([RemoteVolumeState], $n)) - Count $($statsState[$n].SumCount), Size $("{0:n1} GB" -f ($statsState[$n].SumSize / 1gb))"
    }
    
    Write-Host 

    foreach ($n in 0..2) {
        Write-Host "`t$([Enum]::ToObject([RemoteVolumeType], $n)) - Count $($statsType[$n].SumCount), Size $("{0:n1} GB" -f ($statsType[$n].SumSize / 1gb))"
    }

    Write-Host 

    return $errorCount
}

if(-not $(Test-Path $FileOrDir)) {
    Write-Host "No such file or directory: $FileOrDir" -ForegroundColor Red
    exit 1
}

if(Test-Path $FileOrDir -PathType Leaf) {
    exit $(Verify-Hashes -filename $FileOrDir)
}

$verFiles = @(Get-ChildItem $FileOrDir -Filter "*-verification.json" -File)

if ($verFiles.Count -eq 0) { 
    Write-Host "No verification files in folder: $FileOrDir"
    exit 1
}

[int]$errorCount = 0

foreach ($verFile in $verFiles) {
    Write-Host "Verifying file: $($verFile.Name)"
    $errorCount += $(Verify-Hashes -filename $verFile.FullName)
}
        
exit $errorCount