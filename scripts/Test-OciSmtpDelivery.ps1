param(
    [Parameter(Mandatory = $true)]
    [string]$To,

    [string]$HostName = $(if ($env:PasswordReset__Host) { $env:PasswordReset__Host } else { "smtp.email.me-dubai-1.oci.oraclecloud.com" }),
    [int]$Port = $(if ($env:PasswordReset__Port) { [int]$env:PasswordReset__Port } else { 587 }),
    [string]$Username = $env:PasswordReset__Username,
    [string]$Password = $env:PasswordReset__Password,
    [string]$FromEmail = $(if ($env:PasswordReset__SenderEmail) { $env:PasswordReset__SenderEmail } else { "noreply@sachaitech.com" }),
    [string]$FromName = $(if ($env:PasswordReset__SenderName) { $env:PasswordReset__SenderName } else { "SachAI" })
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Username)) {
    throw "PasswordReset__Username is required for OCI SMTP delivery."
}

if ([string]::IsNullOrWhiteSpace($Password)) {
    $secure = Read-Host "OCI SMTP password" -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        $Password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

if ([string]::IsNullOrWhiteSpace($Password)) {
    throw "PasswordReset__Password is required for OCI SMTP delivery."
}

$message = [System.Net.Mail.MailMessage]::new()
$client = $null

try {
    $message.From = [System.Net.Mail.MailAddress]::new($FromEmail, $FromName)
    $message.To.Add($To)
    $message.Subject = "SachAI OCI SMTP verification"
    $message.Body = "This is a real OCI Email Delivery SMTP test from SachAI."
    $message.IsBodyHtml = $false

    $client = [System.Net.Mail.SmtpClient]::new($HostName, $Port)
    $client.EnableSsl = $true
    $client.DeliveryMethod = [System.Net.Mail.SmtpDeliveryMethod]::Network
    $client.UseDefaultCredentials = $false
    $client.Credentials = [System.Net.NetworkCredential]::new($Username, $Password)

    $client.Send($message)
    Write-Host "OCI SMTP verification email sent to $To from $FromEmail."
}
finally {
    if ($client) {
        $client.Dispose()
    }
    $message.Dispose()
}
