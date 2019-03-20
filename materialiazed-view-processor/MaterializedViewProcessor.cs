using System;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Azure.Documents;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Documents.Client;

namespace Azure.Samples
{
    public static class MaterializedViewProcessor
    {
        [FunctionName("MaterializedViewProcessor")]
        public static void Run(
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
            if (input != null && input.Count > 0)
            {
                log.LogInformation("Documents modified " + input.Count);
                log.LogInformation("First document Id " + input[0].Id);
            }
        }
    }
}
