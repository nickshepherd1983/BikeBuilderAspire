#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Provisions the free-tier BikeBuilder stack into an existing Azure subscription.

.DESCRIPTION
    This deploys infrastructure only. It cannot create the Entra tenant or the subscription
    themselves - those are directory and billing operations with no ARM representation, so
    they stay a manual step (see README.md).

    Safe to run repeatedly: ARM deployments are declarative, so a second run reconciles
    rather than duplicates.

.EXAMPLE
    ./deploy.ps1 -SubscriptionId 00000000-0000-0000-0000-000000000000

.EXAMPLE
    ./deploy.ps1 -SubscriptionId <id> -WhatIf
    Shows the resource changes without applying them.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string]$SubscriptionId,

    [string]$Location = 'westeurope',

    [string]$EnvironmentName = 'bikebuilder',

    [string]$ParameterFile = "$PSScriptRoot/main.bicepparam"
)

$ErrorActionPreference = 'Stop'

function Write-Step($message) { Write-Host "`n=== $message ===" -ForegroundColor Cyan }

# --- Preflight -------------------------------------------------------------------------

Write-Step 'Checking prerequisites'

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI (az) is not installed. See https://aka.ms/installazurecli'
}

$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    throw 'Not signed in. Run "az login" first (it opens a browser), then re-run this script.'
}

az account set --subscription $SubscriptionId
if ($LASTEXITCODE -ne 0) { throw "Could not select subscription $SubscriptionId." }
Write-Host "Subscription: $SubscriptionId" -ForegroundColor Green

# The free-tier resources below live in providers a fresh subscription often has not
# registered yet; registration is idempotent and takes a minute or two the first time.
Write-Step 'Registering resource providers'
$providers = @(
    'Microsoft.App', 'Microsoft.OperationalInsights', 'Microsoft.Sql', 'Microsoft.DocumentDB',
    'Microsoft.ServiceBus', 'Microsoft.SignalRService', 'Microsoft.Storage', 'Microsoft.Web',
    'Microsoft.Insights', 'Microsoft.ManagedIdentity'
)
foreach ($provider in $providers) {
    az provider register --namespace $provider --wait 2>$null | Out-Null
    Write-Host "  $provider" -ForegroundColor DarkGray
}

# --- Free-tier guardrails ---------------------------------------------------------------

Write-Step 'Checking free-tier availability'

# Cosmos DB allows exactly one free-tier account per subscription; a second one fails the
# whole deployment late, so surface it now.
$existingFreeCosmos = az cosmosdb list --query "[?enableFreeTier].name" -o tsv 2>$null
if ($existingFreeCosmos) {
    Write-Warning "This subscription already has a free-tier Cosmos account: $existingFreeCosmos"
    Write-Warning 'Only one is allowed per subscription. Delete it, or edit resources.bicep to set enableFreeTier: false (that account then bills at standard rates).'
}

# --- Deploy ------------------------------------------------------------------------------

$deploymentName = "bikebuilder-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

if ($PSCmdlet.ShouldProcess($SubscriptionId, 'Deploy BikeBuilder infrastructure')) {
    Write-Step 'Deploying infrastructure'
    az deployment sub create `
        --name $deploymentName `
        --location $Location `
        --template-file "$PSScriptRoot/main.bicep" `
        --parameters $ParameterFile `
        --output json | Out-Null

    if ($LASTEXITCODE -ne 0) { throw 'Deployment failed. Re-run with --debug, or check the deployment in the portal.' }
}
else {
    Write-Step 'What-if (no changes applied)'
    az deployment sub what-if `
        --name $deploymentName `
        --location $Location `
        --template-file "$PSScriptRoot/main.bicep" `
        --parameters $ParameterFile
    return
}

# --- Report ------------------------------------------------------------------------------

Write-Step 'Deployment outputs'
$outputs = (az deployment sub show --name $deploymentName --query properties.outputs -o json | ConvertFrom-Json)

[PSCustomObject]@{
    ResourceGroup = $outputs.resourceGroupName.value
    Storefront    = $outputs.webPublicUrl.value
    WasmApp       = $outputs.staticWebAppUrl.value
    CatalogApi    = $outputs.apiUrl.value
    OrdersApi     = $outputs.ordersUrl.value
    Functions     = $outputs.functionsUrl.value
    SqlServer     = $outputs.sqlServerFqdn.value
    AppIdentity   = $outputs.appIdentityName.value
} | Format-List

Write-Step 'Remaining manual steps'
Write-Host @"
Infrastructure is provisioned, but three things still need doing - see README.md:

  1. Grant the managed identity access to both SQL databases (Bicep cannot run T-SQL).
     Connect to $($outputs.sqlServerFqdn.value) as the Entra admin and, in EACH database, run:

         CREATE USER [$($outputs.appIdentityName.value)] FROM EXTERNAL PROVIDER;
         ALTER ROLE db_datareader ADD MEMBER [$($outputs.appIdentityName.value)];
         ALTER ROLE db_datawriter ADD MEMBER [$($outputs.appIdentityName.value)];
         ALTER ROLE db_ddladmin  ADD MEMBER [$($outputs.appIdentityName.value)];

     db_ddladmin is needed because the apps run EF Core migrations at startup.

  2. Publish container images and re-deploy with the image parameters set
     (see the apiImage / ordersImage / webPublicImage params in main.bicepparam).

  3. Publish the Functions app and the Blazor WebAssembly front end.

Cost note: everything here sits on a free grant except Service Bus, which has no free tier
at any SKU (Basic bills per operation - cents per month at this app's volume).
"@ -ForegroundColor Yellow
