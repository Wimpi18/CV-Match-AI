param location string = resourceGroup().location
param projectName string = 'cvmatchai'
param environmentName string = 'prod'

@secure()
param sqlAdminPassword string

var uniqueSuffix = uniqueString(resourceGroup().id)
var logAnalyticsWorkspaceName = 'log-${projectName}-${environmentName}-${uniqueSuffix}'
var appInsightsName = 'appinsights-${projectName}-${environmentName}-${uniqueSuffix}'
var acrName = 'cr${projectName}${uniqueSuffix}'
var containerAppEnvName = 'cae-${projectName}-${environmentName}-${uniqueSuffix}'
var sqlServerName = 'sql-${projectName}-${environmentName}-${uniqueSuffix}'
var sqlDbName = 'sqldb-${projectName}-${environmentName}'
var cosmosDbAccountName = 'cosmos-${projectName}-${environmentName}-${uniqueSuffix}'
var storageAccountName = 'st${projectName}${environmentName}${uniqueSuffix}'
var docIntelName = 'cog-docintel-${projectName}-${environmentName}-${uniqueSuffix}'
var openaiName = 'cog-openai-${projectName}-${environmentName}-${uniqueSuffix}'
var keyVaultName = take('kv-${projectName}-${environmentName}-${uniqueSuffix}', 24)

// 1. Log Analytics Workspace (shared logging)
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2025-07-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  properties: {
    sku: {
      name: 'Free'
    }
    retentionInDays: 7
  }
}

// 2. Application Insights (telemetry)
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
    RetentionInDays: 7
  }
}

// 3. Azure Container Registry (ACR)
resource acr 'Microsoft.ContainerRegistry/registries@2025-11-01' = {
  name: acrName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: true
  }
}

// 4. Container Apps Environment
resource containerAppEnv 'Microsoft.App/managedEnvironments@2026-01-01' = {
  name: containerAppEnvName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsWorkspace.properties.customerId
        sharedKey: logAnalyticsWorkspace.listKeys().primarySharedKey
      }
    }
  }
}

// 5. Azure SQL Server & Database (Core Transactions)
resource sqlServer 'Microsoft.Sql/servers@2025-01-01' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: 'wimpisqladmin'
    administratorLoginPassword: sqlAdminPassword
    version: '17.0'
  }
}

resource sqlFirewallAllowAllAzure 'Microsoft.Sql/servers/firewallRules@2025-01-01' = {
  parent: sqlServer
  name: 'AllowAllAzureIPs'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2025-01-01' = {
  parent: sqlServer
  name: sqlDbName
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
    capacity: 5
  }
}

// 6. Cosmos DB (NoSQL Brain - Serverless)
resource cosmosDbAccount 'Microsoft.DocumentDB/databaseAccounts@2026-03-15' = {
  name: cosmosDbAccountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
  }
}

resource cosmosDb 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2026-03-15' = {
  parent: cosmosDbAccount
  name: 'cvmatch-store'
  properties: {
    resource: {
      id: 'cvmatch-store'
    }
  }
}

resource cosmosContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2026-03-15' = {
  parent: cosmosDb
  name: 'resumes'
  properties: {
    resource: {
      id: 'resumes'
      partitionKey: {
        paths: [
          '/userId'
        ]
        kind: 'Hash'
      }
    }
  }
}

// 7. Azure Storage Account (PDF CV Ingestion)
resource storageAccount 'Microsoft.Storage/storageAccounts@2026-04-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2026-04-01' = {
  parent: storageAccount
  name: 'default'
}

resource blobContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2026-04-01' = {
  parent: blobService
  name: 'resumes-pdf'
  properties: {
    publicAccess: 'None'
  }
}

// 8. Azure AI Document Intelligence
resource docIntel 'Microsoft.CognitiveServices/accounts@2026-03-01' = {
  name: docIntelName
  location: location
  kind: 'FormRecognizer'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: docIntelName
    publicNetworkAccess: 'Enabled'
  }
}

// 9. Azure OpenAI Service
resource openai 'Microsoft.CognitiveServices/accounts@2026-03-01' = {
  name: openaiName
  location: location
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: openaiName
    publicNetworkAccess: 'Enabled'
  }
}

// Deployment of DeepSeek model inside OpenAI
resource openaiModel 'Microsoft.CognitiveServices/accounts/deployments@2026-03-01' = {
  parent: openai
  name: 'deepseek-v4-pro'
  properties: {
    model: {
      format: 'DeepSeek'
      name: 'deepseek-v4-pro'
      version: '2026-04-23'
    }
    scaleSettings: {
      scaleType: 'Standard'
      capacity: 10
    }
  }
}

// 10. Azure Key Vault (Secrets Management)
resource keyVault 'Microsoft.KeyVault/vaults@2026-02-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    publicNetworkAccess: 'Enabled'
  }
}

resource sqlAdminPasswordSecret 'Microsoft.KeyVault/vaults/secrets@2026-02-01' = {
  parent: keyVault
  name: 'sqlAdminPassword'
  properties: {
    value: sqlAdminPassword
  }
}

output acrLoginServer string = acr.properties.loginServer
output sqlConnectionString string = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDbName};Persist Security Info=False;User ID=wimpisqladmin;MultipleActiveResultSets=True;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
output cosmosDbEndpoint string = cosmosDbAccount.properties.documentEndpoint
output storageAccountName string = storageAccountName
output docIntelEndpoint string = docIntel.properties.endpoint
output openaiEndpoint string = openai.properties.endpoint
output keyVaultUri string = keyVault.properties.vaultUri
