# Organize Published folder - move DLLs to lib subfolder
param(
    [string]$PublishPath = ".\Published"
)

Write-Host "Organizing published files in: $PublishPath"

# Create lib subfolder
$libPath = Join-Path $PublishPath "lib"
if (-not (Test-Path $libPath)) {
    New-Item -ItemType Directory -Path $libPath | Out-Null
}

# Get all DLL files in the root Published folder
$dlls = Get-ChildItem -Path $PublishPath -Filter "*.dll" -File

foreach ($dll in $dlls) {
    $destination = Join-Path $libPath $dll.Name
    Move-Item -Path $dll.FullName -Destination $destination -Force
    Write-Host "Moved to lib: $($dll.Name)"
}

# Also move .pdb files if any
$pdbs = Get-ChildItem -Path $PublishPath -Filter "*.pdb" -File
foreach ($pdb in $pdbs) {
    $destination = Join-Path $libPath $pdb.Name
    Move-Item -Path $pdb.FullName -Destination $destination -Force
    Write-Host "Moved to lib: $($pdb.Name)"
}

# Move deps.json file
$depsFile = Join-Path $PublishPath "FamidashEditor.deps.json"
if (Test-Path $depsFile) {
    $destination = Join-Path $libPath "FamidashEditor.deps.json"
    Move-Item -Path $depsFile -Destination $destination -Force
    Write-Host "Moved to lib: FamidashEditor.deps.json"
}

Write-Host "`nOrganization complete!"
Write-Host "`nRoot files:"
Get-ChildItem -Path $PublishPath -File | Select-Object Name, @{Name="Size";Expression={"{0:N2} MB" -f ($_.Length / 1MB)}} | Format-Table -AutoSize

Write-Host "`nLib folder files: $((Get-ChildItem -Path $libPath -File).Count)"
