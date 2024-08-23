metadata description = 'Creates an Azure Function in an existing Azure App Service plan.'
param name string
param location string = resourceGroup().location
param tags object = {}

// Reference Properties
param applicationInsightsName string = ''
param managedEnvironmentId string
param keyVaultName string = ''
param managedIdentity bool = !empty(keyVaultName)
param storageAccountName string
param containerRegistryName string

// Runtime Properties
@allowed([
  'dotnet'
  'dotnetcore'
  'dotnet-isolated'
  'node'
  'python'
  'java'
  'powershell'
  'custom'
])
param runtimeName string
param runtimeNameAndVersion string = '${runtimeName}|${runtimeVersion}'
param runtimeVersion string

// Function Settings
@allowed([
  '~4'
  '~3'
  '~2'
  '~1'
])
param extensionVersion string = '~4'

// Microsoft.Web/sites Properties
param kind string
// Microsoft.Web/sites/config
param allowedOrigins array = []
@secure()
param appSettings object = {}
param linuxFxVersion string = runtimeNameAndVersion
@description('Specifies if the resource already exists')
param exists bool = false

resource existingApp 'Microsoft.Web/sites@2023-12-01' existing = if (exists) {
  name: name
}
resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' existing = {
  name: containerRegistryName
}

resource storage 'Microsoft.Storage/storageAccounts@2021-09-01' existing = {
  name: storageAccountName
}
var storageCs = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'

module function 'appservice-func-container.bicep' = {
  name: '${name}-update'
  params: {
    name: name
    location: location
    tags: tags
    allowedOrigins: allowedOrigins
    alwaysOn: false
    appSettings: union(appSettings, {
      AzureWebJobsStorage: storageCs
      FUNCTIONS_EXTENSION_VERSION: extensionVersion
      FUNCTIONS_WORKER_RUNTIME: runtimeName
      WEBSITE_CONTENTAZUREFILECONNECTIONSTRING: storageCs
      WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED: '1'
      DOCKER_REGISTRY_SERVER_URL: acr.properties.loginServer
      DOCKER_REGISTRY_SERVER_USERNAME: acr.listCredentials().username
      DOCKER_REGISTRY_SERVER_PASSWORD: acr.listCredentials().passwords[0].value
    })
    applicationInsightsName: applicationInsightsName
    managedEnvironmentId: managedEnvironmentId
    keyVaultName: keyVaultName
    runtimeName: 'dotnet-isolated'
    runtimeVersion: '8.0'
    scmDoBuildDuringDeployment: false
    linuxFxVersion: !empty(linuxFxVersion)
      ? linuxFxVersion
      : exists
          ? existingApp.properties.siteConfig.linuxFxVersion
          : 'Docker|mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0'
    kind: kind
  }
}

output identityPrincipalId string = managedIdentity ? function.outputs.identityPrincipalId : ''
output name string = function.outputs.name
output uri string = function.outputs.uri
output linuxFxVersion string = function.outputs.linuxFxVersion
