using 'main.bicep'

// Short name every resource is derived from. Changing it deploys a second, parallel stack.
param environmentName = 'bikebuilder'

// Must be a region that offers both the Azure SQL free offer and the Cosmos DB free tier.
param location = 'westeurope'

// Fill these in before deploying:
//   az ad signed-in-user show --query id  -o tsv   -> sqlAdminObjectId
//   az ad signed-in-user show --query userPrincipalName -o tsv -> sqlAdminLoginName
param sqlAdminObjectId = '00000000-0000-0000-0000-000000000000'
param sqlAdminLoginName = 'you@example.com'

// Auth0 tenant that guards the signed-in web app and the ratings/orders back-office APIs.
// Leave the authority empty to deploy with authentication switched off (guest storefront
// and anonymous catalog reads still work; the signed-in app will not).
param auth0Authority = ''
param auth0Audience = 'https://bikebuilder-api'

// Container images. Point these at your public GitHub Container Registry packages once the
// build workflow has pushed them; the placeholder default lets the first deploy succeed.
// param apiImage = 'ghcr.io/nickshepherd1983/bikebuilder-api:latest'
// param ordersImage = 'ghcr.io/nickshepherd1983/bikebuilder-orders:latest'
// param webPublicImage = 'ghcr.io/nickshepherd1983/bikebuilder-web-public:latest'
