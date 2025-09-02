using System.Collections.Generic;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Azure;
using Azure.Communication;
using Azure.Communication.Sms;

namespace QLMLabs.NotificationRelay;

public class SMSRelayTrigger
{
    private readonly ILogger _logger;

    public SMSRelayTrigger(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<SMSRelayTrigger>();
    }

    [Function("SMSRelayTrigger")]
    public async Task<SMSRelayOutput> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        FunctionContext executionContext)
    {
        _logger.LogInformation("New SMS Relay.");

        string? connectionString = Environment.GetEnvironmentVariable("COMMUNICATION_SERVICES_CONNECTION_STRING");
        string? fromPhoneNumber = Environment.GetEnvironmentVariable("FROM_PHONE_NUMBER");

        if (connectionString == null)
        {
            _logger.LogError("Communication Services Connection String is not set.");
            return new SMSRelayOutput
            {
                response = req.CreateResponse(HttpStatusCode.InternalServerError)
            };
        }

        if (fromPhoneNumber == null)
        {
            _logger.LogError("From Phone Number is not set.");
            return new SMSRelayOutput
            {
                response = req.CreateResponse(HttpStatusCode.InternalServerError)
            };
        }

        SmsClient smsClient = new SmsClient(connectionString);

        // Parse the request body to get the destination phone number and message
        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

        var notification = JsonSerializer.Deserialize<SMSRelayMessage>(requestBody);

        if (notification == null)
        {
            _logger.LogError("Failed to parse the request body.");
            return new SMSRelayOutput
            {
                response = req.CreateResponse(HttpStatusCode.BadRequest)
            };
        }

        if (notification.PhoneNumber == null || notification.Message == null)
        {
            _logger.LogError("PhoneNumber or Message is not set.");
            return new SMSRelayOutput
            {
                response = req.CreateResponse(HttpStatusCode.BadRequest)
            };
        }

        try
        {
            var sendSmsResponse =
                await smsClient.SendAsync(fromPhoneNumber, [notification.PhoneNumber], notification.Message);
            _logger.LogInformation($"Sent SMS to {notification.PhoneNumber} with message: {notification.Message}");
            return new SMSRelayOutput
            {
                response = req.CreateResponse(HttpStatusCode.OK),
                cosmos = new
                {
                    id = Guid.NewGuid().ToString(),
                    message = notification.Message,
                    sentUtc = DateTime.UtcNow.ToString("o"),
                    result = sendSmsResponse.Value
                }
            };
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to send SMS.");
            return new SMSRelayOutput
            {
                response = req.CreateResponse(HttpStatusCode.InternalServerError)
            };
        }
    }

    public class SMSRelayMessage
    {
        public string? PhoneNumber { get; set; }
        public string? Message { get; set; }
    }

    public class SMSRelayOutput
    {
        [HttpResult] public HttpResponseData response { get; set; }

        [CosmosDBOutput("SMS", "outboundSMS", Connection = "COSMOS_DB_CONNECTION", CreateIfNotExists = true)]
        public object? cosmos { get; set; }
    }
}