# Disk Partitioner for Windows - One-Click Build & Package Script
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " Building Disk Partitioner for Windows" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# 1. Publish Self-Contained Executable
Write-Host "`n[1/3] Publishing self-contained executable..." -ForegroundColor Yellow
Stop-Process -Name DiskPartitioner -Force -ErrorAction SilentlyContinue
if (Test-Path "./publish") {
    Remove-Item -Path "./publish/*" -Recurse -Force -ErrorAction SilentlyContinue
}
dotnet publish -c Release -r win-x64 --self-contained -p:PublishReadyToRun=false -o ./publish
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed."
    exit $LASTEXITCODE
}

# 2. Copy native assets & Optimize size
Write-Host "`n[2/3] Copying native assets and optimizing package size..." -ForegroundColor Yellow
Copy-Item "nvdaControllerClient.dll" "./publish/" -Force
if (-not (Test-Path "./publish/Resources")) {
    New-Item -ItemType Directory -Path "./publish/Resources" -Force | Out-Null
}
Copy-Item "Resources/app.ico" "./publish/Resources/" -Force

# Remove any foreign language satellite resource folders (leaves English intact)
Get-ChildItem -Path "./publish" -Directory | Where-Object { $_.Name -match '^[a-z]{2}(-[A-Za-z]+)?$' } | Remove-Item -Recurse -Force

# Remove truly unused enterprise/server/XML assemblies to optimize package size safely without touching any WPF runtime dependencies
$unusedAssemblies = @(
    "System.Data.*",
    "PresentationFramework-SystemData.dll",
    "System.Net.Quic.dll",
    "System.Net.WebSockets*.dll",
    "System.Net.Mail.dll",
    "System.Net.Requests.dll",
    "System.Net.Ping.dll",
    "System.Net.WebHeaderCollection.dll",
    "msquic.dll",
    "System.Printing.dll",
    "ReachFramework.dll",
    "PresentationUI.dll",
    "mscordbi.dll",
    "mscordaccore*.dll",
    "Microsoft.DiaSymReader*.dll",
    "System.DirectoryServices*.dll",
    "System.ServiceModel*.dll",
    "System.Transactions*.dll",
    "System.Web*.dll",
    "Microsoft.VisualBasic*.dll",
    "Microsoft.CSharp.dll",
    "PresentationFramework.Luna.dll",
    "PresentationFramework.Royale.dll",
    "PresentationFramework.Classic.dll",
    "PresentationFramework.Fluent.dll",
    "System.Windows.Controls.Ribbon.dll",
    "System.Private.Xml.dll",
    "System.Private.Xml.Linq.dll",
    "System.Xml.*",
    "System.Private.DataContractSerialization.dll",
    "System.Linq.Expressions.dll",
    "System.Linq.Parallel.dll",
    "System.Linq.AsyncEnumerable.dll",
    "System.Collections.Immutable.dll",
    "System.Diagnostics.EventLog*.dll",
    "clrgcexp.dll",
    "System.Runtime.InteropServices.JavaScript.dll",
    "System.Security.Cryptography.OpenSsl.dll",
    "System.Security.Cryptography.Pkcs.dll",
    "System.Security.Cryptography.Xml.dll",
    "System.Threading.Tasks.Dataflow.dll"
)
foreach ($pattern in $unusedAssemblies) {
    Remove-Item -Path "./publish/$pattern" -Force -ErrorAction SilentlyContinue
}

# 3. Compile Inno Setup Installer
Write-Host "`n[3/3] Compiling accessible setup installer..." -ForegroundColor Yellow
$isccPath = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $isccPath)) {
    $isccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
}

if (Test-Path $isccPath) {
    & $isccPath "Installer\setup.iss"
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`n==========================================" -ForegroundColor Green
        $setupFile = (Get-ChildItem "./Installer/Output/*.exe" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1)
        $targetDir = Join-Path $env:USERPROFILE "Music\Output"
        if (Test-Path $targetDir) {
            $copied = $false
            for ($attempt = 1; $attempt -le 15; $attempt++) {
                try {
                    Copy-Item $setupFile.FullName -Destination $targetDir -Force -ErrorAction Stop
                    $copied = $true
                    Break
                } catch {
                    Start-Sleep -Milliseconds 600
                }
            }
            if ($copied) {
                Write-Host " Output Folder: $targetDir\$($setupFile.Name)" -ForegroundColor Green
            } else {
                Write-Warning "Could not copy installer to $targetDir due to file lock."
            }
        }
        Write-Host "==========================================" -ForegroundColor Green
    }
} else {
    Write-Warning "Inno Setup compiler not found. Standalone EXE is ready in ./publish/DiskPartitioner.exe"
}
