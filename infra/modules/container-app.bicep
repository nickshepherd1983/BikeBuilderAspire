// One scale-to-zero Container App. Scale-to-zero is what keeps these inside the Container
// Apps free grant (180,000 vCPU-seconds/month): a single 0.25-vCPU replica left running
// around the clock would burn roughly 657,000 vCPU-seconds and blow past it.
param name string
param location string
param tags object
param environmentId string
param identityId string
param identityClientId string

@description('Image to run. Defaults to the Microsoft quickstart placeholder so the first deployment succeeds before any application image exists in the registry.')
param image string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Port the container listens on.')
param targetPort int = 8080

@description('Whether the app is reachable from the public internet.')
param externalIngress bool = true

@description('Plain environment variables as name/value objects.')
param env array = []

@description('Max replicas. Kept at 1 so a traffic spike cannot quietly run up a bill.')
param maxReplicas int = 1

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }
  properties: {
    environmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: externalIngress
        targetPort: targetPort
        transport: 'auto'
        allowInsecure: false
        // Blazor Server circuits and SignalR negotiate benefit from sticky sessions, and
        // this costs nothing at a single replica.
        stickySessions: {
          affinity: 'sticky'
        }
      }
    }
    template: {
      containers: [
        {
          name: name
          image: image
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: concat(env, [
            {
              name: 'AZURE_CLIENT_ID'
              value: identityClientId
            }
          ])
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: maxReplicas
      }
    }
  }
}

output fqdn string = containerApp.properties.configuration.ingress.fqdn
output url string = 'https://${containerApp.properties.configuration.ingress.fqdn}'
output name string = containerApp.name
