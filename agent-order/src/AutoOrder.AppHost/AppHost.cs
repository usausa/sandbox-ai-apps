var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.AutoOrder_Web>("web");

builder.Build().Run();
