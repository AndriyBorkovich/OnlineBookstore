var builder = DistributedApplication.CreateBuilder(args);


builder.AddProject<Projects.OnlineBookstore_Web>("webfrontend")
    .WithExternalHttpEndpoints();

builder.Build().Run();
