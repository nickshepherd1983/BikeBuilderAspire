#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Generates fresh APIM self-hosted gateway tokens and stores them in the AppHost's user
    secrets, so the Aspire AppHost runs the real gateway container instead of the YARP
    fallback.

.DESCRIPTION
    Gateway tokens cannot be created by Bicep (they are a POST action, not a resource) and
    their expiry is capped at 30 days, so this needs re-running roughly monthly. When a
    token expires, the gateway container fails its health check with a config-auth error in
    its logs - re-run this script, then restart the container (it is persistent in dev, so
    it keeps the stale token env until recreated).

.EXAMPLE
    ./new-gateway-token.ps1 -SubscriptionId 00000000-0000-0000-0000-000000000000
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SubscriptionId,

    [string]$EnvironmentName = 'bikebuilder',

    [int]$ExpiryDays = 29
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI (az) is not installed. See https://aka.ms/installazurecli'
}

$resourceGroup = "rg-$EnvironmentName"
$apimName = az apim list --subscription $SubscriptionId --resource-group $resourceGroup --query '[0].name' -o tsv
if (-not $apimName) { throw "No API Management instance found in $resourceGroup." }

$expiry = (Get-Date).ToUniversalTime().AddDays($ExpiryDays).ToString('yyyy-MM-ddTHH:mm:ssZ')
$appHostProject = Join-Path $PSScriptRoot '..' 'Src' 'BikeBuilder.AppHost'

$configEndpoint = "https://$apimName.configuration.azure-api.net"
dotnet user-secrets set 'Apim:ConfigEndpoint' $configEndpoint --project $appHostProject | Out-Null
Write-Host "Apim:ConfigEndpoint = $configEndpoint" -ForegroundColor Green

foreach ($gateway in @(@{ Name = 'local-dev'; Secret = 'Apim:GatewayTokenDev' }, @{ Name = 'local-test'; Secret = 'Apim:GatewayTokenTest' })) {
    $url = "https://management.azure.com/subscriptions/$SubscriptionId/resourceGroups/$resourceGroup" +
        "/providers/Microsoft.ApiManagement/service/$apimName/gateways/$($gateway.Name)/generateToken?api-version=2024-05-01"
    $token = (az rest --method post --url $url --body "{`"keyType`":`"primary`",`"expiry`":`"$expiry`"}" --query value -o tsv)
    if (-not $token) { throw "Token generation failed for gateway $($gateway.Name)." }

    dotnet user-secrets set $gateway.Secret $token --project $appHostProject | Out-Null
    Write-Host "$($gateway.Secret) set (gateway $($gateway.Name), expires $expiry)" -ForegroundColor Green
}

Write-Host "`nDone. The AppHost will now run the real APIM self-hosted gateway container." -ForegroundColor Cyan
Write-Host 'To fall back to the YARP stand-in: dotnet user-secrets remove Apim:ConfigEndpoint --project Src/BikeBuilder.AppHost'
