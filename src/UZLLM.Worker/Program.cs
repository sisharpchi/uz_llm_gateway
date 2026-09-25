using UZLLM.Observability;
using UZLLM.Persistence;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Infrastructure;
using UZLLM.Modules.Usage.Infrastructure;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Payments.Infrastructure;
using UZLLM.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddUzllmPersistence(builder.Configuration);
builder.Services.AddUzllmRedis(builder.Configuration);
builder.Services.AddUzllmUsage();
builder.Services.AddUzllmBilling();
builder.Services.AddUzllmOrganizations();
builder.Services.AddUzllmPayments(builder.Configuration);
builder.Services.AddScoped<ILeasedJobHandler, PaymentReconciliationJobHandler>();
builder.Services.AddScoped<IOutboxHandler>(services => new PaymentEventLogHandler(
    services.GetRequiredService<ILogger<PaymentEventLogHandler>>(),
    services.GetRequiredService<ITransactionCoordinator>(),
    services.GetRequiredService<IConsumerInboxStore>(), "payment.intent.paid"));
builder.Services.AddScoped<IOutboxHandler>(services => new PaymentEventLogHandler(
    services.GetRequiredService<ILogger<PaymentEventLogHandler>>(),
    services.GetRequiredService<ITransactionCoordinator>(),
    services.GetRequiredService<IConsumerInboxStore>(), "payment.intent.canceled"));
builder.Services.AddScoped<ILeasedJobHandler>(services => new BillingReconciliationJobHandler(
    services.GetRequiredService<IFinancialService>(), "billing.reconcile"));
builder.Services.AddScoped<ILeasedJobHandler>(services => new BillingReconciliationJobHandler(
    services.GetRequiredService<IFinancialService>(), "billing.reconcile.final"));
builder.Services.AddScoped<IOutboxHandler>(services => new BillingUsageEvidenceHandler(
    services.GetRequiredService<IFinancialService>(), "usage.evidence.verified"));
builder.Services.AddScoped<IOutboxHandler>(services => new BillingUsageEvidenceHandler(
    services.GetRequiredService<IFinancialService>(), "usage.evidence.unknown"));
builder.Services.AddScoped<IOutboxHandler>(services => new BillingFinancialAlertHandler(
    services.GetRequiredService<IOperationalAlertPublisher>(),
    services.GetRequiredService<ITransactionCoordinator>(),
    services.GetRequiredService<IConsumerInboxStore>(), "billing.reservation.finalized"));
builder.Services.AddScoped<IOutboxHandler>(services => new BillingFinancialAlertHandler(
    services.GetRequiredService<IOperationalAlertPublisher>(),
    services.GetRequiredService<ITransactionCoordinator>(),
    services.GetRequiredService<IConsumerInboxStore>(), "billing.reversal.applied"));
builder.Services.AddScoped<IOutboxHandler>(services => new BillingFinancialAlertHandler(
    services.GetRequiredService<IOperationalAlertPublisher>(),
    services.GetRequiredService<ITransactionCoordinator>(),
    services.GetRequiredService<IConsumerInboxStore>(), "billing.late_exposure.recorded"));
builder.Services.AddUzllmObservability(builder.Configuration, "UZLLM.Worker");
builder.Services.AddScoped<OutboxDispatchCycle>();
builder.Services.AddScoped<LeasedJobDispatchCycle>();
builder.Services.AddHostedService<OutboxDispatchWorker>();
builder.Services.AddHostedService<LeasedJobDispatchWorker>();
var host = builder.Build();
await host.RunAsync();
