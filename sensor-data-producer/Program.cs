using System;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Newtonsoft.Json;
using System.Threading;
using System.IO;
using System.Collections.Generic;

namespace SensorDataProducer
{
    public class SensorData
    {
        [JsonProperty("deviceId")]
        public string Id { get; set; }

        [JsonProperty("value")]
        public double Value { get; set; }

        [JsonProperty("timestamp")]
        public string TimeStamp { get; set; }

        public override string ToString()
        {
            return string.Format($"{Id}: {TimeStamp} - {Value}");
        }
    }

    public class CosmosDBInfo {
        public string EndpointUri;
        public string Key;
        public string Database;
        public string Collection;
    }

    class Program
    {
        private const string ENDPOINT_URI = "https://totalcosmosdb.documents.azure.com:443/";
        private const string PRIMARY_KEY = "SZE3CwFg975P9NVrluMqyYOeuYfMG0LMhuhuNoRiaEUl5VkjcgIHeg5VQMY8BwozfFu1je6GUYKhErc1ss91gQ==";
        private const string DATABASE = "poc";
        private const string COLLECTION = "rawdata";

        static async Task Main(string[] args)
        {
            string sensorId = string.Empty;

            if (args.Count() == 1)
            {
                sensorId = args[0];
            }

            if (string.IsNullOrEmpty(sensorId))
            {
                Console.WriteLine("Please specify SensorId range. Eg: sensor-data-producer 1-10");
                return;
            }

            int s = 0;
            int e = 0;

            if (sensorId.Contains("-"))
            {
                var split = sensorId.Split('-');
                if (split.Count() != 2) {
                    Console.WriteLine("Range must be in the form N-M, where N and M are positive integers. Eg; 1-10");
                    return;
                }
                
                Int32.TryParse(split[0], out s);
                Int32.TryParse(split[0], out e);
            } else 
            {
                s = 1;
                Int32.TryParse(sensorId, out e);                
            }

            if (s == 0 || e == 0)
            {
                Console.WriteLine("Provided SensorId must be an integer number or a range of positive integers in the form N-M. Eg: 1-10");
                return;
            }

            var tasks = new List<Task>();
            var cts = new CancellationTokenSource();

            var cosmosDBInfo = new CosmosDBInfo()
            {
                EndpointUri = ENDPOINT_URI,
                Key = PRIMARY_KEY,
                Database = DATABASE,
                Collection = COLLECTION
            };

            var simulator = new Simulator(cosmosDBInfo, cts.Token);

            foreach (int i in Enumerable.Range(s, e))
            {
                tasks.Add(new Task(async () => await simulator.Run(i), TaskCreationOptions.LongRunning));
            }

            tasks.ForEach(t => t.Start());

            Console.WriteLine("Press any key to terminate simulator");
            Console.ReadKey(true);

            cts.Cancel();
            Console.WriteLine("Cancel requested...");

            await Task.WhenAll(tasks.ToArray());

            Console.WriteLine("Done.");
        }
    }

    class Simulator {

        private CancellationToken _token;
        private CosmosDBInfo _cosmosDB;

        public Simulator(CosmosDBInfo cosmosDB, CancellationToken token)
        {
            _token = token;
            _cosmosDB = cosmosDB;
        }

        public async Task Run(int sensorId)
        {
            DocumentClient client;
            Random rnd = new Random();

            client = new DocumentClient(
                new Uri(_cosmosDB.EndpointUri), 
                _cosmosDB.Key, 
                new ConnectionPolicy { ConnectionMode = ConnectionMode.Direct, ConnectionProtocol = Protocol.Tcp } 
                );

            var database = await client.CreateDatabaseIfNotExistsAsync(new Database { Id = _cosmosDB.Database });

            var collection = await client.CreateDocumentCollectionIfNotExistsAsync(
                UriFactory.CreateDatabaseUri(_cosmosDB.Database), 
                new DocumentCollection { Id = _cosmosDB.Collection }
                );

            Random random = new Random();

            while (!_token.IsCancellationRequested)
            {
                var sensorData = new SensorData()
                {
                    Id = sensorId.ToString().PadLeft(3, '0'),
                    Value = 100 + random.NextDouble() * 100,
                    TimeStamp = DateTime.UtcNow.ToString("o")
                };

                Console.WriteLine(sensorData + "[{0}]", DateTime.Now.ToString("o"));

                try
                {
                    await client.CreateDocumentAsync(
                        UriFactory.CreateDocumentCollectionUri(_cosmosDB.Database, _cosmosDB.Collection), 
                        sensorData);
                }
                catch (DocumentClientException de)
                {
                    if (de.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        Console.WriteLine($"{sensorData.Id}: Waiting for ${de.RetryAfter} msec...");
                        await Task.Delay(de.RetryAfter);
                    }
                    else
                    {
                        throw;
                    }
                }

                await Task.Delay(rnd.Next(500) + 750);
            }            
        }
    }
}
