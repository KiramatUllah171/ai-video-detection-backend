@echo off
set ASPNETCORE_ENVIRONMENT=Development
set AI_PROVIDER=local
set PROVIDER_MODE=local
set BITMIND_ENABLED=false
set EXTERNAL_PROVIDER_POLICY=Disabled
set LOCAL_FALLBACK_ENABLED=true
set "PasswordReset__Provider=Smtp"
set "PasswordReset__Host=smtp.gmail.com"
set "PasswordReset__Port=587"
set "PasswordReset__UseStartTls=true"
set "PasswordReset__UseSsl=false"
set "PasswordReset__Username=kiramatullahcomputer@gmail.com"
set "PasswordReset__SenderEmail=kiramatullahcomputer@gmail.com"
set "PasswordReset__SenderName=sachvideoai"
set "PasswordReset__FrontendBaseUrl=http://localhost:5173"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Ensure-BackendPortFree.ps1" -Port 5166
if errorlevel 1 exit /b 1

if "%PasswordReset__Password%"=="" (
  for /f "usebackq delims=" %%P in (`powershell -NoProfile -Command "$secure = Read-Host 'Gmail SMTP app password' -AsSecureString; $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure); try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }"`) do set "PasswordReset__Password=%%P"
)
if "%PasswordReset__Password%"=="" (
  echo Gmail SMTP app password is required.
  exit /b 1
)

pushd "%~dp0AiVideoDetection.Api"
dotnet run --launch-profile https
popd
