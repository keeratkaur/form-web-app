using Microsoft.Extensions.Options;
using MailKit.Security;
using MimeKit;
using Microsoft.Identity.Client;
using Forms_app.Data;

public class EmailHelper
{
    private readonly SmtpSettings _smtpSettings;

    public EmailHelper(IOptions<SmtpSettings> smtpSettings)
    {
        _smtpSettings = smtpSettings.Value;
    }

    // Method to acquire an access token using MSAL
    private async Task<string> GetAccessTokenAsync()
    {
        // Build the confidential client application using the Azure AD credentials.
        var app = ConfidentialClientApplicationBuilder.Create(_smtpSettings.appConfiguration.ClientId)
            .WithClientSecret(_smtpSettings.appConfiguration.ClientSecret)
            .WithAuthority($"https://login.microsoftonline.com/{_smtpSettings.appConfiguration.TenantId}")
            .Build();

        // Define the required scope for SMTP sending.
        // The token request uses the .default scope to request all permissions consented to for the app.
        string[] scopes = new string[] { "https://outlook.office365.com/.default" };

        // Acquire an access token using the client credentials flow.
        var result = await app.AcquireTokenForClient(scopes).ExecuteAsync();
        return result.AccessToken;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string body)
    {
        // Create the email message using MimeKit
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_smtpSettings.FromName, _smtpSettings.FromEmail));
        message.To.Add(new MailboxAddress(string.Empty, toEmail));
        message.Subject = subject;

        var builder = new BodyBuilder
        {
            HtmlBody = body
        };
        message.Body = builder.ToMessageBody();

        // Use MailKit's SmtpClient which supports OAuth2
        using (var client = new MailKit.Net.Smtp.SmtpClient())
        {
            // For development/testing only:
            // Bypass certificate validation. For production, handle certificate validation properly.
            client.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => true;

            // Connect to the SMTP server (e.g., Office 365: smtp.office365.com, Port: 587)
            await client.ConnectAsync(_smtpSettings.Host, _smtpSettings.Port, SecureSocketOptions.StartTls);

            // Retrieve the OAuth2 access token
            var accessToken = await GetAccessTokenAsync();

            // Authenticate using OAuth2
            var oauth2 = new SaslMechanismOAuth2(_smtpSettings.Username, accessToken);
            await client.AuthenticateAsync(oauth2);

            // Send the email message
            await client.SendAsync(message);

            // Disconnect cleanly
            await client.DisconnectAsync(true);
        }
    }
}
