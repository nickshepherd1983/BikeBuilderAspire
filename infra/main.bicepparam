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

// API Management requires a publisher contact. NOTE: APIM is the one deliberately paid
// resource in this stack - the Developer tier (~$50/month) is the cheapest that supports
// the self-hosted gateways local dev and the integration tests connect to.
param publisherEmail = 'you@example.com'
param publisherName = 'BikeBuilder'

// Order receipts via Mailjet. The keys come from the environment rather than this file so
// they never land in git: `deploy.ps1 -MailjetApiKey ... -MailjetSecretKey ...` sets them
// for the deployment. Both empty (the default) deploys with email switched off. The sender
// address must be a validated sender or domain in the Mailjet account.
param mailjetApiKey = readEnvironmentVariable('BIKEBUILDER_MAILJET_API_KEY', '')
param mailjetSecretKey = readEnvironmentVariable('BIKEBUILDER_MAILJET_SECRET_KEY', '')
param emailFromAddress = 'orders@example.com'
param emailFromName = 'BikeBuilder'

// Container images. Point these at your public GitHub Container Registry packages once the
// build workflow has pushed them; the placeholder default lets the first deploy succeed.
// param apiImage = 'ghcr.io/nickshepherd1983/bikebuilder-api:latest'
// param ordersImage = 'ghcr.io/nickshepherd1983/bikebuilder-orders:latest'
// param webPublicImage = 'ghcr.io/nickshepherd1983/bikebuilder-web-public:latest'
