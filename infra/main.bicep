// Subscription-scope entry point: creates the resource group, then deploys everything into
// it. Bicep cannot create the tenant or the subscription themselves - those are directory
// and billing operations, not ARM resources. See infra/README.md for that manual runbook.
targetScope = 'subscription'

@minLength(3)
@maxLength(12)
@description('Short name used to derive every resource name. Lowercase letters and digits only.')
param environmentName string = 'bikebuilder'

@description('Region for every resource. Must be one that offers the Azure SQL free offer and the Cosmos DB free tier.')
param location string = 'westeurope'

@description('Object id of the Entra user or group that becomes the SQL server admin. Get it with: az ad signed-in-user show --query id -o tsv')
param sqlAdminObjectId string

@description('Display name matching sqlAdminObjectId, shown as the Entra admin on the SQL server.')
param sqlAdminLoginName string

@description('Auth0 (or stub OIDC) issuer the APIs validate tokens against.')
param auth0Authority string = ''

@description('Auth0 API audience the APIs expect.')
param auth0Audience string = 'https://bikebuilder-api'

@description('Publisher email required by the API Management service (shown on its developer portal).')
param publisherEmail string

@description('Publisher display name required by the API Management service.')
param publisherName string = 'BikeBuilder'

@description('Image for the catalog API. Defaults to a placeholder so the first deployment succeeds before any image is published.')
param apiImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Image for the orders GraphQL service.')
param ordersImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Image for the public storefront.')
param webPublicImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@secure()
@description('Mailjet API key for order receipts. Leave empty to deploy without email.')
param mailjetApiKey string = ''

@secure()
@description('Mailjet secret key for order receipts.')
param mailjetSecretKey string = ''

@description('Sender address on order receipts; must be validated in the Mailjet account.')
param emailFromAddress string = 'orders@example.com'

@description('Sender display name on order receipts.')
param emailFromName string = 'BikeBuilder'

@description('Tags applied to every resource.')
param tags object = {
  application: 'bikebuilder'
  costTier: 'free'
}

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-11-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

module resources 'resources.bicep' = {
  name: 'bikebuilder-resources'
  scope: resourceGroup
  params: {
    environmentName: environmentName
    location: location
    sqlAdminObjectId: sqlAdminObjectId
    sqlAdminLoginName: sqlAdminLoginName
    auth0Authority: auth0Authority
    auth0Audience: auth0Audience
    publisherEmail: publisherEmail
    publisherName: publisherName
    apiImage: apiImage
    ordersImage: ordersImage
    webPublicImage: webPublicImage
    mailjetApiKey: mailjetApiKey
    mailjetSecretKey: mailjetSecretKey
    emailFromAddress: emailFromAddress
    emailFromName: emailFromName
    tags: tags
  }
}

output resourceGroupName string = resourceGroup.name
output apiUrl string = resources.outputs.apiUrl
output ordersUrl string = resources.outputs.ordersUrl
output webPublicUrl string = resources.outputs.webPublicUrl
output functionsUrl string = resources.outputs.functionsUrl
output staticWebAppUrl string = resources.outputs.staticWebAppUrl
output sqlServerFqdn string = resources.outputs.sqlServerFqdn
output appIdentityClientId string = resources.outputs.appIdentityClientId
output appIdentityName string = resources.outputs.appIdentityName
output functionAppName string = resources.outputs.functionAppName
output staticWebAppName string = resources.outputs.staticWebAppName
output signalRHostName string = resources.outputs.signalRHostName
output containerAppNames array = resources.outputs.containerAppNames
output apimGatewayUrl string = resources.outputs.apimGatewayUrl
output apimConfigEndpoint string = resources.outputs.apimConfigEndpoint
output apimName string = resources.outputs.apimName
