@echo off
set ASPNETCORE_ENVIRONMENT=Development
set AI_PROVIDER=local
set PROVIDER_MODE=local
set BITMIND_ENABLED=false
set EXTERNAL_PROVIDER_POLICY=Disabled
set LOCAL_FALLBACK_ENABLED=true

pushd "%~dp0AiVideoDetection.Api"
dotnet run --launch-profile https
popd
