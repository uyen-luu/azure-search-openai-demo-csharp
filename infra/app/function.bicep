param name string
param location string = resourceGroup().location
param tags object = {}

param allowedOrigins array = []
param applicationInsightsName string = ''
param managedEnvironmentId string
@secure()
param appSettings object = {}
param keyVaultName string
param serviceName string = 'func'
param storageAccountName string
param kind string = 'functionapp,linux,container,azurecontainerapps'
param linuxFxVersion string
@description('Specifies if the resource exists')
param exists bool
param containerRegistryName string
@description('The OpenAI Embedding deployment name')

module function '../core/host/functions-upsert.bicep' = {
  name: 'ca-${serviceName}'
  params: {
    name: name
    location: location
    tags: union(tags, { 'azd-service-name': serviceName })
    allowedOrigins: allowedOrigins
    appSettings: appSettings
    applicationInsightsName: applicationInsightsName
    managedEnvironmentId: managedEnvironmentId
    keyVaultName: keyVaultName
    runtimeName: 'dotnet-isolated'
    runtimeVersion: '8.0'
    storageAccountName: storageAccountName
    linuxFxVersion: linuxFxVersion
    kind: kind
    exists: exists
    containerRegistryName: containerRegistryName
  }
}

output SERVICE_FUNCTION_IDENTITY_PRINCIPAL_ID string = function.outputs.identityPrincipalId
output SERVICE_FUNCTION_NAME string = function.outputs.name
output SERVICE_FUNCTION_URI string = function.outputs.uri
output SERVICE_FUNCTION__IMAGE_NAME string = function.outputs.linuxFxVersion
