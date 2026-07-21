$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
#set "path=%path%;%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin"
$csproj = "$root\src\TrayToolbar\TrayToolbar.csproj"
$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version

$msbuild = Get-Command msbuild -ErrorAction SilentlyContinue

function Publish-Portable([string]$runtimeIdentifier)
{
    if ($msbuild)
    {
        & $msbuild.Source -t:Publish -p:RuntimeIdentifier=$runtimeIdentifier $csproj -p:PublishDir=${root}\publish -p:Configuration=Release -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:SelfContained=false -p:PublishProtocol=FileSystem
        return
    }

    dotnet publish $csproj -c Release -r $runtimeIdentifier -o ${root}\publish --self-contained false /p:PublishSingleFile=true /p:PublishReadyToRun=false
}

if ($msbuild)
{
    & $msbuild.Source -t:restore $csproj
}
else
{
    dotnet restore $csproj
}

Publish-Portable "win-arm64"
Compress-Archive "$root\publish\*.exe" "$root\TrayToolbar-win-arm64-portable-$version.zip" -Force

Publish-Portable "win-x64"
Compress-Archive "$root\publish\*.exe" "$root\TrayToolbar-win-x64-portable-$version.zip" -Force
