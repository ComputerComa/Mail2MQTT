using Microsoft.Extensions.Options;
using SmtpMqttGateway.Configuration;
using SmtpMqttGateway.Mqtt;
using SmtpMqttGateway.Services;
using SmtpMqttGateway.Smtp;
using SmtpServer.Storage;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSystemd();

builder.Services
    .AddOptions<SmtpOptions>()
    .Bind(builder.Configuration.GetSection(SmtpOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<SmtpOptions>, SmtpOptionsValidator>();

builder.Services
    .AddOptions<MqttOptions>()
    .Bind(builder.Configuration.GetSection(MqttOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<MqttOptions>, MqttOptionsValidator>();

builder.Services.AddSingleton<IAlertEventFactory, AlertEventFactory>();
builder.Services.AddSingleton<IMessageStore, GatewayMessageStore>();

// Registered as a singleton so IAlertPublisher and the hosted lifecycle share
// the same MQTT connection. Registration order also controls shutdown order:
// the generic host stops hosted services in reverse-registration order, so
// registering the MQTT publisher before the SMTP listener means SMTP stops
// accepting connections first, and MQTT (with its "offline" status) stops last.
builder.Services.AddSingleton<MqttAlertPublisher>();
builder.Services.AddSingleton<IAlertPublisher>(sp => sp.GetRequiredService<MqttAlertPublisher>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MqttAlertPublisher>());

builder.Services.AddHostedService<SmtpHostedService>();

var host = builder.Build();

// ValidateOnStart() above makes host.Run() throw OptionsValidationException
// immediately if Smtp/Mqtt configuration is invalid, before any socket opens.
host.Run();
