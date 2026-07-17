@echo off
set ASPNETCORE_ENVIRONMENT=Development
set AI_PROVIDER=bitmind
set PROVIDER_MODE=bitmind
set BITMIND_ENABLED=true
set EXTERNAL_PROVIDER_POLICY=Always
set LOCAL_FALLBACK_ENABLED=false

pushd "%~dp0AiVideoDetection.Api"
dotnet run --launch-profile https
popd
