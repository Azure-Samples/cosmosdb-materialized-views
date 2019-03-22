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

            JObject viewAll = null;
            var optionsAll = new RequestOptions() { PartitionKey = new PartitionKey("global") };

            try
            {
                var uriAll = UriFactory.CreateDocumentUri(_databaseName, _collectionName, "global");

                _log.LogInformation($"Materialized view: {uriAll.ToString()}");

                viewAll = await _client.ReadDocumentAsync<JObject>(uriAll, optionsAll);
            }
            catch (DocumentClientException ex)
            {
                if (ex.StatusCode != HttpStatusCode.NotFound)
                    throw ex;
            }

            if (viewAll == null)
            {
                viewAll = new JObject();
                viewAll["id"] = "global";
                viewAll["deviceId"] = "global";
                viewAll["type"] = "global";
                viewAll["deviceSummary"] = new JObject();
            }

            viewAll["deviceSummary"][device.DeviceId] = device.Value;

            await UpsertDocument(viewAll, optionsAll);
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
                    _log.LogInformation($"RU Used: {result.RequestCharge:0.0}");
                    return result;                                  
                }
                catch (DocumentClientException de)
                {
                    if (de.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        _log.LogInformation($"Waiting for {de.RetryAfter} msec...");
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
