Add-Type -AssemblyName System.IO.Compression.FileSystem

# Явный путь к проекту — поправь если переместил папку
$base    = [System.IO.Path]::GetDirectoryName($MyInvocation.MyCommand.Path)
$version = "1.0.0"
$tpm     = "$base\RoadStyle-$version.tpm"

if (Test-Path $tpm) { Remove-Item $tpm }

$zip = [System.IO.Compression.ZipFile]::Open($tpm, "Create")

# Манифест
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
    $zip, "$base\package.json", "package.json")

# DLL
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
    $zip, "$base\bin\Debug\net48\RoadStyle.dll", "bin/RoadStyle.dll")

# .plugin
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
    $zip, "$base\RoadStyle.plugin", "plugins/RoadStyle.plugin")

# Иконки — все PNG из папки Icons
$iconCount = 0
Get-ChildItem "$base\Icons\*.png" | ForEach-Object {
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $zip, $_.FullName, "icons/$($_.Name)")
    Write-Host "  + icons/$($_.Name)"
    $iconCount++
}

$zip.Dispose()

Write-Host ""
Write-Host "Иконок добавлено: $iconCount"

# Проверка содержимого
Write-Host ""
Write-Host "Содержимое $tpm :"
[System.IO.Compression.ZipFile]::OpenRead($tpm).Entries | ForEach-Object {
    Write-Host "  $($_.FullName)"
}
