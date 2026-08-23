param(
    [Parameter(Mandatory = $true)]
    [string]$To,

    [string]$Password = $env:PasswordReset__Password
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Password)) {
    $secure = Read-Host "Gmail SMTP app password" -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        $Password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

if ([string]::IsNullOrWhiteSpace($Password)) {
    throw "PasswordReset__Password is required for Gmail SMTP delivery."
}

$from = "kiramatullahcomputer@gmail.com"
$message = [System.Net.Mail.MailMessage]::new()
$client = $null

try {
    $message.From = [System.Net.Mail.MailAddress]::new($from, "sachvideoai")
    $message.To.Add($To)
    $message.Subject = "sachvideoai SMTP verification"
    $message.Body = "This is a real SMTP delivery test from sachvideoai."
    $message.IsBodyHtml = $false

    $client = [System.Net.Mail.SmtpClient]::new("smtp.gmail.com", 587)
    $client.EnableSsl = $true
    $client.DeliveryMethod = [System.Net.Mail.SmtpDeliveryMethod]::Network
    $client.UseDefaultCredentials = $false
    $client.Credentials = [System.Net.NetworkCredential]::new($from, $Password)

    $client.Send($message)
    Write-Host "Gmail SMTP verification email sent to $To from $from."
}
finally {
    if ($client) {
        $client.Dispose()
    }
    $message.Dispose()
}
