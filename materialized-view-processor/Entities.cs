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

namespace Azure.Samples.Entities
{
    public class Device
    {        
        public string DeviceId;
        public double Value;
        public string TimeStamp;

        public static Device FromDocument(Document document)
        {
            var result = new Device()
            {
                DeviceId = document.GetPropertyValue<string>("deviceId"),
                Value = document.GetPropertyValue<double>("value"),
                TimeStamp = document.GetPropertyValue<DateTime>("timestamp").ToString("yyyy-MM-ddTHH:mm:ssK")
            };

            return result;
        }    
    }

    public class DeviceMaterializedView
    {
        [JsonProperty("id")]
        public string Name;

        [JsonProperty("aggregationSum")]
        public double AggregationSum;

        [JsonProperty("lastValue")]
        public double LastValue;

        [JsonProperty("type")]
        public string Type;

        [JsonProperty("deviceId")]
        public string DeviceId;

        [JsonProperty("lastUpdate")]
        public string TimeStamp;
    }
}
