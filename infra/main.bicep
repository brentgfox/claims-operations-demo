targetScope = 'resourceGroup'

@description('Existing Container Apps environment in this resource group.')
param environmentName string

@description('Existing container registry in this resource group.')
param registryName string

@description('Existing Azure Monitor action group used by the SRE Agent.')
param actionGroupName string

param location string = resourceGroup().location
param namePrefix string = 'claims'
param imageTag string = '20261124'
param provisionApplications bool = true
param alertEnabled bool = false
param resourceTags { *: string } = resourceGroup().tags

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: environmentName
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' existing = {
  name: actionGroupName
}

resource pullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${namePrefix}-acrpull'
  location: location
  tags: resourceTags
}

var acrPullRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, pullIdentity.id, acrPullRoleId)
  scope: registry
  properties: {
    roleDefinitionId: acrPullRoleId
    principalId: pullIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource api 'Microsoft.App/containerApps@2024-03-01' = if (provisionApplications) {
  name: 'ca-${namePrefix}-api'
  location: location
  tags: resourceTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${pullIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: pullIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'claims-api'
          image: '${registry.properties.loginServer}/claims-api:${imageTag}'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'CLAIMS_API_VERSION', value: 'v1' }
            { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
          ]
          probes: [
            {
              type: 'Readiness'
              httpGet: { path: '/health', port: 8080 }
              initialDelaySeconds: 5
              periodSeconds: 10
            }
            {
              type: 'Liveness'
              httpGet: { path: '/health', port: 8080 }
              initialDelaySeconds: 15
              periodSeconds: 20
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

resource frontend 'Microsoft.App/containerApps@2024-03-01' = if (provisionApplications) {
  name: 'ca-${namePrefix}-frontend'
  location: location
  tags: resourceTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${pullIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 80
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: pullIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'claims-frontend'
          image: '${registry.properties.loginServer}/claims-frontend:${imageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'CLAIMS_API_HOST'
              value: api.?properties.configuration.ingress.fqdn ?? ''
            }
          ]
          probes: [
            {
              type: 'Readiness'
              httpGet: { path: '/health', port: 80 }
              initialDelaySeconds: 5
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

resource alert 'Microsoft.Insights/metricAlerts@2018-03-01' = if (provisionApplications) {
  name: 'alert-claims-routing-5xx'
  location: 'global'
  tags: resourceTags
  properties: {
    description: 'Synthetic claims demo: review-routing requests return HTTP 5xx.'
    enabled: alertEnabled
    severity: 2
    scopes: [
      api.id
    ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    autoMitigate: true
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'routing5xx'
          criterionType: 'StaticThresholdCriterion'
          metricNamespace: 'microsoft.app/containerapps'
          metricName: 'Requests'
          timeAggregation: 'Total'
          operator: 'GreaterThan'
          threshold: 5
          dimensions: [
            { name: 'statusCodeCategory', operator: 'Include', values: ['5xx'] }
          ]
        }
      ]
    }
    actions: [
      { actionGroupId: actionGroup.id }
    ]
  }
}

output imagePullPrincipalId string = pullIdentity.properties.principalId
output imagePullIdentityId string = pullIdentity.id
output registryId string = registry.id
output apiUrl string = provisionApplications ? 'https://${api.?properties.configuration.ingress.fqdn ?? ''}' : ''
output frontendUrl string = provisionApplications ? 'https://${frontend.?properties.configuration.ingress.fqdn ?? ''}' : ''
