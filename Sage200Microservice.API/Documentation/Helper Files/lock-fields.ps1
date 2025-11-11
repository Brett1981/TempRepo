param()

# === EDIT THESE ===
$BaseUrl   = "https://api.columbus.sage.com/uk/sage200extra/accounts/v1"  # no trailing slash
$Token     = @'
eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCIsImtpZCI6Ik56TXdPVVJHTVVZNU5ERXpRelJHUVVNMVF6azJSa1U1UVRJMU0wRTROemhGUmpWQ04wSTNOQSJ9.eyJpc3MiOiJodHRwczovL2lkLnNhZ2UuY29tLyIsInN1YiI6ImF1dGgwfDcwYmRiZGIyMTUzOWNlMTUzOTYyODBkYTgxZjdjOWYzZDcyZGQwZDFlZTE3M2FkNyIsImF1ZCI6WyJzMjAwdWtpcGQvc2FnZTIwMCIsImh0dHBzOi8vc2FnZS1jaWQtcHJvZC5zYWdlaWRwcm9kLmF1dGgwYXBwLmNvbS91c2VyaW5mbyJdLCJpYXQiOjE3NTk4MjQ3NDIsImV4cCI6MTc1OTg1MzU0Miwic2NvcGUiOiJvcGVuaWQgcHJvZmlsZSBlbWFpbCBvZmZsaW5lX2FjY2VzcyIsImF6cCI6InlHaEwzckh0TUt2TWRtTTZ4S25aSVdtN1VOQlJvSmxmIn0.ObtlTReqWWSuoZvj-awy_wnkfIlzTJ1JvWozElRan5ceb0nHgsL3R4pMYHIjo4Qc4eXzBrO9YpYbOxjgb_ZIRlnULeGUQ597B8UP25twB7UIcP2E6vxW8JN4frYYAbODDDzWD_gmVRB4VFjim0baMjvZZ95bMXEhvwvJoBUv_crbh7lL1PThMfRPFPFLTUO4dHw4qejp25KIbxCwFePEXGaOpwKLrCOH0okVHtv4XNiXG-ltmWGPQNORkynkXNaolZu5NEPRYK73912EeKAxNLc0P1_bjQ2rWNLL_EDRWrI5nbdL2E-Q8XQ3yUH44KGegVg2AjRE2lSxCKiMdpQAVg
'@ -replace '\r?\n',''  # remove any CR/LF  - OAuth access_token from Sage ID (do not wrap newlines)
$Site      = "ad499543-f1ac-44ef-b501-8d6a91de3647"
$Company   = "35"

# For POST tests:
$CustomerId   = 198531337                 # real customer id from your tenant
$ProductCode  = "SERV"                    # or set $StockItemId instead
$StockItemId  = $null                     # e.g., 987654
$ReceiptAmt   = 10.00

# === Output folder ===
$stamp  = (Get-Date).ToString("ddMMyyyy_HHmmss")
$OutDir = Join-Path (Get-Location) "field-lock_$stamp"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$commonHeaders = @{
  "Authorization" = "Bearer $Token"
  "X-Site"        = $Site
  "X-Company"     = $Company
  "Accept"        = "application/json"
}

# ---------------- Helpers ----------------
# Make sure we're on TLS1.2 (older Windows PowerShell can default lower)
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# Optional: a friendly UA can help certain proxies
$UserAgent = "powershell-sage200-client/1.0"

function Get-JwtClaims {
  param([string]$Jwt)
  try {
    $parts = $Jwt.Split('.')
    $payload = $parts[1].Replace('-','+').Replace('_','/')
    switch ($payload.Length % 4) { 2 {$payload+='=='}; 3 {$payload+='='} }
    return ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json)
  } catch { return $null }
}

function Test-TokenFresh {
  $c = Get-JwtClaims $Token
  if ($c -and $c.exp) {
    $exp = [DateTimeOffset]::FromUnixTimeSeconds([int64]$c.exp).UtcDateTime
    $mins = [math]::Round(($exp - [DateTime]::UtcNow).TotalMinutes,1)
    if ($mins -le 0) { Write-Host "WARN: OAuth token is expired ($exp UTC)." -ForegroundColor Yellow }
    elseif ($mins -lt 10) { Write-Host "WARN: OAuth token expires in $mins minutes." -ForegroundColor Yellow }
  }
}

function Invoke-WithRetry {
  param(
    [ValidateSet('GET','POST')] [string]$Method,
    [string]$Name,
    [string]$Url,
    $Body = $null
  )
  $attempt = 0
  $max     = 5
  $baseMs  = 400

  while ($attempt -lt $max) {
    $attempt++
    try {
      if ($Method -eq 'GET') {
        $resp = Invoke-WebRequest -Method GET -Uri $Url -Headers $commonHeaders -UserAgent $UserAgent -TimeoutSec 60
      } else {
        $headers = $commonHeaders.Clone()
        $headers["Idempotency-Key"] = [guid]::NewGuid().ToString()
        $json = $Body | ConvertTo-Json -Depth 32
        $resp = Invoke-WebRequest -Method POST -Uri $Url -Headers $headers -ContentType "application/json" -Body $json -UserAgent $UserAgent -TimeoutSec 60
      }
      Save-Resp $Name $resp
      return $resp
    } catch {
      # Try to read an HTTP status if we have a response
      $status = $null
      if ($_.Exception -and $_.Exception.Response) {
        try { $status = [int]$_.Exception.Response.StatusCode } catch {}
      }
      # Retry on transient statuses
      if ($status -in 502,503,504,429 -or -not $status) {
        $sleep = [int]([math]::Min(5000, [math]::Pow(2, $attempt) * $baseMs + (Get-Random -Min 0 -Max 250)))
        Start-Sleep -Milliseconds $sleep
        if ($attempt -lt $max) { continue }
      }
	  ("REQUEST BODY:`n" + $json) | Add-Content -Path (Join-Path $OutDir "$Name.error.txt")
      # Token trouble hint
      if ($status -eq 401) { Write-Host "NOTE: 401 Unauthorized—check token/site/company headers." -ForegroundColor Yellow }
      Dump-Error $Name $Url $_ $OutDir
      break
    }
  }
}

# Health check first (saves to _sites.json if good)
function Test-SageHealth {
  try {
    $resp = Invoke-WebRequest -Method GET -Uri "$BaseUrl/sites" -Headers @{ Authorization="Bearer $Token"; Accept="application/json" } -UserAgent $UserAgent -TimeoutSec 30
    $resp.Content | Set-Content (Join-Path $OutDir "_sites.json") -Encoding UTF8
    return $true
  } catch {
    Dump-Error "_sites_health" "$BaseUrl/sites" $_ $OutDir
    return $false
  }
}

# Rewire your wrappers to use the retry helper
function Get-Json { param([string]$Name, [string]$Url)
  $resp = Invoke-WithRetry -Method GET -Name $Name -Url $Url
  if ($resp) { return $resp.Content | ConvertFrom-Json }
}
function Call-GET  { param($Name, $Url)  Invoke-WithRetry -Method GET  -Name $Name -Url $Url  | Out-Null }
function Call-POST { param($Name, $Url, $Body) Invoke-WithRetry -Method POST -Name $Name -Url $Url -Body $Body | Out-Null }

function Save-Resp {
  param($Name, $Resp)
  $path = Join-Path $OutDir "$Name.json"
  if ($Resp -is [string]) { $Resp | Set-Content -Path $path -Encoding UTF8 }
  elseif ($Resp.PSObject.Properties.Name -contains "Content") { $Resp.Content | Set-Content -Path $path -Encoding UTF8 }
  else { ($Resp | ConvertTo-Json -Depth 32) | Set-Content -Path $path -Encoding UTF8 }
  Write-Host "Saved -> $path"
}

function Dump-Error {
  param($Name, $Url, $Err, $OutDir)
  $file = Join-Path $OutDir "$Name.error.txt"
  $w = New-Object System.Text.StringBuilder
  [void]$w.AppendLine("NAME: $Name")
  [void]$w.AppendLine("URL : $Url")
  if ($Err.Exception -and $Err.Exception.Response) {
    $resp = [System.Net.HttpWebResponse]$Err.Exception.Response
    [void]$w.AppendLine(("STATUS: {0} {1}" -f ([int]$resp.StatusCode), $resp.StatusDescription))
    [void]$w.AppendLine("HEADERS:")
    foreach ($k in $resp.Headers.AllKeys) {
      $val = $resp.Headers.GetValues($k) -join ", "
      [void]$w.AppendLine(("  {0}: {1}" -f $k, $val))
    }
    try {
      $sr = New-Object System.IO.StreamReader($resp.GetResponseStream())
      $body = $sr.ReadToEnd()
      [void]$w.AppendLine("BODY:")
      [void]$w.AppendLine($body)
    } catch {
      [void]$w.AppendLine("BODY: <unreadable>")
    }
  } else {
    [void]$w.AppendLine("NO HTTP RESPONSE OBJECT")
    [void]$w.AppendLine(($Err | Out-String))
  }
  if ($Err.ErrorDetails -and $Err.ErrorDetails.Message) {
  [void]$w.AppendLine("ERRORDETAILS:")
  [void]$w.AppendLine($Err.ErrorDetails.Message)
}
  $w.ToString() | Set-Content -Path $file -Encoding UTF8
  Write-Host "ERROR -> $file" -ForegroundColor Red
}


# Try to discover a bank id (several likely endpoints)
function Get-AnyBankId {
  $candidates = @(
    "$BaseUrl/banks?`$top=1",
    "$BaseUrl/bank_accounts?`$top=1",
    "$BaseUrl/cash_book_banks?`$top=1"
  )
  foreach ($u in $candidates) {
    try {
      $r = Invoke-WebRequest -Method GET -Uri $u -Headers $commonHeaders
      $items = $r.Content | ConvertFrom-Json
      if ($items) {
        if ($items[0].id)      { return [int64]$items[0].id }
        if ($items[0].bank_id) { return [int64]$items[0].bank_id }
      }
    } catch { }
  }
  return $null
}

function New-FreeTextBody {
    param(
        [string]$textField = 'free_text',
        [string]$lineTypeField = 'line_type',
        [int]$lineTypeValue = 1
    )
    
    $nowUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    $docNo = New-DocumentNo -NumericOnly
    
    # Get customer details
    $cust = Get-Json "lookup_customer_$CustomerId" "$BaseUrl/customers/$CustomerId"
    $TaxCodeId = [int64]$cust.default_tax_code_id
    $NomRef = $cust.default_nominal_code_reference
    $NomCC = $cust.default_nominal_code_cost_centre
    $NomDept = $cust.default_nominal_code_department
    
    $line = @{
        $lineTypeField = $lineTypeValue
        $textField = "SERV"
        quantity = 1
        unit_price = 10.00
        tax_code_id = $TaxCodeId
        nominal_code_reference = $NomRef
    }
    
    if ($NomCC) { $line.nominal_code_cost_centre = $NomCC }
    if ($NomDept) { $line.nominal_code_department = $NomDept }
    
    return @{
        customer_id = $CustomerId
        document_no = $docNo
        reference = "LOCKTEST-$docNo"
        document_date = $nowUtc
        use_invoice_address = $true
        requested_delivery_date = $nowUtc
        promised_delivery_date = $nowUtc
        lines = @($line)
    }
}

function New-DocumentNo {
  param([switch]$NumericOnly)   # use -NumericOnly if your site enforces numeric-only
  $stamp = (Get-Date -Format "yyMMddHHmmss")   # 12 digits
  $rnd   = Get-Random -Minimum 100 -Maximum 999  # 3 digits
  if ($NumericOnly) { $doc = "$stamp$rnd" } else { $doc = "SO$stamp$rnd" }  # 15 or 17 chars
  if ($doc.Length -gt 20) { $doc = $doc.Substring(0,20) }
  return $doc
}

# Resolve tax_code_id for the chosen CustomerId (tries /customers/{id} then a filter)
function Get-CustomerTaxCodeId {
  param([long]$cid)
  try {
    $r = Invoke-WebRequest -Method GET -Uri "$BaseUrl/customers/$cid" -Headers $commonHeaders
    $cust = $r.Content | ConvertFrom-Json
    if ($cust.default_tax_code_id) { return [int64]$cust.default_tax_code_id }
  } catch { }
  try {
    $r = Invoke-WebRequest -Method GET -Uri "$BaseUrl/customers?`$filter=id eq $cid&`$top=1" -Headers $commonHeaders
    $cust = ($r.Content | ConvertFrom-Json)[0]
    if ($cust.default_tax_code_id) { return [int64]$cust.default_tax_code_id }
  } catch { }
  # Fallback: grab any tax code
  try {
    $r = Invoke-WebRequest -Method GET -Uri "$BaseUrl/tax_codes?`$top=1" -Headers $commonHeaders
    $tc = ($r.Content | ConvertFrom-Json)[0]
    if ($tc.id) { return [int64]$tc.id }
  } catch { }
  return $null
}

Test-TokenFresh | Out-Null
if (-not (Test-SageHealth)) {
  Write-Host "Upstream health check failed (see _sites_health.error.txt). Continuing with retries enabled..." -ForegroundColor Yellow
}
# ---------------- Calls ----------------

# (Optional) Save /sites for the record
try {
  $sites = Invoke-WebRequest -Uri "$BaseUrl/sites" -Headers @{ Authorization="Bearer $Token"; Accept="application/json" }
  $sites.Content | Set-Content (Join-Path $OutDir "_sites.json") -Encoding UTF8
} catch { }

# 1) List customers
Call-GET "1_customers_list" "$BaseUrl/customers?`$top=1"

# 2) List SOP orders
Call-GET "2_sop_orders_list" "$BaseUrl/sop_orders?`$top=1"


# 3) Create SOP order — FreeText line (match accounts/v1 schema)

$docNo  = New-DocumentNo -NumericOnly   # <=20 chars; required when auto numbering is OFF
$nowUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# Pull customer defaults
$cust       = Get-Json "lookup_customer_$CustomerId" "$BaseUrl/customers/$CustomerId"
$TaxCodeId  = [int64]$cust.default_tax_code_id
$NomRef     = $cust.default_nominal_code_reference
$NomCC      = $cust.default_nominal_code_cost_centre
$NomDept    = $cust.default_nominal_code_department

# Build the line using THIS API's field names
$line = @{
  line_type            = "EnumLineTypeFreeText"  # string enum, not numeric
  description          = "SERV"                  # required for free-text
  line_quantity        = 1
  selling_unit_price   = 10.00
  tax_code_id          = $TaxCodeId
  nominal_reference    = $NomRef
}
if ($NomCC)  { $line.nominal_cost_centre = $NomCC }
if ($NomDept){ $line.nominal_department  = $NomDept }
# Optional (only if you want a visible unit text for free-text lines):
# $line.selling_unit_description = "Each"

# Order header
$orderBody = @{
  customer_id             = $CustomerId
  document_no             = $docNo                 # REQUIRED (auto numbering OFF)
  reference               = "LOCKTEST-$docNo"
  document_date           = $nowUtc
  use_invoice_address     = $true
  requested_delivery_date = $nowUtc
  promised_delivery_date  = $nowUtc
  lines                   = @($line)
}

Call-POST "3_sop_orders_create" "$BaseUrl/sop_orders" $orderBody

# Optional automatic retry flipping 'free_text' -> 'text' if the first POST 400s
if (Test-Path (Join-Path $OutDir "3_sop_orders_create.error.txt")) {
  $line.Remove('free_text') | Out-Null
  $line['text'] = 'SERV'
  $orderBody['lines'] = @($line)
  Call-POST "3_sop_orders_create_alt" "$BaseUrl/sop_orders" $orderBody
}

# If it 400s again, retry with 'text'
if (Test-Path (Join-Path $OutDir "3_sop_orders_create.error.txt")) {
  $orderBody2 = New-FreeTextBody 'text'
  Call-POST "3_sop_orders_create_alt" "$BaseUrl/sop_orders" $orderBody2
}

# If that 400s, immediately retry using numeric id = 1
if (Test-Path (Join-Path $OutDir "3_sop_orders_create.error.txt")) {
  $orderBody2 = New-FreeTextBody -lineTypeField 'line_type_id' -lineTypeValue 1
  Call-POST "3_sop_orders_create_alt" "$BaseUrl/sop_orders" $orderBody2
}

# If that 400s, immediately retry using numeric id = 1
if (Test-Path (Join-Path $OutDir "3_sop_orders_create.error.txt")) {
  $orderBody2 = New-FreeTextBody -lineTypeField 'line_type_id' -lineTypeValue 1
  Call-POST "3_sop_orders_create_alt" "$BaseUrl/sop_orders" $orderBody2
}

# 4) List sales_transaction_views
Call-GET "4_sales_transaction_views_list" "$BaseUrl/sales_transaction_views?`$top=1&`$orderby=posted_date desc"

# 5) List sales_posted_transactions
Call-GET "5_sales_posted_transactions_list" "$BaseUrl/sales_posted_transactions?`$top=1"

# 6) Create sales receipt (uses bank_id + cheque_value)
$BankId = if ($BankIdOverride) { $BankIdOverride } else { Get-AnyBankId }
if (-not $BankId) { Write-Host "WARN: Could not auto-discover bank id; set `$BankIdOverride to a known id." -ForegroundColor Yellow }

$receiptBody = @{
  customer_id  = $CustomerId
  bank_id      = $BankId
  cheque_value = $ReceiptAmt
  reference    = "LOCKTEST-R$(Get-Date -Format 'HHmmss')"
}
Call-POST "6_sales_receipts_create" "$BaseUrl/sales_receipts" $receiptBody

Write-Host "`nAll done. Outputs in: $OutDir"
