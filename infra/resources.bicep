// Every resource for a free-tier BikeBuilder deployment.
//
// What actually costs money here: Service Bus has no free tier at any SKU, so the Basic
// namespace bills $0.05 per million operations (cents/month at this app's volume). Log
// Analytics is free for the first 5 GB ingested per month. Everything else below sits on a
// documented always-free grant or the 12-month new-account allowance for blob storage.
targetScope = 'resourceGroup'

param environmentName string
param location string
param sqlAdminObjectId string
param sqlAdminLoginName string
param auth0Authority string
param auth0Audience string
param tags object

@description('Publisher email required by the API Management service resource.')
param publisherEmail string

@description('Publisher display name required by the API Management service resource.')
param publisherName string

// Container images. Azure Container Registry has no free SKU (Basic is roughly $5/month),
// so these default to GitHub Container Registry, which is free for public repositories and
// needs no pull credentials when the package is public. The placeholder default lets the
// very first deployment succeed before any image has been pushed.
@description('Image for the catalog API, e.g. ghcr.io/<owner>/bikebuilder-api:latest')
param apiImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Image for the orders GraphQL service.')
param ordersImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Image for the public storefront.')
param webPublicImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

// Globally-unique names (storage, SQL, Service Bus, SignalR, Cosmos all share a global namespace).
var suffix = uniqueString(resourceGroup().id)
var shortSuffix = substring(suffix, 0, 6)

// The Function App name is derived up front rather than read back off the resource: the
// storefront needs the Functions URL for SignalR negotiate, while the Functions CORS list
// needs the storefront URL, and referencing both resources directly is a dependency cycle.
var functionAppName = 'func-${environmentName}-${shortSuffix}'
#disable-next-line no-hardcoded-env-urls
var functionAppUrl = 'https://${functionAppName}.azurewebsites.net'

// Same up-front-derivation trick for the storefront's URL: the api and orders apps need it
// for CORS (WebAppOrigins), but referencing webPublicApp.outputs.url from apiApp would be a
// cycle - webPublicApp already depends on apiApp for its service-discovery address.
var webPublicAppName = 'ca-${environmentName}-web-public'

// ---------------------------------------------------------------------------------------
// Identity - one user-assigned identity shared by every compute resource, so all the RBAC
// below is granted once rather than per-app.
// ---------------------------------------------------------------------------------------

resource appIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${environmentName}'
  location: location
  tags: tags
}

// ---------------------------------------------------------------------------------------
// Observability - Log Analytics' first 5 GB/month is free; retention pinned to the 30-day
// free floor so it stays that way.
// ---------------------------------------------------------------------------------------

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${environmentName}'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: json('0.5')
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${environmentName}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ---------------------------------------------------------------------------------------
// Storage - component images, plus the Functions host's own runtime storage.
// ---------------------------------------------------------------------------------------

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'st${environmentName}${shortSuffix}'
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource componentImages 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'component-images'
  properties: {
    publicAccess: 'None'
  }
}

// ---------------------------------------------------------------------------------------
// Service Bus - Basic is the only sensible SKU here: it supports queues (all this app uses)
// and costs per-operation. Basic cannot do topics, which is fine - there is one queue.
// ---------------------------------------------------------------------------------------

resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: 'sb-${environmentName}-${shortSuffix}'
  location: location
  tags: tags
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    disableLocalAuth: true
    minimumTlsVersion: '1.2'
  }
}

resource notificationsQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBus
  name: 'bikebuilder-notifications'
  properties: {
    maxDeliveryCount: 10
    // Basic tier caps TTL at 14 days; notifications are worthless long before that.
    defaultMessageTimeToLive: 'PT1H'
  }
}

// ---------------------------------------------------------------------------------------
// SignalR - Free_F1 in Serverless mode. Serverless accepts client connections only (no
// server connections), which is exactly what lets every app scale to zero: the Function
// broadcasts through the REST/management surface instead of holding a hub server open.
// Free tier ceiling: 20 concurrent connections and 20,000 messages/day.
// ---------------------------------------------------------------------------------------

resource signalR 'Microsoft.SignalRService/signalR@2023-02-01' = {
  name: 'sigr-${environmentName}-${shortSuffix}'
  location: location
  tags: tags
  sku: {
    name: 'Free_F1'
    tier: 'Free'
    capacity: 1
  }
  kind: 'SignalR'
  properties: {
    features: [
      {
        flag: 'ServiceMode'
        value: 'Serverless'
      }
    ]
    cors: {
      allowedOrigins: [
        '*'
      ]
    }
  }
}

// ---------------------------------------------------------------------------------------
// Azure SQL - the free offer allows up to 10 databases per subscription, each with 100,000
// vCore-seconds and 32 GB. Both of this app's bounded contexts fit. AutoPause on exhaustion
// is the important bit: when the monthly grant runs out the database stops rather than
// silently billing at General Purpose serverless rates.
// ---------------------------------------------------------------------------------------

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: 'sql-${environmentName}-${shortSuffix}'
  location: location
  tags: tags
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    // Entra-only: no SQL logins, no password to store anywhere.
    administrators: {
      administratorType: 'ActiveDirectory'
      login: sqlAdminLoginName
      sid: sqlAdminObjectId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true
      principalType: 'User'
    }
  }
}

// Container Apps egress IPs are not stable on the Consumption profile, so the app tier is
// reached through the "allow Azure services" "0.0.0.0" pseudo-rule rather than a real range.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

var freeDatabaseProperties = {
  collation: 'SQL_Latin1_General_CP1_CI_AS'
  maxSizeBytes: 34359738368
  autoPauseDelay: 60
  minCapacity: json('0.5')
  useFreeLimit: true
  freeLimitExhaustionBehavior: 'AutoPause'
  zoneRedundant: false
}

var freeDatabaseSku = {
  name: 'GP_S_Gen5_2'
  tier: 'GeneralPurpose'
  family: 'Gen5'
  capacity: 2
}

resource catalogDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'BikeBuilderDb'
  location: location
  tags: tags
  sku: freeDatabaseSku
  properties: freeDatabaseProperties
}

resource ordersDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'BikeBuilderOrdersDb'
  location: location
  tags: tags
  sku: freeDatabaseSku
  properties: freeDatabaseProperties
}

// ---------------------------------------------------------------------------------------
// Cosmos DB - free tier gives 1000 RU/s and 25 GB, and is limited to ONE account per
// subscription. The database takes exactly the free 1000 RU/s as shared throughput.
// ---------------------------------------------------------------------------------------

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: 'cosmos-${environmentName}-${shortSuffix}'
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    enableFreeTier: true
    disableLocalAuth: true
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
  }
}

resource cosmosDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: cosmos
  name: 'bikebuilder'
  properties: {
    resource: {
      id: 'bikebuilder'
    }
    options: {
      throughput: 1000
    }
  }
}

resource ratingsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: cosmosDatabase
  name: 'ratings'
  properties: {
    resource: {
      id: 'ratings'
      partitionKey: {
        paths: [
          '/bikeBuildId'
        ]
        kind: 'Hash'
      }
    }
  }
}

// ---------------------------------------------------------------------------------------
// RBAC - every data plane reached with the managed identity, so nothing stores a key or a
// connection string with a secret in it.
// ---------------------------------------------------------------------------------------

var storageBlobDataContributor = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
var serviceBusDataOwner = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '090c5cfd-751d-490a-894a-3ce6f1109419')
var signalRServiceOwner = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7e4f1700-ea5a-4f59-8f37-079cfe29dce3')
var monitoringMetricsPublisher = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '3913510d-42f4-4e42-8a64-420c390055eb')

resource storageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, appIdentity.id, storageBlobDataContributor)
  properties: {
    roleDefinitionId: storageBlobDataContributor
    principalId: appIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource serviceBusRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: serviceBus
  name: guid(serviceBus.id, appIdentity.id, serviceBusDataOwner)
  properties: {
    roleDefinitionId: serviceBusDataOwner
    principalId: appIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource signalRRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: signalR
  name: guid(signalR.id, appIdentity.id, signalRServiceOwner)
  properties: {
    roleDefinitionId: signalRServiceOwner
    principalId: appIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource metricsRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: appInsights
  name: guid(appInsights.id, appIdentity.id, monitoringMetricsPublisher)
  properties: {
    roleDefinitionId: monitoringMetricsPublisher
    principalId: appIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Cosmos data-plane access is its own role system, separate from Azure RBAC.
resource cosmosDataContributor 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: cosmos
  name: guid(cosmos.id, appIdentity.id, 'data-contributor')
  properties: {
    roleDefinitionId: '${cosmos.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
    principalId: appIdentity.properties.principalId
    scope: cosmos.id
  }
}

// ---------------------------------------------------------------------------------------
// Container Apps - the three ASP.NET Core services, all scaled to zero.
// ---------------------------------------------------------------------------------------

resource containerEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${environmentName}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
}

var sqlConnectionBase = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Encrypt=True;TrustServerCertificate=False;Authentication=Active Directory Default;'

var webPublicOrigin = 'https://${webPublicAppName}.${containerEnv.properties.defaultDomain}'
var staticWebAppOrigin = 'https://${staticWebApp.properties.defaultHostname}'

// The services validate browser origins themselves (the gateway is a transparent hop), and
// the deployed browsers live on the Static Web App (signed-in web app) and the storefront.
var webAppOriginsEnv = [
  {
    name: 'WebAppOrigins__0'
    value: staticWebAppOrigin
  }
  {
    name: 'WebAppOrigins__1'
    value: webPublicOrigin
  }
]

var commonEnv = [
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    value: appInsights.properties.ConnectionString
  }
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: 'Production'
  }
  {
    name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
    value: 'true'
  }
]

module apiApp 'modules/container-app.bicep' = {
  name: 'app-api'
  params: {
    name: 'ca-${environmentName}-api'
    location: location
    tags: tags
    environmentId: containerEnv.id
    identityId: appIdentity.id
    identityClientId: appIdentity.properties.clientId
    image: apiImage
    env: concat(commonEnv, webAppOriginsEnv, [
      {
        name: 'ConnectionStrings__BikeBuilderDb'
        value: '${sqlConnectionBase}Database=BikeBuilderDb;'
      }
      {
        name: 'ConnectionStrings__servicebus'
        value: '${serviceBus.name}.servicebus.windows.net'
      }
      {
        name: 'ConnectionStrings__component-images'
        value: '${storage.properties.primaryEndpoints.blob}component-images'
      }
      {
        name: 'Auth0__Authority'
        value: auth0Authority
      }
      {
        name: 'Auth0__Audience'
        value: auth0Audience
      }
    ])
  }
}

module ordersApp 'modules/container-app.bicep' = {
  name: 'app-orders'
  params: {
    name: 'ca-${environmentName}-orders'
    location: location
    tags: tags
    environmentId: containerEnv.id
    identityId: appIdentity.id
    identityClientId: appIdentity.properties.clientId
    image: ordersImage
    env: concat(commonEnv, webAppOriginsEnv, [
      {
        name: 'ConnectionStrings__BikeBuilderOrdersDb'
        value: '${sqlConnectionBase}Database=BikeBuilderOrdersDb;'
      }
      {
        name: 'ConnectionStrings__servicebus'
        value: '${serviceBus.name}.servicebus.windows.net'
      }
      {
        name: 'services__api__https__0'
        value: apiApp.outputs.url
      }
      {
        name: 'Auth0__Authority'
        value: auth0Authority
      }
      {
        name: 'Auth0__Audience'
        value: auth0Audience
      }
    ])
  }
}

module webPublicApp 'modules/container-app.bicep' = {
  name: 'app-web-public'
  params: {
    name: webPublicAppName
    location: location
    tags: tags
    environmentId: containerEnv.id
    identityId: appIdentity.id
    identityClientId: appIdentity.properties.clientId
    image: webPublicImage
    env: concat(commonEnv, [
      {
        name: 'services__api__https__0'
        value: apiApp.outputs.url
      }
      {
        name: 'services__orders__https__0'
        value: ordersApp.outputs.url
      }
      // The storefront no longer hosts the notification hub; its browser clients negotiate
      // against the Functions app and connect straight to SignalR Service.
      {
        name: 'NotificationsNegotiateBaseAddress'
        value: functionAppUrl
      }
    ])
  }
}

// ---------------------------------------------------------------------------------------
// Functions - Consumption (Y1) carries the clearest always-free grant: 1,000,000 executions
// and 400,000 GB-seconds per month. Hosts both the ratings API and the Service Bus ->
// SignalR notification fan-out that replaced the storefront's always-on background service.
// ---------------------------------------------------------------------------------------

resource functionPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'plan-${environmentName}-func'
  location: location
  tags: tags
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  properties: {
    reserved: true
  }
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${appIdentity.id}': {}
    }
  }
  properties: {
    serverFarmId: functionPlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|10.0'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      cors: {
        // Both front ends call the negotiate endpoint cross-origin.
        allowedOrigins: [
          webPublicApp.outputs.url
          'https://${staticWebApp.properties.defaultHostname}'
        ]
        supportCredentials: false
      }
      appSettings: [
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storage.name
        }
        {
          name: 'AzureWebJobsStorage__credential'
          value: 'managedidentity'
        }
        {
          name: 'AzureWebJobsStorage__clientId'
          value: appIdentity.properties.clientId
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'AZURE_CLIENT_ID'
          value: appIdentity.properties.clientId
        }
        // Identity-based Service Bus trigger connection ("servicebus" prefix + __fullyQualifiedNamespace).
        {
          name: 'servicebus__fullyQualifiedNamespace'
          value: '${serviceBus.name}.servicebus.windows.net'
        }
        {
          name: 'servicebus__credential'
          value: 'managedidentity'
        }
        {
          name: 'servicebus__clientId'
          value: appIdentity.properties.clientId
        }
        // Identity-based SignalR binding connection.
        {
          name: 'AzureSignalRConnectionString__serviceUri'
          value: 'https://${signalR.properties.hostName}'
        }
        {
          name: 'AzureSignalRConnectionString__credential'
          value: 'managedidentity'
        }
        {
          name: 'AzureSignalRConnectionString__clientId'
          value: appIdentity.properties.clientId
        }
        {
          name: 'ConnectionStrings__cosmos'
          value: cosmos.properties.documentEndpoint
        }
        {
          name: 'Auth0__Authority'
          value: auth0Authority
        }
        {
          name: 'Auth0__Audience'
          value: auth0Audience
        }
        // The worker's hand-rolled CorsMiddleware reads these; the site-level cors block
        // above only covers the host-handled negotiate path.
        {
          name: 'WebAppOrigins__0'
          value: staticWebAppOrigin
        }
        {
          name: 'WebAppOrigins__1'
          value: webPublicOrigin
        }
      ]
    }
  }
}

// ---------------------------------------------------------------------------------------
// Static Web Apps - the Blazor WebAssembly front end. Genuinely free, including TLS and a
// custom domain.
// ---------------------------------------------------------------------------------------

resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = {
  name: 'stapp-${environmentName}-${shortSuffix}'
  location: location
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    // Content is pushed by the SWA CLI / GitHub Action rather than built by Azure.
    buildProperties: {
      skipGithubActionWorkflowGeneration: true
    }
  }
}

// ---------------------------------------------------------------------------------------
// API Management - the browser-facing edge for the three APIs, and the config source for
// the local self-hosted gateway container the AppHost runs. NOT free: Developer tier is
// ~$50/month, the one deliberately paid resource in this stack (see infra/README.md).
// ---------------------------------------------------------------------------------------

module apim 'modules/apim.bicep' = {
  name: 'apim'
  params: {
    name: 'apim-${environmentName}-${shortSuffix}'
    location: location
    tags: tags
    publisherEmail: publisherEmail
    publisherName: publisherName
    apiBackendUrl: apiApp.outputs.url
    ordersBackendUrl: ordersApp.outputs.url
    ratingsBackendUrl: functionAppUrl
  }
}

output apiUrl string = apiApp.outputs.url
output ordersUrl string = ordersApp.outputs.url
output webPublicUrl string = webPublicApp.outputs.url
output functionsUrl string = 'https://${functionApp.properties.defaultHostName}'
output functionAppName string = functionApp.name
output staticWebAppUrl string = 'https://${staticWebApp.properties.defaultHostname}'
output staticWebAppName string = staticWebApp.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output appIdentityClientId string = appIdentity.properties.clientId
output appIdentityName string = appIdentity.name
output signalRHostName string = signalR.properties.hostName
output serviceBusNamespace string = '${serviceBus.name}.servicebus.windows.net'
output storageAccountName string = storage.name
output cosmosEndpoint string = cosmos.properties.documentEndpoint
output containerAppNames array = [
  apiApp.outputs.name
  ordersApp.outputs.name
  webPublicApp.outputs.name
]
output apimGatewayUrl string = apim.outputs.gatewayUrl
output apimConfigEndpoint string = apim.outputs.configEndpoint
output apimName string = apim.outputs.apimName
