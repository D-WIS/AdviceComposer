using DWIS.AdviceComposer.SchedulerROPContext.Test;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
