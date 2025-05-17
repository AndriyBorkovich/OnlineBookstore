var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure resources
var postgresql = builder.AddPostgres("postgres")
    .WithDataVolumeMount("postgres-data");

var catalogDb = postgresql.AddDatabase("catalogdb");
var orderDb = postgresql.AddDatabase("orderdb");

var mongodb = builder.AddMongoDB("mongodb")
    .WithDataVolumeMount("mongo-data");

var redis = builder.AddRedis("redis")
    .WithDataVolumeMount("redis-data");

var elasticsearch = builder.AddElasticsearch("elasticsearch")
    .WithDataVolumeMount("elastic-data");

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
