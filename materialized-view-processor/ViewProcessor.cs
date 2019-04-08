using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Azure.Documents;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Documents.Client;
using System.Net;
using Azure.Samples.Entities;

namespace Azure.Samples.Processor
{   
    public class ViewProcessor
    {
        private DocumentClient _client;
        private Uri _collectionUri;
        private ILogger _log;

        private string _databaseName = Environment.GetEnvironmentVariable("DatabaseName");
        private string _collectionName = Environment.GetEnvironmentVariable("ViewCollectionName");


        public ViewProcessor(DocumentClient client, ILogger log)
        {
            _log = log;
            _client = client;
            _collectionUri = UriFactory.CreateDocumentCollectionUri(_databaseName, _collectionName);
        }

        public async Task UpdateGlobalMaterializedView(Device device)
        {
            _log.LogInformation("Updating global materialized view");

            Document viewAll = null;
            var optionsAll = new RequestOptions() { PartitionKey = new PartitionKey("global") };

            int attempts = 0;

            while (attempts < 10)
            {
                try
                {
                    var uriAll = UriFactory.CreateDocumentUri(_databaseName, _collectionName, "global");

                    _log.LogInformation($"Materialized view: {uriAll.ToString()}");

                    viewAll = await _client.ReadDocumentAsync(uriAll, optionsAll);                
                }
                catch (DocumentClientException ex)
                {
                    if (ex.StatusCode != HttpStatusCode.NotFound)
                        throw ex;
                }

                if (viewAll == null)
                {
                    viewAll = new Document();
                    viewAll.SetPropertyValue("id", "global");
                    viewAll.SetPropertyValue("deviceId", "global");
                    viewAll.SetPropertyValue("type", "global");
                    viewAll.SetPropertyValue("id", "global");
                    viewAll.SetPropertyValue("deviceSummary", new JObject());
                }

                var ds = viewAll.GetPropertyValue<JObject>("deviceSummary");
                ds[device.DeviceId] = device.Value;
                viewAll.SetPropertyValue("deviceSummary", ds);            
                viewAll.SetPropertyValue("deviceLastUpdate", device.TimeStamp);
                viewAll.SetPropertyValue("lastUpdate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssK"));

                AccessCondition acAll = new AccessCondition() {
                    Type = AccessConditionType.IfMatch,
                    Condition = viewAll.ETag                
                };
                optionsAll.AccessCondition = acAll;
                
                try 
                {
                    await UpsertDocument(viewAll, optionsAll);
                    return;
                }
                catch (DocumentClientException de) 
                {
                    if (de.StatusCode == HttpStatusCode.PreconditionFailed)
                    {
                        attempts += 1;
                        _log.LogWarning($"Optimistic concurrency pre-condition check failed. Trying again ({attempts}/10)");                        
                    }
                    else
                    {
                        throw;
                    }
                }              
            }

            throw new ApplicationException("Could not insert document after retring 10 times, due to concurrency violations");
        }

        public async Task UpdateDeviceMaterializedView(Device device)
        {
            var optionsSingle = new RequestOptions() { PartitionKey = new PartitionKey(device.DeviceId) };

            DeviceMaterializedView viewSingle = null;

            try
            {
                var uriSingle = UriFactory.CreateDocumentUri(_databaseName, _collectionName, device.DeviceId);

                _log.LogInformation($"Materialized view: {uriSingle.ToString()}");

                viewSingle = await _client.ReadDocumentAsync<DeviceMaterializedView>(uriSingle, optionsSingle);
            }
            catch (DocumentClientException ex)
            {
                if (ex.StatusCode != HttpStatusCode.NotFound)
                    throw ex;
            }

            //log.LogInformation("Document: " + viewSingle.ToString());

            if (viewSingle == null)
            {
                _log.LogInformation("Creating new materialized view");
                viewSingle = new DeviceMaterializedView()
                {
                    Name = device.DeviceId,
                    Type = "device",
                    DeviceId = device.DeviceId,
                    AggregationSum = device.Value,
                    LastValue = device.Value,
                    TimeStamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssK")
                };
            }
            else
            {
                _log.LogInformation("Updating materialized view");
                viewSingle.AggregationSum += device.Value;
                viewSingle.LastValue = device.Value;
                viewSingle.TimeStamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssK");
            }
            
            await UpsertDocument(viewSingle, optionsSingle);
        }

        private async Task<ResourceResponse<Document>> UpsertDocument(object document, RequestOptions options)
        {
            int attempts = 0;

            while (attempts < 3)
            {
                try
                {
                    var result = await _client.UpsertDocumentAsync(_collectionUri, document, options);                      
                    _log.LogInformation($"{options.PartitionKey} RU Used: {result.RequestCharge:0.0}");
                    return result;                                  
                }
                catch (DocumentClientException de)
                {
                    if (de.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        _log.LogWarning($"Waiting for {de.RetryAfter} msec...");
                        await Task.Delay(de.RetryAfter);
                        attempts += 1;
                    }
                    else
                    {
                        throw;
                    }
                }
            }            

            throw new ApplicationException("Could not insert document after being throttled 3 times");
        }
    }
}
