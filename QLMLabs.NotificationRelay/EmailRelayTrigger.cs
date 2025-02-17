using System.Collections.Generic;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Azure;
using Azure.Communication;
using Azure.Communication.Email;

namespace QLMLabs.NotificationRelay;

public class EmailRelayTrigger(ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<SMSRelayTrigger>();

    [Function("EmailRelayTrigger")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        FunctionContext executionContext)
    {
        _logger.LogInformation("New Email Relay.");
        
        string? connectionString = Environment.GetEnvironmentVariable("COMMUNICATION_SERVICES_CONNECTION_STRING");
        string? fromEmail = Environment.GetEnvironmentVariable("FROM_EMAIL");
        
        if (connectionString == null)
        {
            _logger.LogError("Communication Services Connection String is not set.");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }

        if (fromEmail == null)
        {
            _logger.LogError("From Email is not set.");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }

        
        
        // Parse the request body to get the destination phone number and message
        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        
        var notification = JsonSerializer.Deserialize<EmailRelayMessage>(requestBody);
        
        if (notification == null)
        {
            _logger.LogError("Failed to parse the request body.");
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }
        
        if (notification.To.Count == 0 || notification.Message == null)
        {
            _logger.LogError("Email or Message is not set.");
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }
        
        try
        {
            var emailClient = new EmailClient(connectionString);

            var emailMessage = new EmailMessage(
                senderAddress: fromEmail,
                content: new EmailContent(notification.Subject ?? "No Subject")
                {
                    PlainText = notification.Message,
                    
                },
                recipients: new EmailRecipients(
                    notification.To.Select(to => new EmailAddress(to)),
                    notification.CC?.Select(cc => new EmailAddress(cc)),
                    notification.Bcc?.Select(bcc => new EmailAddress(bcc))
                ));
            
            EmailSendOperation emailSendOperation = await emailClient.SendAsync(
                WaitUntil.Completed,
                emailMessage);
            
            if (!emailSendOperation.HasValue || emailSendOperation.Value.Status == EmailSendStatus.Failed)
            {
                _logger.LogError($"Failed to send email to {notification.To}");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }
            
            
            _logger.LogInformation($"Sent Email to {notification.To} with message: {notification.Message}");
            return req.CreateResponse(HttpStatusCode.OK);
            
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to send email.");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }
    }
    
    public class EmailRelayMessage
    {
        public List<string> To { get; set; } = [];
        public List<string>? CC { get; set; }
        public List<string>? Bcc { get; set; }
        public string? Subject { get; set; }
        public string? Message { get; set; }
        
        // Depreciated: Backward compatibility with old API will be removed in future versions
        public string? Email
        {
            set
            {
                if (value != null) To.Add(value);
            }
        }
    }
}