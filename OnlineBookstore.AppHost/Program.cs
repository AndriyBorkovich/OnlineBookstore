var builder = DistributedApplication.CreateBuilder(args);

// Databases
var postgresql = builder.AddPostgres("postgres")
    .WithPgAdmin(c => c.WithImageTag("latest").WithLifetime(ContainerLifetime.Persistent))
    .WithDataVolume("bookstore-pg-data")
    .WithLifetime(ContainerLifetime.Persistent);

var catalogDb = postgresql.AddDatabase("catalogdb");
var orderDb = postgresql.AddDatabase("orderdb");

var mongodb = builder.AddMongoDB("mongodb")
    .WithDataVolume("bookstore-mongo-data")
    .WithMongoExpress(c => c.WithImageTag("latest").WithLifetime(ContainerLifetime.Persistent))
    .WithLifetime(ContainerLifetime.Persistent);

var reviewDb = mongodb.AddDatabase("reviewdb");

var redis = builder.AddRedis("redis")
    .WithDataVolume("bookstore-redis-data")
    .WithRedisInsight(c => c.WithImageTag("latest").WithLifetime(ContainerLifetime.Persistent))
    .WithLifetime(ContainerLifetime.Persistent);

var elasticsearch = builder.AddElasticsearch("elasticsearch")
    .WithDataVolume("bookstore-elastic-data")
    .WithLifetime(ContainerLifetime.Persistent);

builder.AddProject<Projects.OnlineBookstore_MigrationsService>("migrations-service")
    .WithReference(catalogDb)
    .WaitFor(catalogDb)
    .WithReference(orderDb)
    .WaitFor(orderDb);

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
builder.AddProject<Projects.OnlineBookstore_Web>("web-frontend")
    .WithReference(catalogService)
    .WaitFor(catalogService)
    .WithReference(orderService)
    .WaitFor(orderService)
    .WithReference(reviewService)
    .WaitFor(reviewService)
    .WithExternalHttpEndpoints();

builder.Build().Run();
