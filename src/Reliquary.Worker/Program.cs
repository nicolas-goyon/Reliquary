using Reliquary.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddReliquaryCore(builder.Configuration);
builder.Services.AddReliquaryWorker(builder.Configuration);

await builder.Build().RunAsync();
