using Microsoft.Extensions.Configuration;
using UZLLM.Modules.Notifications.Infrastructure;

namespace UZLLM.Worker.Tests;

public sealed class IdentityEmailConfigurationTests
{
    [Fact]
    public void Smtp_options_require_host_sender_and_paired_credentials()
    {
        Assert.Throws<InvalidOperationException>(() => IdentitySmtpOptions.FromConfiguration(Config()));
        Assert.Throws<InvalidOperationException>(() => IdentitySmtpOptions.FromConfiguration(Config(
            ("Email:SmtpHost", "smtp.example.uz"), ("Email:FromAddress", "noreply@example.uz"),
            ("Email:Username", "mailer"))));
        Assert.Throws<InvalidOperationException>(() => IdentitySmtpOptions.FromConfiguration(Config(
            ("Email:SmtpHost", "smtp.example.uz"), ("Email:FromAddress", "noreply@example.uz"),
            ("Email:SmtpPort", "0"))));

        var valid = IdentitySmtpOptions.FromConfiguration(Config(
            ("Email:SmtpHost", "smtp.example.uz"), ("Email:FromAddress", "noreply@example.uz"),
            ("Email:Username", "mailer"), ("Email:Password", "secret")));
        Assert.Equal(587, valid.Port);
        Assert.Equal("smtp.example.uz", valid.Host);
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(
            value => value.Key, value => (string?)value.Value)).Build();
}
