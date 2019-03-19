Generates a data point every second and send it to Cosmos DB Database, in the `rawdata` collection.

Accepts "Sensor Id" as parameter. Sensor Id can be set also via SENSOR_ID environment variable.

It will generate data like the following:

```
{
    "deviceId": "<sensor id>",
    "v": 127.984604316328,
    "ts2": "2017-10-10T00:07:00Z",
 }
 ```

 TO DO:
- Run the app in a container (Azure Container Instances) to generate data for "n" sensors. Make sure you change the Sensor Id value for every running instance of the app.



