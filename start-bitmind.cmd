@echo off
set ASPNETCORE_ENVIRONMENT=Development
set AI_PROVIDER=bitmind
set PROVIDER_MODE=bitmind
set BITMIND_ENABLED=true
set EXTERNAL_PROVIDER_POLICY=Always
set LOCAL_FALLBACK_ENABLED=false
set "PasswordReset__Provider=Smtp"
set "PasswordReset__Host=smtp.gmail.com"
set "PasswordReset__Port=587"
set "PasswordReset__UseStartTls=true"
set "PasswordReset__UseSsl=false"
set "PasswordReset__Username=kiramatullahcomputer@gmail.com"
set "PasswordReset__SenderEmail=kiramatullahcomputer@gmail.com"
set "PasswordReset__SenderName=SachAI"
set "PasswordReset__FrontendBaseUrl=http://localhost:5173"

if "%Jwt__Secret%"=="" set "Jwt__Secret=sachai-local-development-jwt-secret-change-before-production-2026"
if "%SubscriptionSecurity__HmacSecret%"=="" set "SubscriptionSecurity__HmacSecret=sachai-local-development-subscription-hmac-secret-change-before-production-2026"
if "%Easypaisa__CallbackSecret%"=="" set "Easypaisa__CallbackSecret=sachai-local-development-easypaisa-callback-secret-change-before-production-2026"

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
