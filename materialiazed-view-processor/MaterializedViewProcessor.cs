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

namespace Azure.Samples
{
    public class AllDevicesMaterializedView
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("aggregationSum")]
        public double AggregationSum { get; set; }

        [JsonProperty("lastValue")]
        public double LastValue { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("deviceId")]
        public string DeviceId { get; set; }

        [JsonProperty("lastUpdate")]
        public string TimeStamp { get; set; }        
    }

    public class DeviceMaterializedView
    {
        [JsonProperty("id")]
        public string Name { get; set; }

        [JsonProperty("aggregationSum")]
        public double AggregationSum { get; set; }

        [JsonProperty("lastValue")]
        public double LastValue { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("deviceId")]
        public string DeviceId { get; set; }

        [JsonProperty("lastUpdate")]
        public string TimeStamp { get; set; }        
    }

    public static class MaterializedViewProcessor
    {

        [FunctionName("MaterializedViewProcessor")]
        public static async Task Run(
            [CosmosDBTrigger(
                databaseName: "%DatabaseName%", 
                collectionName: "%RawCollectionName%", 
                ConnectionStringSetting = "ConnectionString", 
                LeaseCollectionName = "leases", 
                CreateLeaseCollectionIfNotExists = true
            )]IReadOnlyList<Document> input,
            [CosmosDB(
                databaseName: "%DatabaseName%",
                collectionName: "%ViewCollectionName%", // TODO: does this do anything? We have 3 different views collections now and all are getting written to
                ConnectionStringSetting = "ConnectionString"
            )]DocumentClient client,
            ILogger log
        )        
        {
            var databaseName = Environment.GetEnvironmentVariable("DatabaseName");
            var collectionName = Environment.GetEnvironmentVariable("ViewCollectionName");
            var pricingCollection = Environment.GetEnvironmentVariable("PricingCollectionName");

            var collectionUri = UriFactory.CreateDocumentCollectionUri(databaseName, collectionName);

            if (input != null && input.Count > 0)
            {
                foreach(var d in input)
                {                    
                    var ts = d.GetPropertyValue<DateTime>("timestamp").ToString("yyyy-MM-ddTHH:mm:ssK");                    
                    var deviceId = d.GetPropertyValue<string>("deviceId");
                    var value = d.GetPropertyValue<double>("value");
                    
                    var optionsSingle = new RequestOptions() { PartitionKey = new PartitionKey(deviceId) };
                    
                    DeviceMaterializedView viewSingle = null;
                    
                    try {
                        var uriSingle = UriFactory.CreateDocumentUri(databaseName, collectionName, deviceId);
                        
                        log.LogInformation($"Materialized view: {uriSingle.ToString()}");

                        viewSingle = await client.ReadDocumentAsync<DeviceMaterializedView>(uriSingle, optionsSingle);                        
                    } 
                    catch (DocumentClientException ex)
                    {
                        if (ex.StatusCode != HttpStatusCode.NotFound) 
                            throw ex;         
                    }

                    //log.LogInformation("Document: " + viewSingle.ToString());

                    if (viewSingle == null)
                    {
                        log.LogInformation("Creating new materialized view");    
                        viewSingle = new DeviceMaterializedView()
                        {
                            Name = deviceId,
                            Type = "device",
                            DeviceId = deviceId,
                            AggregationSum = value,
                            LastValue = value,
                            TimeStamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssK")
                        };
                    } else {
                        log.LogInformation("Updating materialized view");
                        viewSingle.AggregationSum += value;
                        viewSingle.LastValue = value;
                        viewSingle.TimeStamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssK");                        
                    }                    
                    
                    var resultSingle = await client.UpsertDocumentAsync(collectionUri, viewSingle, optionsSingle);

                    log.LogInformation(resultSingle.StatusCode.ToString());                    


                    log.LogInformation("Updating ALL materialized view");

                    JObject viewAll = null;
                    var optionsAll = new RequestOptions() { PartitionKey = new PartitionKey("000") };

                    try {
                        var uriAll = UriFactory.CreateDocumentUri(databaseName, collectionName, "000");
                        
                        log.LogInformation($"Materialized view: {uriAll.ToString()}");

                        viewAll = await client.ReadDocumentAsync<JObject>(uriAll, optionsAll);                        
                    } 
                    catch (DocumentClientException ex)
                    {
                        if (ex.StatusCode != HttpStatusCode.NotFound) 
                            throw ex;         
                    }

                    if (viewAll == null)
                    {
                        viewAll = new JObject();
                        viewAll["id"] = "000";
                        viewAll["deviceId"] = "000";
                        viewAll["deviceSummary"] = new JObject();
                    }

                    viewAll["deviceSummary"][deviceId] = value;

                    var resultAll = await client.UpsertDocumentAsync(collectionUri, viewAll, optionsAll);
                    
                    log.LogInformation(resultAll.StatusCode.ToString());                    
                }    
            }
        }
    }
}
