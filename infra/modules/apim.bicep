// Azure API Management fronting the three backend APIs, plus the two self-hosted gateway
// registrations local development and the integration tests connect their gateway container
// to (see how-to-deploy-self-hosted-gateway-docker in the APIM docs).
//
// NOT free: Developer is the cheapest tier that supports self-hosted gateways (~$50/month,
// no SLA). Consumption cannot host them, which is the whole reason this tier is used.
//
// The route contract here (orders and ratings under a path prefix, the catalog api on the
// root path so gRPC-Web method paths need no prefix) is mirrored by the YARP fallback in
// Src/BikeBuilder.Gateway/appsettings.json; keep the two in sync.
targetScope = 'resourceGroup'

param name string
param location string
param tags object

@description('Shown on the developer portal and required by the service resource.')
param publisherEmail string
param publisherName string

@description('Cloud backend for the catalog API (the api container app URL).')
param apiBackendUrl string

@description('Cloud backend for the orders GraphQL service (the orders container app URL).')
param ordersBackendUrl string

@description('Cloud backend for the ratings API (the Functions app URL - clients append api/...).')
param ratingsBackendUrl string

resource apim 'Microsoft.ApiManagement/service@2024-05-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Developer'
    capacity: 1
  }
  properties: {
    publisherEmail: publisherEmail
    publisherName: publisherName
  }
}

// One API per backend. The catalog api owns the empty root path: gRPC-Web calls address
// fixed service paths (/bikebuilder.ComponentService/...) that Grpc.Net.Client cannot
// prefix, and APIM matches the most specific API path first, so /orders and /ratings still
// win over the root catch-all. protocols includes http because the self-hosted container
// serves plain http on 8080; subscriptions are off because browsers call anonymously.
var apis = [
  {
    name: 'catalog'
    path: ''
    serviceUrl: apiBackendUrl
    localDevUrl: 'http://host.docker.internal:7101'
    localTestUrl: 'http://host.docker.internal:18100'
  }
  {
    name: 'orders'
    path: 'orders'
    serviceUrl: ordersBackendUrl
    localDevUrl: 'http://host.docker.internal:7401'
    localTestUrl: 'http://host.docker.internal:18600'
  }
  {
    name: 'ratings'
    path: 'ratings'
    serviceUrl: ratingsBackendUrl
    localDevUrl: 'http://host.docker.internal:7071'
    localTestUrl: 'http://host.docker.internal:18500'
  }
]

resource apiResources 'Microsoft.ApiManagement/service/apis@2024-05-01' = [for api in apis: {
  parent: apim
  name: api.name
  properties: {
    displayName: 'BikeBuilder ${api.name}'
    path: api.path
    protocols: [
      'https'
      'http'
    ]
    serviceUrl: api.serviceUrl
    subscriptionRequired: false
  }
}]

// APIM answers 404 for any request that matches no operation - including CORS preflights -
// so every API gets a wildcard operation per verb. CORS itself stays in the backends (they
// validate the browser origins from WebAppOrigins); the gateway is a transparent hop.
var verbs = [
  'GET'
  'POST'
  'PUT'
  'DELETE'
  'PATCH'
  'HEAD'
  'OPTIONS'
]

var apiVerbPairs = flatten(map(range(0, length(apis)), apiIndex => map(verbs, verb => {
  apiIndex: apiIndex
  verb: verb
})))

resource operations 'Microsoft.ApiManagement/service/apis/operations@2024-05-01' = [for pair in apiVerbPairs: {
  parent: apiResources[pair.apiIndex]
  name: toLower('${pair.verb}-all')
  properties: {
    displayName: '${pair.verb} /*'
    method: pair.verb
    urlTemplate: '/*'
  }
}]

// The self-hosted gateways pull this same API config but must route to the developer
// machine instead of the cloud backends - and dev and test pin different local ports, hence
// two gateway registrations. context.Deployment.Gateway.Id identifies which gateway is
// evaluating the policy ('managed' in the cloud, which falls through to the serviceUrl).
var localGatewayNames = [
  'local-dev'
  'local-test'
]

var backendSwitchPolicy = '''
<policies>
  <inbound>
    <base />
    <choose>
      <when condition="@(context.Deployment.Gateway.Id == &quot;local-dev&quot;)">
        <set-backend-service base-url="{LOCAL_DEV_URL}" />
      </when>
      <when condition="@(context.Deployment.Gateway.Id == &quot;local-test&quot;)">
        <set-backend-service base-url="{LOCAL_TEST_URL}" />
      </when>
    </choose>
  </inbound>
  <backend>
    <base />
  </backend>
  <outbound>
    <base />
  </outbound>
  <on-error>
    <base />
  </on-error>
</policies>
'''

resource apiPolicies 'Microsoft.ApiManagement/service/apis/policies@2024-05-01' = [for (api, apiIndex) in apis: {
  parent: apiResources[apiIndex]
  name: 'policy'
  properties: {
    format: 'rawxml'
    value: replace(replace(backendSwitchPolicy, '{LOCAL_DEV_URL}', api.localDevUrl), '{LOCAL_TEST_URL}', api.localTestUrl)
  }
}]

resource gateways 'Microsoft.ApiManagement/service/gateways@2024-05-01' = [for gatewayName in localGatewayNames: {
  parent: apim
  name: gatewayName
  properties: {
    description: 'Self-hosted gateway run by the Aspire AppHost (${gatewayName}).'
    locationData: {
      name: gatewayName
    }
  }
}]

var gatewayApiPairs = flatten(map(range(0, length(localGatewayNames)), gatewayIndex => map(range(0, length(apis)), apiIndex => {
  gatewayIndex: gatewayIndex
  apiIndex: apiIndex
})))

resource gatewayApis 'Microsoft.ApiManagement/service/gateways/apis@2024-05-01' = [for pair in gatewayApiPairs: {
  parent: gateways[pair.gatewayIndex]
  name: apis[pair.apiIndex].name
  dependsOn: [
    apiResources[pair.apiIndex]
  ]
}]

output gatewayUrl string = apim.properties.gatewayUrl
output configEndpoint string = 'https://${apim.name}.configuration.azure-api.net'
output apimName string = apim.name
