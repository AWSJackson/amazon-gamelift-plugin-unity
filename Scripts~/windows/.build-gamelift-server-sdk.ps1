$ROOT_DIR="."
$RUNTIME_PATH="$ROOT_DIR\Runtime"
$RUNTIME_PLUGINS_PATH="$RUNTIME_PATH\Plugins"
$TEMP_PATH="C:\Temp"
$NUGET_EXE_PATH="$TEMP_PATH\nuget.exe"
$S3_SERVER_SDK_BUCKET="https://gamelift-server-sdk-release.s3.us-west-2.amazonaws.com/csharp"
$S3_SERVER_SDK_DOWNLOAD_LINK="$S3_SERVER_SDK_BUCKET/GameLift-CSharp-ServerSDK-5.1.0.zip"
$TEMP_ZIP_PATH = "$TEMP_PATH\GameLiftServerSDK.zip"
$TEMP_EXTRACTED_PATH = "$TEMP_PATH\GameLiftServerSDK"
$SDK_TARGET_FRAMEWORK="net462"
$SDK_PATH="$TEMP_EXTRACTED_PATH\src"
$SDK_CSPROJ_PATH="$SDK_PATH\src\GameLiftServerSDK\GameLiftServerSDK.csproj"
$SDK_SLN_PATH="$SDK_PATH\GameLiftServerSDK.sln"

if (-Not (Test-Path -Path $RUNTIME_PATH))
{
	echo "$RUNTIME_PATH is not found in the working directory. Make sure you are executing the script from the project root."
	exit 1
}

if ((Get-Command "dotnet" -ErrorAction SilentlyContinue) -eq $null) 
{ 
	Write-Host "Unable to find 'dotnet' executable in your PATH. Install a version of .NET Core (See README on recommendation)"
	exit 1
}

if (-Not (Test-Path -Path $TEMP_EXTRACTED_PATH) )
{
	if (-Not (Test-Path -Path $TEMP_ZIP_PATH) )
	{
		echo "Downloading GameLift Managed Servers SDK..."
		iwr $S3_SERVER_SDK_DOWNLOAD_LINK -OutFile $TEMP_ZIP_PATH
	} 
	else 
	{
		echo "GameLift Server SDK is already downloaded and stored at $TEMP_ZIP_PATH, skip downloading."
	}
	Expand-Archive -Force -LiteralPath $TEMP_ZIP_PATH -DestinationPath $TEMP_EXTRACTED_PATH

	echo "Updating Newtonsoft.Json version used to 13.0.1 for compatibility with Unity 2020+"

	(Get-Content $SDK_CSPROJ_PATH).replace('Newtonsoft.Json, Version=9.0.0.0, Culture=neutral, PublicKeyToken=30ad4fe6b2a6aeed, processorArchitecture=MSIL', 'Newtonsoft.Json') | Set-Content $SDK_CSPROJ_PATH
	(Get-Content $SDK_CSPROJ_PATH).replace('Newtonsoft.Json.9.0.1', 'Newtonsoft.Json.13.0.1') | Set-Content $SDK_CSPROJ_PATH
}
else
{
	echo "GameLIft Server SDK is already downloaded and configured at $TEMP_EXTRACTED_PATH, skipping configuration."
}

if (-Not (Test-Path -Path $NUGET_EXE_PATH) )
{
	echo "Downloading NuGet started"
	iwr https://dist.nuget.org/win-x86-commandline/latest/nuget.exe -OutFile $NUGET_EXE_PATH
	echo "Adding nuget.org source"
	C:\Temp\nuget.exe sources Add -Name "nuget.org" -Source https://api.nuget.org/v3/index.json
	C:\Temp\nuget.exe sources Enable -Name "nuget.org"
}
else
{
	echo "nuget.exe is already downloaded and stored at $NUGET_EXE_PATH, skip downloading."
}

echo "Building GameLift Server SDK..."
Invoke-Expression '& $NUGET_EXE_PATH restore "$SDK_SLN_PATH" -PackagesDirectory "$SDK_PATH\packages"'
dotnet build "$SDK_CSPROJ_PATH" --configuration Release --output "$RUNTIME_PLUGINS_PATH" --framework "$SDK_TARGET_FRAMEWORK"

# Newtonsoft.json is deleted in favor of the Newtonsoft.json dependency in Unity. See "com.unity.nuget.newtonsoft-json: x.x.x" dependency in package.json.

if (Test-Path -Path $RUNTIME_PLUGINS_PATH\Newtonsoft.Json.dll)
{
	del "$RUNTIME_PLUGINS_PATH\Newtonsoft.Json.dll"
}

if (Test-Path -Path $RUNTIME_PLUGINS_PATH\Newtonsoft.Json.xml)
{
	del "$RUNTIME_PLUGINS_PATH\Newtonsoft.Json.xml"
}

echo "GameLift Server SDK build completed!"
