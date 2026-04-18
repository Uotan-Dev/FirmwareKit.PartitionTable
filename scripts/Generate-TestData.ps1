param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\FirmwareKit.PartitionTable.Tests\test-gpt-4096.bin")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-UInt32LE {
    param(
        [byte[]]$Buffer,
        [int]$Offset,
        [uint32]$Value
    )

    $Buffer[$Offset] = [byte]($Value -band 0xFF)
    $Buffer[$Offset + 1] = [byte](($Value -shr 8) -band 0xFF)
    $Buffer[$Offset + 2] = [byte](($Value -shr 16) -band 0xFF)
    $Buffer[$Offset + 3] = [byte](($Value -shr 24) -band 0xFF)
}

function Write-UInt64LE {
    param(
        [byte[]]$Buffer,
        [int]$Offset,
        [uint64]$Value
    )

    Write-UInt32LE -Buffer $Buffer -Offset $Offset -Value ([uint32]$Value)
    Write-UInt32LE -Buffer $Buffer -Offset ($Offset + 4) -Value ([uint32]($Value -shr 32))
}

function Initialize-Crc32Table {
    $table = New-Object uint32[] 256
    for ($i = 0; $i -lt 256; $i++) {
        $crc = [uint32]$i
        for ($bit = 0; $bit -lt 8; $bit++) {
            if (($crc -band 1) -eq 1) {
                $crc = ($crc -shr 1) -bxor [uint32]3988292384
            }
            else {
                $crc = $crc -shr 1
            }
        }
        $table[$i] = $crc
    }

    return $table
}

function Compute-Crc32 {
    param(
        [byte[]]$Buffer,
        [int]$Offset,
        [int]$Length,
        [uint32[]]$Table
    )

    $crc = [uint32]4294967295
    for ($i = $Offset; $i -lt ($Offset + $Length); $i++) {
        $index = [byte](($crc -band 0xFF) -bxor $Buffer[$i])
        $crc = ($crc -shr 8) -bxor $Table[$index]
    }

    return [uint32](-bnot $crc)
}

function New-GptSampleImage {
    param(
        [int]$SectorSize = 4096
    )

    $totalSectors = 128
    $image = New-Object byte[] ($SectorSize * $totalSectors)
    $entryBuffer = New-Object byte[] (128 * 4)
    $crcTable = Initialize-Crc32Table

    $partitionType = [Guid]::Parse('EBD0A0A2-B9E5-4433-87C0-68B6B72699C7')
    $partitionId = [Guid]::Parse('11111111-2222-3333-4444-555555555555')
    $diskGuid = [Guid]::Parse('AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE')

    $partitionType.ToByteArray().CopyTo($entryBuffer, 0)
    $partitionId.ToByteArray().CopyTo($entryBuffer, 16)
    Write-UInt64LE -Buffer $entryBuffer -Offset 32 -Value 2048
    Write-UInt64LE -Buffer $entryBuffer -Offset 40 -Value 4095
    Write-UInt64LE -Buffer $entryBuffer -Offset 48 -Value 0
    [Text.Encoding]::Unicode.GetBytes('DATA').CopyTo($entryBuffer, 56)

    $entryCrc32 = Compute-Crc32 -Buffer $entryBuffer -Offset 0 -Length $entryBuffer.Length -Table $crcTable

    $image[510] = 0x55
    $image[511] = 0xAA
    $image[450] = 0xEE
    Write-UInt32LE -Buffer $image -Offset 454 -Value 1
    Write-UInt32LE -Buffer $image -Offset 458 -Value ([uint32]::MaxValue)

    $header = New-Object byte[] $SectorSize
    [Text.Encoding]::ASCII.GetBytes('EFI PART').CopyTo($header, 0)
    Write-UInt32LE -Buffer $header -Offset 8 -Value 0x00010000
    Write-UInt32LE -Buffer $header -Offset 12 -Value 92
    Write-UInt32LE -Buffer $header -Offset 16 -Value 0
    Write-UInt32LE -Buffer $header -Offset 20 -Value 0
    Write-UInt64LE -Buffer $header -Offset 24 -Value 1
    Write-UInt64LE -Buffer $header -Offset 32 -Value 127
    Write-UInt64LE -Buffer $header -Offset 40 -Value 34
    Write-UInt64LE -Buffer $header -Offset 48 -Value 125
    $diskGuid.ToByteArray().CopyTo($header, 56)
    Write-UInt64LE -Buffer $header -Offset 72 -Value 2
    Write-UInt32LE -Buffer $header -Offset 80 -Value 4
    Write-UInt32LE -Buffer $header -Offset 84 -Value 128
    Write-UInt32LE -Buffer $header -Offset 88 -Value $entryCrc32

    $headerPrefix = New-Object byte[] 92
    [Array]::Copy($header, 0, $headerPrefix, 0, 92)
    $headerCrc32 = Compute-Crc32 -Buffer $headerPrefix -Offset 0 -Length 92 -Table $crcTable
    Write-UInt32LE -Buffer $header -Offset 16 -Value $headerCrc32

    [Array]::Copy($header, 0, $image, $SectorSize, $header.Length)
    [Array]::Copy($entryBuffer, 0, $image, $SectorSize * 2, $entryBuffer.Length)
    [Array]::Copy($entryBuffer, 0, $image, $SectorSize * 126, $entryBuffer.Length)

    $backupHeader = New-Object byte[] $SectorSize
    [Array]::Copy($header, 0, $backupHeader, 0, $SectorSize)
    Write-UInt64LE -Buffer $backupHeader -Offset 24 -Value 127
    Write-UInt64LE -Buffer $backupHeader -Offset 32 -Value 1
    Write-UInt64LE -Buffer $backupHeader -Offset 72 -Value 126
    $backupHeaderPrefix = New-Object byte[] 92
    [Array]::Copy($backupHeader, 0, $backupHeaderPrefix, 0, 92)
    $backupHeaderCrc32 = Compute-Crc32 -Buffer $backupHeaderPrefix -Offset 0 -Length 92 -Table $crcTable
    Write-UInt32LE -Buffer $backupHeader -Offset 16 -Value $backupHeaderCrc32
    [Array]::Copy($backupHeader, 0, $image, $SectorSize * 127, $backupHeader.Length)

    return $image
}

$image = New-GptSampleImage -SectorSize 4096
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputPath)) | Out-Null
[IO.File]::WriteAllBytes($OutputPath, $image)
Write-Host "Wrote $OutputPath"
