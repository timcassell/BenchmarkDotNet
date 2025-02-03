param (
    [string]$PackageVersion
)

$packageId = "BenchmarkDotNet.Weaver"
$scriptpath = $MyInvocation.MyCommand.Path
$dir = Split-Path $scriptpath
$weaverDir = [System.IO.Path]::Combine($dir, "..\src\$packageId")
$weaverProj = [System.IO.Path]::Combine($weaverDir, "$packageId.csproj")

dotnet clean $weaverProj --nodeReuse:false
dotnet build $weaverProj -c Release --nodeReuse:false
$packageDir = [System.IO.Path]::Combine($weaverDir, "packages")
dotnet pack $weaverProj -c Release --nodeReuse:false --output $packageDir

# Clear nuget cache of existing package if it exists.
$nugetCachePaths = dotnet nuget locals all --list | ForEach-Object {
    $_.Trim() -replace "http-cache:", "" -replace "global-packages:", "" -replace "plugins-cache:", "" -replace "temp:", "" -replace "nuget.config", ""
}

foreach ($cachePath in $nugetCachePaths) {
    $cachePath = $cachePath.Trim()

    # Construct the package path
    $packagePath = [System.IO.Path]::Combine($cachePath, $packageId, $PackageVersion)
    Write-Output "Constructed package path: $packagePath"

    # Check if the package path exists and remove it
    if (Test-Path -Path $packagePath) {
        Remove-Item -Path $packagePath -Recurse -Force
        Write-Output "Cleared cache for package $packageId version $PackageVersion from $cachePath"
    } else {
        Write-Output "Package $packageId version $PackageVersion not found in cache $cachePath"
    }
}