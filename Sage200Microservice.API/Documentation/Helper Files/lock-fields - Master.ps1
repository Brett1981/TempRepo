param()

# ==================== EDIT THESE ====================
$BaseUrl   = "https://api.columbus.sage.com/uk/sage200extra/accounts/v1"  # no trailing slash
$Token     = @'
eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCIsImtpZCI6Ik56TXdPVVJHTVVZNU5ERXpRelJHUVVNMVF6azJSa1U1UVRJMU0wRTROemhGUmpWQ04wSTNOQSJ9.eyJpc3MiOiJodHRwczovL2lkLnNhZ2UuY29tLyIsInN1YiI6ImF1dGgwfDcwYmRiZGIyMTUzOWNlMTUzOTYyODBkYTgxZjdjOWYzZDcyZGQwZDFlZTE3M2FkNyIsImF1ZCI6WyJzMjAwdWtpcGQvc2FnZTIwMCIsImh0dHBzOi8vc2FnZS1jaWQtcHJvZC5zYWdlaWRwcm9kLmF1dGgwYXBwLmNvbS91c2VyaW5mbyJdLCJpYXQiOjE3NTk4MjQ3NDIsImV4cCI6MTc1OTg1MzU0Miwic2NvcGUiOiJvcGVuaWQgcHJvZmlsZSBlbWFpbCBvZmZsaW5lX2FjY2VzcyIsImF6cCI6InlHaEwzckh0TUt2TWRtTTZ4S25aSVdtN1VOQlJvSmxmIn0.ObtlTReqWWSuoZvj-awy_wnkfIlzTJ1JvWozElRan5ceb0nHgsL3R4pMYHIjo4Qc4eXzBrO9YpYbOxjgb_ZIRlnULeGUQ597B8UP25twB7UIcP2E6vxW8JN4frYYAbODDDzWD_gmVRB4VFjim0baMjvZZ95bMXEhvwvJoBUv_crbh7lL1PThMfRPFPFLTUO4dHw4qejp25KIbxCwFePEXGaOpwKLrCOH0okVHtv4XNiXG-ltmWGPQNORkynkXNaolZu5NEPRYK73912EeKAxNLc0P1_bjQ2rWNLL_EDRWrI5nbdL2E-Q8XQ3yUH44KGegVg2AjRE2lSxCKiMdpQAVg
'@ -replace '\r?\n',''  # remove any CR/LF  - OAuth access_token from Sage ID (do not wrap newlines)
$Site      = "ad499543-f1ac-44ef-b501-8d6a91de3647"
$Company   = "35"

# Known IDs / test values
$CustomerId   = 198531337      # real customer id in your tenant
$ReceiptAmt   = 10.00
$ExternalKey  = "extk-LOCKTEST"   # mirrored to spare_text_1 where relevant

# Which creates to run (enable as ready)
$DO_SOP_ORDER             = $true
$DO_SOP_ORDER_NEW         = $false
$DO_SOP_STATUS_CHANGE     = $false
$SOP_STATUS_VALUE         = "EnumSopDocumentStatusLive"
$DO_SOP_DUPLICATE         = $true

$DO_SALES_RECEIPT         = $true
$DO_SALES_PAYMENT         = $true          # keep $true only if your tenant supports it

# Sales Ledger docs (send header totals; do NOT send 'reference')
$DO_SALES_INVOICE         = $true
$DO_SALES_INVOICE_NEW     = $true
$DO_SALES_CREDIT_NOTE     = $true
$DO_SALES_CREDIT_NOTE_NEW = $true
$DO_SALES_ALLOCATIONS     = $true         # needs real open URNs – leave off unless you have them

$DO_CUSTOMER              = $true          # minimal create; auto-fallback without reference
$DO_CUSTOMER_NEW          = $true
$DO_CUSTOMER_CONTACT      = $true

# ==================== Output folder & CSV ====================
$stamp  = (Get-Date).ToString("yyyyMMdd_HHmmss")
$OutDir = Join-Path (Get-Location) "field-lock_$stamp"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$MatrixCsv = Join-Path $OutDir "field-lock-matrix.csv"

# ==================== Common headers ====================
# IMPORTANT: Do NOT add 'Connection' or 'Keep-Alive' headers. PowerShell will throw.
$commonHeaders = @{
  "Authorization" = "Bearer $Token"
  "X-Site"        = $Site
  "X-Company"     = $Company
  "Accept"        = "application/json"
}

# ==================== Helpers ====================
[Net.ServicePointManager]::SecurityProtocol     = [Net.SecurityProtocolType]::Tls12
[System.Net.ServicePointManager]::Expect100Continue = $false  # helps some proxies
$UserAgent = "powershell-sage200-client/1.0"

function Save-Resp {
  param($Name, $Resp)
  $path = Join-Path $OutDir "$Name.json"
  if ($Resp -is [string]) {
    $Resp | Set-Content -Path $path -Encoding UTF8
  } elseif ($Resp.PSObject.Properties.Name -contains "Content") {
    $Resp.Content | Set-Content -Path $path -Encoding UTF8
  } else {
    ($Resp | ConvertTo-Json -Depth 32) | Set-Content -Path $path -Encoding UTF8
  }
  Write-Host "Saved -> $path"
}

function Dump-Error {
  param($Name, $Url, $Err, $Body = $null)
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
  if ($Body -ne $null) {
    try {
      $json = $Body | ConvertTo-Json -Depth 32
      [void]$w.AppendLine("REQUEST BODY:")
      [void]$w.AppendLine($json)
    } catch {
      [void]$w.AppendLine("REQUEST BODY: <unserializable>")
    }
  }
  $w.ToString() | Set-Content -Path $file -Encoding UTF8
  Write-Host "ERROR -> $file" -ForegroundColor Red
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
      $status = $null
      if ($_.Exception -and $_.Exception.Response) {
        try { $status = [int]$_.Exception.Response.StatusCode } catch {}
      }
      if ($status -in 502,503,504,429 -or -not $status) {
        $sleep = [int]([math]::Min(8000, [math]::Pow(2, $attempt) * $baseMs + (Get-Random -Min 0 -Max 300)))
        Start-Sleep -Milliseconds $sleep
        if ($attempt -lt $max) { continue }
      }
      if ($status -eq 401) {
        Write-Host "NOTE: 401 Unauthorized — check token/site/company headers." -ForegroundColor Yellow
      }
      Dump-Error $Name $Url $_ $Body
      break
    }
  }
}

function Get-Json {
  param([string]$Name, [string]$Url)
  $resp = Invoke-WithRetry -Method GET -Name $Name -Url $Url
  if ($resp) { return $resp.Content | ConvertFrom-Json }
}

function Call-GET  { param($Name, $Url)  Invoke-WithRetry -Method GET  -Name $Name -Url $Url  | Out-Null }
function Call-POST { param($Name, $Url, $Body) Invoke-WithRetry -Method POST -Name $Name -Url $Url -Body $Body | Out-Null }

# Discover a bank id from any likely endpoint
function Get-AnyBankId {
  $candidates = @(
    "$BaseUrl/banks?`$top=1",
    "$BaseUrl/bank_accounts?`$top=1",
    "$BaseUrl/cash_book_banks?`$top=1"
  )
  foreach ($u in $candidates) {
    try {
      $r = Invoke-WebRequest -Method GET -Uri $u -Headers $commonHeaders -UserAgent $UserAgent
      $items = $r.Content | ConvertFrom-Json
      if ($items) {
        if ($items[0].id)      { return [int64]$items[0].id }
        if ($items[0].bank_id) { return [int64]$items[0].bank_id }
      }
    } catch { }
  }
  return $null
}

# Friendly doc number (<= 20 chars)
function New-DocumentNo {
  param([switch]$NumericOnly)
  $stamp = (Get-Date -Format "yyMMddHHmmss")  # 12 digits
  $rnd   = Get-Random -Minimum 100 -Maximum 999
  $doc   = ""
  if ($NumericOnly) { $doc = "$stamp$rnd" } else { $doc = "SO$stamp$rnd" }
  if ($doc.Length -gt 20) { $doc = $doc.Substring(0,20) }
  return $doc
}

# Customers: many tenants cap at 8 and prefer numeric
function New-CustomerReference {
  # 8 numerics: yyMMdd + 2 digits
  return (Get-Date -Format "yyMMdd") + (Get-Random -Minimum 10 -Maximum 99)
}

# ==================== Body Builders ====================

# SOP order (free-text line) — accounts/v1 field names
function Build-SopOrder {
  param([int64]$cid, [string]$externalKey)

  $nowUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
  $docNo  = New-DocumentNo -NumericOnly

  $cust = Get-Json "lookup_customer_$cid" "$BaseUrl/customers/$cid"
  $TaxCodeId = [int64]$cust.default_tax_code_id
  $NomRef    = $cust.default_nominal_code_reference
  $NomCC     = $cust.default_nominal_code_cost_centre
  $NomDept   = $cust.default_nominal_code_department

  $line = @{
    line_type            = "EnumLineTypeFreeText"   # string enum (not numeric)
    description          = "SERV"                   # REQUIRED for free-text
    line_quantity        = 1
    selling_unit_price   = 10.00
    tax_code_id          = $TaxCodeId
    nominal_reference    = $NomRef
  }
  if ($NomCC)  { $line.nominal_cost_centre = $NomCC }
  if ($NomDept){ $line.nominal_department  = $NomDept }

  $body = @{
    customer_id             = $cid
    document_no             = $docNo                # REQUIRED if SOP numbering auto is OFF
    reference               = "LOCKTEST-$docNo"
    document_date           = $nowUtc
    requested_delivery_date = $nowUtc
    promised_delivery_date  = $nowUtc
    use_invoice_address     = $true
    lines                   = @($line)
  }
  if ($externalKey) { $body.spare_text_1 = $externalKey }
  return $body
}

# Sales Ledger invoice/credit — send header totals (NOT SOP lines), do NOT send 'reference'
function Build-SalesLedgerDoc {
  param([int64]$cid, [ValidateSet("Invoice","Credit")] [string]$Kind, [decimal]$goods = 10.00, [decimal]$tax = 0.00, [string]$externalKey)

  $nowUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
  $body = @{
    customer_id          = $cid
    transaction_date     = $nowUtc
    document_goods_value = [decimal]$goods
    document_tax_value   = [decimal]$tax
  }
  if ($externalKey) { $body.spare_text_1 = $externalKey }
  return $body
}

# Sales Receipt (works in tenant with bank_id + cheque_value)
function Build-SalesReceipt {
  param([int64]$cid, [decimal]$amount)
  $bank = Get-AnyBankId
  $body = @{
    customer_id  = $cid
    bank_id      = $bank
    cheque_value = $amount
    reference    = "LOCKTEST-R" + (Get-Date -Format "HHmmss")
  }
  return $body
}

# Sales Payment (often mirrors receipt schema)
function Build-SalesPayment {
  param([int64]$cid, [decimal]$amount)
  $bank = Get-AnyBankId
  $body = @{
    customer_id  = $cid
    bank_id      = $bank
    cheque_value = $amount
    reference    = "LOCKTEST-P" + (Get-Date -Format "HHmmss")
  }
  return $body
}

# Customer (minimal; retry without reference if tenant rejects it)
function Build-Customer {
  param([string]$externalKey)
  $ref = New-CustomerReference
  $body = @{
    reference            = $ref            # 8 numeric chars
    name                 = "TEST Customer $ref"
    main_address_line_1  = "1 Test Street"
    main_city            = "Testville"
    main_postcode        = "T35 7AA"
    status               = "EnumTradingStatusLive"
  }
  if ($externalKey) { $body.spare_text_1 = $externalKey }
  return $body
}
function Build-CustomerNew { param([string]$externalKey) return (Build-Customer -externalKey $externalKey) }

# Customer Contact
function Build-Contact {
  $body = @{
    name          = "Primary Contact"
    telephone     = "0123456789"
    email_address = "primary@example.com"
    is_primary    = $true
  }
  return $body
}

# ==================== Result Matrix ====================
$Matrix = New-Object System.Collections.Generic.List[Object]
function Add-MatrixRow {
  param($Path, $Method, $Name, $Outcome, $StatusCode, $PrimaryId, $Extra, $ErrorMessage)
  $row = [PSCustomObject]@{
    Path        = $Path
    Method      = $Method
    Name        = $Name
    Outcome     = $Outcome
    StatusCode  = $StatusCode
    PrimaryId   = $PrimaryId
    Extra       = $Extra
    Error       = $ErrorMessage
  }
  $Matrix.Add($row) | Out-Null
}

# ==================== Run ====================

# Health check/sanity
try {
  $sites = Invoke-WebRequest -Uri "$BaseUrl/sites" -Headers @{ Authorization="Bearer $Token"; Accept="application/json" } -UserAgent $UserAgent
  $sites.Content | Set-Content (Join-Path $OutDir "_sites.json") -Encoding UTF8
  Write-Host "Saved -> $(Join-Path $OutDir "_sites.json")"
} catch {
  Dump-Error "_sites_health" "$BaseUrl/sites" $_
  Write-Host "WARN: _sites health failed. Continuing with retries..." -ForegroundColor Yellow
}

# Smoke GETs
Call-GET "smoke_customers" "$BaseUrl/customers?`$top=1"
Call-GET "smoke_sop_orders" "$BaseUrl/sop_orders?`$top=1"

# ========== 1) SOP Order ==========
$createdSopId = $null
if ($DO_SOP_ORDER) {
  $body = Build-SopOrder -cid $CustomerId -externalKey $ExternalKey
  $name = "sop_orders_create"
  $url  = "$BaseUrl/sop_orders"
  $resp = Invoke-WithRetry -Method POST -Name $name -Url $url -Body $body
  if ($resp) {
    $json = $resp.Content | ConvertFrom-Json
    $createdSopId = $json.id
    Add-MatrixRow "/sop_orders" "POST" $name "Success" 200 "$($json.id)" "$($json.document_no)" ""
  } else {
    $msg = ""; $ef = Join-Path $OutDir "$name.error.txt"; if (Test-Path $ef) { $msg = Get-Content -Raw $ef }
    Add-MatrixRow "/sop_orders" "POST" $name "Failed" 400 "" "" $msg
  }
}

# (Optional) /sop_orders_new
if ($DO_SOP_ORDER_NEW) {
  $body = Build-SopOrder -cid $CustomerId -externalKey $ExternalKey
  $name = "sop_orders_new_create"
  $url  = "$BaseUrl/sop_orders_new"
  $resp = Invoke-WithRetry -Method POST -Name $name -Url $url -Body $body
  if ($resp) {
    $json = $resp.Content | ConvertFrom-Json
    Add-MatrixRow "/sop_orders_new" "POST" $name "Success" 200 "$($json.id)" "$($json.document_no)" ""
  } else {
    $msg = ""; $ef = Join-Path $OutDir "$name.error.txt"; if (Test-Path $ef) { $msg = Get-Content -Raw $ef }
    Add-MatrixRow "/sop_orders_new" "POST" $name "Failed" 400 "" "" $msg
  }
}

# SOP status change
if ($DO_SOP_STATUS_CHANGE -and $createdSopId) {
  $name = "sop_orders_status_change"
  $url  = "$BaseUrl/sop_orders_status"
  $body = @{ id = $createdSopId; status = $SOP_STATUS_VALUE }
  $resp = Invoke-WithRetry -Method POST -Name $name -Url $url -Body $body
  if ($resp) {
    Add-MatrixRow "/sop_orders_status" "POST" $name "Success" 200 "$createdSopId" "$SOP_STATUS_VALUE" ""
  } else {
    $msg = ""; $ef = Join-Path $OutDir "$name.error.txt"; if (Test-Path $ef) { $msg = Get-Content -Raw $ef }
    Add-MatrixRow "/sop_orders_status" "POST" $name "Failed" 400 "$createdSopId" "" $msg
  }
}

# SOP duplicate (requires an order we just created)
if ($DO_SOP_DUPLICATE -and $createdSopId) {
  $name = "sop_orders_duplicate"
  $url  = "$BaseUrl/sop_orders_duplicate"
  $body = @{
    source_id   = $createdSopId
    customer_id = $CustomerId
  }
  $resp = Invoke-WithRetry -Method POST -Name $name -Url $url -Body $body
  if ($resp) {
    $json = $resp.Content | ConvertFrom-Json
    Add-MatrixRow "/sop_orders_duplicate" "POST" $name "Success" 200 "$($json.id)" "$($json.document_no)" ""
  } else {
    $msg = ""; $ef = Join-Path $OutDir "$name.error.txt"; if (Test-Path $ef) { $msg = Get-Content -Raw $ef }
    Add-MatrixRow "/sop_orders_duplicate" "POST" $name "Failed" 400 "" "" $msg
  }
}

# ========== 2) Sales Receipt ==========
if ($DO_SALES_RECEIPT) {
  $body = Build-SalesReceipt -cid $CustomerId -amount $ReceiptAmt
  if (-not $body.bank_id) {
    Write-Host "WARN: No bank_id discovered; skipping /sales_receipts." -ForegroundColor Yellow
  } else {
    $name = "sales_receipts_create"
    $url  = "$BaseUrl/sales_receipts"
    $resp = Invoke-WithRetry -Method POST -Name $name -Url $url -Body $body
    if ($resp) {
      $json = $resp.Content | ConvertFrom-Json
      Add-MatrixRow "/sales_receipts" "POST" $name "Success" 200 "$($json.urn)" "" ""
    } else {
      $msg = ""; $ef = Join-Path $OutDir "$name.error.txt"; if (Test-Path $ef) { $msg = Get-Content -Raw $ef }
      Add-MatrixRow "/sales_receipts" "POST" $name "Failed" 400 "" "" $msg
    }
  }
}

# ========== 3) Sales Payment ==========
if ($DO_SALES_PAYMENT) {
  $body = Build-SalesPayment -cid $CustomerId -amount $ReceiptAmt
  if (-not $body.bank_id) {
    Write-Host "WARN: No bank_id discovered; skipping /sales_payments." -ForegroundColor Yellow
  } else {
    $name = "sales_payments_create"
    $url  = "$BaseUrl/sales_payments"
    $resp = Invoke-WithRetry -Method POST -Name $name -Url $url -Body $body
    if ($resp) {
      $json = $resp.Content | ConvertFrom-Json
      Add-MatrixRow "/sales_payments" "POST" $name "Success" 200 "$($json.urn)" "" ""
    } else {
      $msg = ""; $ef = Join-Path $OutDir "$name.error.txt"; if (Test-Path $ef) { $msg = Get-Content -Raw $ef }
      Add-MatrixRow "/sales_payments" "POST" $name "Failed" 400 "" "" $msg
    }
  }
}

# ========== 4) Sales Ledger: Invoice / New ==========
if ($DO_SALES_INVOICE) {
  $body = Build-SalesLedgerDoc -cid $CustomerId -Kind "Invoice" -goods 10.00 -tax 0.00 -externalKey $ExternalKey
  $name = "sales_invoices_create"
  $url  = "$BaseUrl/sales_invoices"
  $resp = Invoke-WithRetry -Method POST -Name $name -Url $url -Body $body
  if ($resp) {
    $json = $resp.Content | ConvertFrom-Json
    Add-MatrixRow "/sales_invoices" "POST" $name "Success" 200 "$($json.urn)" "" ""
  } else {
    $msg = ""; $ef = Join-Path $OutDir "$name.error.txt"; if (Test-Path $ef) { $msg = Get-Content -Raw $ef }
    Add-MatrixRow "/sales_invoices" "POST" $name "Failed" 400 "" "" $msg
  }
}
if ($DO_SALES_INVOICE_NEW) {
  $body = Build-SalesLedgerDoc -cid $CustomerId -Kind "Invoice" -goods 10.00 -tax 0.00 -externalKey $ExternalKey
  $name = "sales_invoices_new_create"
  $url  = "$BaseUrl/sales_invoices_new"
  $resp = Invoke-WithRetry -Method POST -Name $name -Url $url -Body $body
  if ($resp) {
    $json = $resp.Content | ConvertFrom-Json
    Add-MatrixRow "/sales_invoices_new" "POST" $name "Success" 200 "$($json.urn)" "" ""
  } else {
    $msg = ""; $ef = Join-Path $OutDir "$name.error.txt"; if (Test-Path $ef) { $msg = Get-Content -Raw $ef }
    Add-MatrixRow "/sales_invoices_new" "POST" $name "Failed" 400 "" "" $msg
  }
}

# ========== 5) Sales Ledger: Credit Note / New ==========
if ($DO_SALES_CREDIT_NOTE) {
  $body = Build-SalesLedgerDoc -cid $CustomerId -Kind "Credit" -goods 10.00 -tax 0.00 -externalKey $ExternalKey
  $name = "sales_credit_notes_create"
  $url  = "$BaseUrl/sales_credit_notes"
  $resp = Invoke-WithRetry -Method POST -Name $name -Url $url -Body $body
  if ($resp) {
    $json = $resp.Content | ConvertFrom-Json
    Add-MatrixRow "/sales_credit_notes" "POST" $name "Success" 200 "$($json.urn)" "" ""
  } else {
    $msg = ""; $ef = Join-Path $OutDir "$name.error.txt"; if (Test-Path $ef) { $msg = Get-Content -Raw $ef }
    Add-MatrixRow "/sales_credit_notes" "POST" $name "Failed" 400 "" "" $msg
  }
}
if ($DO_SALES_CREDIT_NOTE_NEW) {
  $body = Build-SalesLedgerDoc -cid $CustomerId -Kind "Credit" -goods 10.00 -tax 0.00 -externalKey $ExternalKey
  $name = "sales_credit_notes_new_create"
  $url  = "$BaseUrl/sales_credit_notes_new"
  $resp = Invoke-WithRetry -Method POST -Name $name -Url $url -Body $body
  if ($resp) {
    $json = $resp.Content | ConvertFrom-Json
    Add-MatrixRow "/sales_credit_notes_new" "POST" $name "Success" 200 "$($json.urn)" "" ""
  } else {
    $msg = ""; $ef = Join-Path $OutDir "$name.error.txt"; if (Test-Path $ef) { $msg = Get-Content -Raw $ef }
    Add-MatrixRow "/sales_credit_notes_new" "POST" $name "Failed" 400 "" "" $msg
  }
}

# ========== 6) Sales Allocations (needs real URNs) ==========
if ($DO_SALES_ALLOCATIONS) {
  $name = "sales_allocations_create"
  $url  = "$BaseUrl/sales_allocations"
  $body = @{
    allocations = @(
      @{ receipt_urn = 726427; invoice_urn = 701711; value = 0.00 }
    )
  }
  $resp = Invoke-WithRetry -Method POST -Name $name -Url $url -Body $body
  if ($resp) {
    Add-MatrixRow "/sales_allocations" "POST" $name "Success" 200 "" "" ""
  } else {
    $msg = ""; $ef = Join-Path $OutDir "$name.error.txt"; if (Test-Path $ef) { $msg = Get-Content -Raw $ef }
    Add-MatrixRow "/sales_allocations" "POST" $name "Failed" 400 "" "" $msg
  }
}

# ========== 7) Customer / New ==========
$createdCustomerId = $null
if ($DO_CUSTOMER) {
  $body = Build-Customer -externalKey $ExternalKey
  $name = "customers_create"
  $url  = "$BaseUrl/customers"
  $resp = Invoke-WithRetry -Method POST -Name $name -Url $url -Body $body
  if (-not $resp) {
    # Retry without reference to allow auto-numbering if tenant forbids setting it
    if ($body.ContainsKey('reference')) { $body.Remove('reference') | Out-Null }
    $resp = Invoke-WithRetry -Method POST -Name $name -Url $url -Body $body
  }
  if ($resp) {
    $json = $resp.Content | ConvertFrom-Json
    $createdCustomerId = $json.id
    Add-MatrixRow "/customers" "POST" $name "Success" 200 "$($json.id)" "$($json.reference)" ""
  } else {
    $msg = ""; $ef = Join-Path $OutDir "$name.error.txt"; if (Test-Path $ef) { $msg = Get-Content -Raw $ef }
    Add-MatrixRow "/customers" "POST" $name "Failed" 400 "" "" $msg
  }
}
if ($DO_CUSTOMER_NEW) {
  $body = Build-CustomerNew -externalKey $ExternalKey
  $name = "customers_new_create"
  $url  = "$BaseUrl/customers_new"
  $resp = Invoke-WithRetry -Method POST -Name $name -Url $url -Body $body
  if (-not $resp) {
    if ($body.ContainsKey('reference')) { $body.Remove('reference') | Out-Null }
    $resp = Invoke-WithRetry -Method POST -Name $name -Url $url -Body $body
  }
  if ($resp) {
    $json = $resp.Content | ConvertFrom-Json
    if (-not $createdCustomerId) { $createdCustomerId = $json.id }
    Add-MatrixRow "/customers_new" "POST" $name "Success" 200 "$($json.id)" "$($json.reference)" ""
  } else {
    $msg = ""; $ef = Join-Path $OutDir "$name.error.txt"; if (Test-Path $ef) { $msg = Get-Content -Raw $ef }
    Add-MatrixRow "/customers_new" "POST" $name "Failed" 400 "" "" $msg
  }
}

# ========== 8) Customer Contact ==========
if ($DO_CUSTOMER_CONTACT) {
  $targetCustId = if ($createdCustomerId) { $createdCustomerId } else { $CustomerId }
  $body = Build-Contact
  $name = "customer_contacts_create"
  $url  = "$BaseUrl/customers/$targetCustId/customer_contacts"
  $resp = Invoke-WithRetry -Method POST -Name $name -Url $url -Body $body
  if ($resp) {
    $json = $resp.Content | ConvertFrom-Json
    Add-MatrixRow "/customers/{id}/customer_contacts" "POST" $name "Success" 200 "$($json.id)" "" ""
  } else {
    $msg = ""; $ef = Join-Path $OutDir "$name.error.txt"; if (Test-Path $ef) { $msg = Get-Content -Raw $ef }
    Add-MatrixRow "/customers/{id}/customer_contacts" "POST" $name "Failed" 400 "" "" $msg
  }
}

# ========== Write CSV ==========
$Matrix | Export-Csv -NoTypeInformation -Path $MatrixCsv -Encoding UTF8
Write-Host "`nAll done. Outputs in: $OutDir"
Write-Host "Matrix: $MatrixCsv"
