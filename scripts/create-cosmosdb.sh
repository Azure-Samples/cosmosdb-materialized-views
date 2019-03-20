#!/bin/bash
set -euo pipefail

export LOCATION='eastus'
export RESOURCE_GROUP='mvsample'
export COSMOSDB_SERVER_NAME='mvsample'
export COSMOSDB_DATABASE_NAME='mvsample'
export COSMOSDB_COLLECTION_NAME_RAW='raw'
export COSMOSDB_COLLECTION_NAME_MV='view'
export COSMOSDB_RU=10000

echo 'creating resource group'
echo ". name: $RESOURCE_GROUP"
echo ". location: $LOCATION"

az group create -n $RESOURCE_GROUP -l $LOCATION \
1> /dev/null

echo 'creating cosmosdb account'
echo ". name: $COSMOSDB_SERVER_NAME"
SERVER_EXISTS=`az cosmosdb check-name-exists -n $COSMOSDB_SERVER_NAME -o tsv`
if [ $SERVER_EXISTS == "false" ]; then
    az cosmosdb create -g $RESOURCE_GROUP -n $COSMOSDB_SERVER_NAME \
    1> /dev/null
fi

echo 'creating cosmosdb database'
echo ". name: $COSMOSDB_DATABASE_NAME"
DB_EXISTS=`az cosmosdb database exists -g $RESOURCE_GROUP -n $COSMOSDB_SERVER_NAME --db-name $COSMOSDB_DATABASE_NAME -o tsv`
if [ $DB_EXISTS == "false" ]; then
    az cosmosdb database create -g $RESOURCE_GROUP -n $COSMOSDB_SERVER_NAME \
        --db-name $COSMOSDB_DATABASE_NAME \
        1> /dev/null
fi

echo 'creating cosmosdb collection'
echo ". name: $COSMOSDB_COLLECTION_NAME_RAW"
COLLECTION_EXISTS=`az cosmosdb collection exists -g $RESOURCE_GROUP -n $COSMOSDB_SERVER_NAME --db-name $COSMOSDB_DATABASE_NAME --collection-name $COSMOSDB_COLLECTION_NAME_RAW -o tsv`
if [ $COLLECTION_EXISTS == "false" ]; then
    az cosmosdb collection create -g $RESOURCE_GROUP -n $COSMOSDB_SERVER_NAME -d $COSMOSDB_DATABASE_NAME \
    --collection-name $COSMOSDB_COLLECTION_NAME_RAW \
    --partition-key-path "/id" \
    --throughput $COSMOSDB_RU \
    1> /dev/null
fi

echo 'creating cosmosdb collection'
echo ". name: $COSMOSDB_COLLECTION_NAME_MV"
COLLECTION_EXISTS=`az cosmosdb collection exists -g $RESOURCE_GROUP -n $COSMOSDB_SERVER_NAME --db-name $COSMOSDB_DATABASE_NAME --collection-name $COSMOSDB_COLLECTION_NAME_MV -o tsv`
if [ $COLLECTION_EXISTS == "false" ]; then
    az cosmosdb collection create -g $RESOURCE_GROUP -n $COSMOSDB_SERVER_NAME -d $COSMOSDB_DATABASE_NAME \
    --collection-name $COSMOSDB_COLLECTION_NAME_MV \
    --partition-key-path "/id" \
    --throughput $COSMOSDB_RU \
    1> /dev/null
fi

