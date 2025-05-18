var builder = DistributedApplication.CreateBuilder(args);

var postgresql = builder.AddPostgres("postgres")
    .WithPgAdmin(c => c.WithLifetime(ContainerLifetime.Persistent))
    .WithDataVolume("postgres-data")
    .WithLifetime(ContainerLifetime.Persistent);

var catalogDb = postgresql.AddDatabase("catalogdb");
var orderDb = postgresql.AddDatabase("orderdb");

var mongodb = builder.AddMongoDB("mongodb")
    .WithDataVolume("mongo-data")
    .WithMongoExpress(c => c.WithLifetime(ContainerLifetime.Persistent))
    .WithLifetime(ContainerLifetime.Persistent);

var reviewDb = mongodb.AddDatabase("reviewdb");

var redis = builder.AddRedis("redis")
    .WithDataVolume("redis-data")
    .WithRedisInsight(c => c.WithLifetime(ContainerLifetime.Persistent))
    .WithLifetime(ContainerLifetime.Persistent);

var elasticsearch = builder.AddElasticsearch("elasticsearch")
    .WithDataVolume("elastic-data")
    .WithLifetime(ContainerLifetime.Persistent);

// Microservices
var catalogService = builder.AddProject<Projects.OnlineBookstore_CatalogService>("catalog-service")
    .WithReference(catalogDb)
    .WaitFor(catalogDb)
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(elasticsearch)
    .WaitFor(elasticsearch)
    .WithExternalHttpEndpoints();

var orderService = builder.AddProject<Projects.OnlineBookStore_OrderService>("order-service")
    .WithReference(orderDb)
    .WaitFor(orderDb)
    .WithExternalHttpEndpoints();

var reviewService = builder.AddProject<Projects.OnlineBookstore_ReviewService>("review-service")
    .WithReference(reviewDb)
    .WaitFor(reviewDb)
    .WithReference(redis)
    .WaitFor(redis)
    .WithExternalHttpEndpoints();

// Frontend
builder.AddProject<Projects.OnlineBookstore_Web>("webfrontend")
    .WithReference(catalogService)
    .WithReference(orderService)
    .WithReference(reviewService)
    .WithExternalHttpEndpoints();

builder.Build().Run();
