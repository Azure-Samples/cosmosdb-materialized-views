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
using Azure.Samples.Processor;

namespace Azure.Samples
{
    public static class Function
    {
        [FunctionName("MaterializedViewProcessor")]
        public static async Task Run(
            [CosmosDBTrigger(
                databaseName: "%DatabaseName%", 
                collectionName: "%RawCollectionName%", 
                ConnectionStringSetting = "ConnectionString", 
                LeaseCollectionName = "leases", 
                FeedPollDelay=1000
            )]IReadOnlyList<Document> input,
            [CosmosDB(
                databaseName: "%DatabaseName%",
                collectionName: "%ViewCollectionName%",
                ConnectionStringSetting = "ConnectionString"
            )]DocumentClient client,
            ILogger log
        )        
        {
            if (input != null && input.Count > 0)
            {
                var p = new ViewProcessor(client, log);
                
                log.LogInformation($"Processing {input.Count} events");
                
                foreach(var d in input)
                {
                    var device = Device.FromDocument(d);

                    var tasks = new List<Task>();

                    tasks.Add(p.UpdateDeviceMaterializedView(device));
                    tasks.Add(p.UpdateGlobalMaterializedView(device));

                    await Task.WhenAll(tasks);
                }    
            }
        }
    }
}
