#!/bin/bash

export ROOT_NAME='mvsample'
export LOCATION='eastus'

export RESOURCE_GROUP=$ROOT_NAME
export STORAGE_ACCOUNT="${ROOT_NAME}storage"
export COSMOSDB_SERVER_NAME=$ROOT_NAME
export COSMOSDB_DATABASE_NAME=$ROOT_NAME
export COSMOSDB_COLLECTION_NAME_RAW='raw'
export COSMOSDB_COLLECTION_NAME_MV='view'
export COSMOSDB_RU=1000
export PLAN_NAME="${ROOT_NAME}plan"
export FUNCTIONAPP_NAME="MaterializedViewProcessor"

echo "starting deployment: $ROOT_NAME"

PP=$( cd "$(dirname "${BASH_SOURCE[0]}")" ; pwd -P )

mkdir $PP/logs &>/dev/null

set -euo pipefail

echo "checking prerequisites"

HAS_AZ=`command -v az`
if [ -z HAS_AZ ]; then
    echo "AZ CLI not found"
    echo "please install it as described here:"
    echo "https://docs.microsoft.com/en-us/cli/azure/install-azure-cli-apt?view=azure-cli-latest"
    exit 1
fi

HAS_ZIP=`command -v zip`
if [ -z HAS_ZIP ]; then
    echo "zip not found"
    echo "please install it as it is needed by the script"
    exit 1
fi

HAS_DOTNET=`command -v dotnet`
if [ -z HAS_DOTNET ]; then
    echo "dotnet not found"
    echo "please install .NET Core it as it is needed by the script"
    echo "https://dotnet.microsoft.com/download"
    exit 1
fi

echo 'creating resource group'
az group create -n $RESOURCE_GROUP -l $LOCATION -o json \
1> $PP/logs/010-group-create.log

echo 'creating storage account'
az storage account create -n $STORAGE_ACCOUNT -g $RESOURCE_GROUP --sku Standard_LRS \
1> $PP/logs/020-storage-account.log

echo 'creating cosmosdb account'
SERVER_EXISTS=`az cosmosdb check-name-exists -n $COSMOSDB_SERVER_NAME -o tsv`
if [ $SERVER_EXISTS == "false" ]; then
    az cosmosdb create -g $RESOURCE_GROUP -n $COSMOSDB_SERVER_NAME \
    -o json \
    1> $PP/logs/030-cosmosdb-create.log
fi

echo 'creating cosmosdb database'
DB_EXISTS=`az cosmosdb database exists -g $RESOURCE_GROUP -n $COSMOSDB_SERVER_NAME --db-name $COSMOSDB_DATABASE_NAME -o tsv`
if [ $DB_EXISTS == "false" ]; then
    az cosmosdb database create -g $RESOURCE_GROUP -n $COSMOSDB_SERVER_NAME \
        --db-name $COSMOSDB_DATABASE_NAME \
        -o json \
        1> $PP/logs/040-cosmosdb-database-create.log
fi

echo 'creating cosmosdb raw collection'
COLLECTION_EXISTS=`az cosmosdb collection exists -g $RESOURCE_GROUP -n $COSMOSDB_SERVER_NAME --db-name $COSMOSDB_DATABASE_NAME --collection-name $COSMOSDB_COLLECTION_NAME_RAW -o tsv`
if [ $COLLECTION_EXISTS == "false" ]; then
    az cosmosdb collection create -g $RESOURCE_GROUP -n $COSMOSDB_SERVER_NAME -d $COSMOSDB_DATABASE_NAME \
    --collection-name $COSMOSDB_COLLECTION_NAME_RAW \
    --partition-key-path "/deviceId" \
    --indexing-policy '{ "indexingMode": "none", "automatic": false }' \
    --throughput $COSMOSDB_RU \
    -o json \
    1> $PP/logs/050-cosmosdb-collection-create-raw.log
fi

echo 'creating cosmosdb view collection'
COLLECTION_EXISTS=`az cosmosdb collection exists -g $RESOURCE_GROUP -n $COSMOSDB_SERVER_NAME --db-name $COSMOSDB_DATABASE_NAME --collection-name $COSMOSDB_COLLECTION_NAME_MV -o tsv`
if [ $COLLECTION_EXISTS == "false" ]; then
    az cosmosdb collection create -g $RESOURCE_GROUP -n $COSMOSDB_SERVER_NAME -d $COSMOSDB_DATABASE_NAME \
    --collection-name $COSMOSDB_COLLECTION_NAME_MV \
    --partition-key-path "/deviceId" \
    --indexing-policy '{ "indexingMode": "none", "automatic": false }' \
    --throughput $COSMOSDB_RU \
    -o json \
   1> $PP/logs/060-cosmosdb-collection-create-mv.log
fi

echo 'creating appinsights'
az resource create --resource-group $RESOURCE_GROUP --resource-type "Microsoft.Insights/components" \
--name $ROOT_NAME --location $LOCATION --properties '{"ApplicationId":"$ROOT_NAME","Application_Type":"other","Flow_Type":"Redfield"}' \
-o json \
1> $PP/logs/080-appinsights.log

echo 'getting appinsights instrumentation key'
APPINSIGHTS_INSTRUMENTATIONKEY=`az resource show -g $RESOURCE_GROUP -n $ROOT_NAME --resource-type "Microsoft.Insights/components" --query properties.InstrumentationKey -o tsv`

echo 'creating function app'
az functionapp create -g $RESOURCE_GROUP -n $FUNCTIONAPP_NAME \
--consumption-plan-location $LOCATION \
--app-insights-key $APPINSIGHTS_INSTRUMENTATIONKEY \
--storage-account $STORAGE_ACCOUNT \
-o json \
1> $PP/logs/090-functionapp.log

echo 'adding app settings for connection strings'

echo ". DatabaseName"
az functionapp config appsettings set --name $FUNCTIONAPP_NAME \
--resource-group $RESOURCE_GROUP \
--settings DatabaseName=$COSMOSDB_DATABASE_NAME \
-o json \
1>> $PP/logs/090-functionapp.log

echo ". RawCollectionName"
az functionapp config appsettings set --name $FUNCTIONAPP_NAME \
--resource-group $RESOURCE_GROUP \
--settings RawCollectionName=$COSMOSDB_COLLECTION_NAME_RAW \
-o json \
1>> $PP/logs/090-functionapp.log

echo ". ViewCollectionName"
az functionapp config appsettings set --name $FUNCTIONAPP_NAME \
--resource-group $RESOURCE_GROUP \
--settings ViewCollectionName=$COSMOSDB_COLLECTION_NAME_MV \
-o json \
1>> $PP/logs/090-functionapp.log

echo ". ConnectionString"
COSMOSDB_CONNECTIONSTRING=`az cosmosdb list-connection-strings -g $RESOURCE_GROUP --name $COSMOSDB_SERVER_NAME --query 'connectionStrings[0].connectionString' -o tsv`
az functionapp config appsettings set --name $FUNCTIONAPP_NAME \
--resource-group $RESOURCE_GROUP \
--settings ConnectionString=$COSMOSDB_CONNECTIONSTRING \
-o json \
1>> $PP/logs/090-functionapp.log

echo 'building function app'
FUNCTION_SRC_PATH=$PP/../materialized-view-processor
CURDIR=$PWD
cd $FUNCTION_SRC_PATH
dotnet publish . --configuration Release 

echo 'creating zip file'
ZIPFOLDER="./bin/Release/netcoreapp2.1/publish/"
rm -f publish.zip
cd $ZIPFOLDER
zip -r $PP/publish.zip . 
cd $CURDIR

echo 'deploying function'
az functionapp deployment source config-zip \
--resource-group $RESOURCE_GROUP \
--name $FUNCTIONAPP_NAME \
--src $PP/publish.zip \
1> $PP/logs/100-functionapp-deploy.log

echo 'removing local zip file'
rm -f $PP/publish.zip

echo 'creating App.Config'

COSMOSDB_URI=`az cosmosdb list -g $RESOURCE_GROUP --query '[0].documentEndpoint' -o tsv`
COSMOSDB_KEY=`az cosmosdb list-keys -g $RESOURCE_GROUP -n $COSMOSDB_SERVER_NAME --query 'primaryMasterKey' -o tsv`
APP=$PP/../sensor-data-producer

sed "s|{URI}|${COSMOSDB_URI}|g" $APP/App.config.template > $APP/App.config

sed -i.bak "s|{KEY}|${COSMOSDB_KEY}|g" $APP/App.config

sed -i.bak "s|{DB}|${COSMOSDB_DATABASE_NAME}|g" $APP/App.config

sed -i.bak "s|{RAW}|${COSMOSDB_COLLECTION_NAME_RAW}|g" $APP/App.config

rm -f $APP/App.config.bak

echo 'deployment done'
