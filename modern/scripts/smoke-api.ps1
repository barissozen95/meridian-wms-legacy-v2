# Live API smoke test: drives every legacy endpoint through a full order lifecycle against a running API.
# Usage: ./scripts/smoke-api.ps1 [-BaseUrl http://localhost:5080]   (exits 1 on the first unexpected status)
param([string]$BaseUrl = "http://localhost:5080")
$ErrorActionPreference = "Stop"

function Call([string]$Method, [string]$Path, [int]$Expect, $Body = $null) {
    $req = @{ Method = $Method; Uri = "$BaseUrl$Path"; UseBasicParsing = $true }
    if ($null -ne $Body) { $req.Body = ($Body | ConvertTo-Json -Depth 5 -Compress); $req.ContentType = "application/json" }
    # Works on Windows PowerShell 5.1 (no -SkipHttpErrorCheck): read non-2xx status/body from the exception.
    try {
        $res = Invoke-WebRequest @req
        $status = [int]$res.StatusCode; $content = $res.Content
    } catch [System.Net.WebException] {
        $status = [int]$_.Exception.Response.StatusCode
        $content = (New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd()
    }
    $ok = $status -eq $Expect
    Write-Host ("{0} {1,-6} {2,-62} -> {3} (expected {4})" -f ($(if ($ok) { "PASS" } else { "FAIL" })), $Method, $Path, $status, $Expect)
    if ($content) { Write-Host ("     " + $content.Substring(0, [Math]::Min(400, $content.Length))) }
    if (-not $ok) { exit 1 }
    if ($content -and $content.TrimStart().StartsWith("{")) { return $content | ConvertFrom-Json }
}

$null = Call GET "/health" 200
$null = Call GET "/api/products" 200
$null = Call GET "/api/products/1" 200
$null = Call GET "/api/products/999" 404
$null = Call POST "/api/products" 200 @{ Sku = "GSK-100"; Name = "Flange Gasket 100mm"; Category = "Seals"; UnitWeightKg = 0.045; UnitPrice = 3.20 }
$null = Call POST "/api/products" 409 @{ Sku = "GSK-100"; Name = "Duplicate"; UnitWeightKg = 1; UnitPrice = 1 }

$null = Call GET "/api/inventory/warehouse/WH-CENTRAL" 200
$null = Call GET "/api/inventory/low-stock" 200
$null = Call GET "/api/inventory/search?name=PUMP" 200
$null = Call GET ("/api/inventory/search?name=" + [uri]::EscapeDataString("' OR '1'='1")) 200
$null = Call POST "/api/inventory/adjust?productId=1&warehouseCode=WH-CENTRAL&delta=5" 200
$null = Call POST "/api/inventory/adjust?productId=3&warehouseCode=WH-CENTRAL&delta=-100" 400

$null = Call POST "/api/orders" 400 @{ CustomerName = "Acme"; Lines = @(@{ ProductId = 1; Quantity = 0 }) }
$order = Call POST "/api/orders" 200 @{ CustomerName = "Acme Pumps Ltd"; DestinationCity = "Leeds"; DestinationCountry = "UK"; Lines = @(@{ ProductId = 1; Quantity = 3 }, @{ ProductId = 2; Quantity = 10 }) }
$id = $order.OrderId
$null = Call POST "/api/orders/$id/ship" 400 @{ Carrier = "DHL Freight"; TrackingNumber = "TRK-SMOKE-1" }
$null = Call POST "/api/orders/$id/allocate" 200
$null = Call POST "/api/orders/$id/allocate" 400
$null = Call POST "/api/orders/$id/ship" 200 @{ Carrier = "DHL Freight"; TrackingNumber = "TRK-SMOKE-1" }
$null = Call GET "/api/orders/$id" 200
$null = Call GET "/api/orders" 200
$null = Call GET "/api/shipments/order/$id" 200
$null = Call GET "/api/shipments" 200
$null = Call POST "/api/orders/999/allocate" 404

Write-Host "SMOKE RESULT: ALL PASSED"

