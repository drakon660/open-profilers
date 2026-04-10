using Carter;
using Mongo.Profiler.AspNet;
using Mongo.Profiler.Client.AspNet;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration["ConnectionString"] ?? "mongodb://localhost:27017";
var databaseName = builder.Configuration["DatabaseName"] ?? "profiler_samples";
var collectionName = builder.Configuration["CollectionName"] ?? "orders";
var grpcPort = builder.Configuration.GetValue("GrpcPort", 5179);
var serverSelectionTimeoutMs = builder.Configuration.GetValue("MongoServerSelectionTimeoutMs", 1500);
var connectTimeoutMs = builder.Configuration.GetValue("MongoConnectTimeoutMs", 1500);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCarter();
builder.AddMongoProfiler(options =>
{
    options.Port = grpcPort;
    options.ListenOnAnyIp = false;
});
builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
{
    var settings = MongoClientSettings.FromConnectionString(connectionString);
    settings.ServerSelectionTimeout = TimeSpan.FromMilliseconds(Math.Clamp(serverSelectionTimeoutMs, 250, 60_000));
    settings.ConnectTimeout = TimeSpan.FromMilliseconds(Math.Clamp(connectTimeoutMs, 250, 60_000));
    settings = settings.UseMongoProfiler(serviceProvider);
    return new MongoClient(settings);
});
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider
        .GetRequiredService<IMongoClient>()
        .GetDatabase(databaseName)
        .GetCollection<Order>(collectionName));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => "Mongo.Profiler.AspNet is running.");
app.MapCarter();
app.MapMongoProfiler();

app.Logger.LogInformation("Mongo profiler gRPC endpoint is listening on localhost:{GrpcPort}.", grpcPort);

app.Run();
