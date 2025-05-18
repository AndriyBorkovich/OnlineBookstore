var builder = DistributedApplication.CreateBuilder(args);

var postgresql = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithDataVolume("postgres-data")
    .WithLifetime(ContainerLifetime.Persistent);

var catalogDb = postgresql.AddDatabase("catalogdb");
var orderDb = postgresql.AddDatabase("orderdb");

var mongodb = builder.AddMongoDB("mongodb")
    .WithDataVolume("mongo-data")
    .WithLifetime(ContainerLifetime.Persistent);

var reviewDb = mongodb.AddDatabase("reviewdb");

var redis = builder.AddRedis("redis")
    .WithDataVolume("redis-data")
    .WithLifetime(ContainerLifetime.Persistent);

var elasticsearch = builder.AddElasticsearch("elasticsearch")
    .WithDataVolume("elastic-data")
    .WithLifetime(ContainerLifetime.Persistent);

// Microservices
var catalogService = builder.AddProject<Projects.OnlineBookstore_CatalogService>("catalogservice")
    .WithReference(catalogDb)
    .WithReference(redis)
    .WithReference(elasticsearch)
    .WithExternalHttpEndpoints();

var orderService = builder.AddProject<Projects.OnlineBookStore_OrderService>("orderservice")
    .WithReference(orderDb)
    .WithExternalHttpEndpoints();

var reviewService = builder.AddProject<Projects.OnlineBookstore_ReviewService>("reviewservice")
    .WithReference(mongodb)
    .WithReference(redis)
    .WithExternalHttpEndpoints();

// Frontend
builder.AddProject<Projects.OnlineBookstore_Web>("webfrontend")
    .WithReference(catalogService)
    .WithReference(orderService)
    .WithReference(reviewService)
    .WithExternalHttpEndpoints();

builder.Build().Run();
