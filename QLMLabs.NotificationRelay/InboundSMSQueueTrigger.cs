using System;
using System.Text.Json;
using Azure.Storage.Queues.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace QLMLabs.NotificationRelay;

public class InboundSMSQueueTrigger(ILogger<InboundSMSQueueTrigger> logger)
{
    [Function(nameof(InboundSMSQueueTrigger))]
    [CosmosDBOutput("SMS", "inboundSMS", Connection = "COSMOS_DB_CONNECTION", CreateIfNotExists = true)]
    public object? Run([QueueTrigger("inboundsms", Connection = "INBOUND_SMS_QUEUE_CONNECTION_STRING")] string item)
    {
        logger.LogInformation("Queue item received.");

        JsonElement ev;
        try
        {
            ev = JsonSerializer.Deserialize<JsonElement>(item);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse queue JSON.");
            return new
            {
                Error = "Failed to parse queue JSON",
                message = item,
                Exception = ex.Message
            };
        }
        
        ev.TryGetProperty("data", out var dataElement);
        
        var doc = new Dictionary<string, object>
        {
            ["id"] = Guid.NewGuid().ToString(),

            // keep full payload
            ["message"] = item,
            
            ["data"] = dataElement,
            
            // Add timestamp
            ["ingestedUtc"] = DateTime.UtcNow.ToString("o")
        };

        return doc;
    }
}