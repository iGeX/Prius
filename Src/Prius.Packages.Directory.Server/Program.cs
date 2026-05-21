using Prius.PackagesRegistry;
using Prius.Engine;
using Prius.Engine.Abstractions;
using DirectoryPackageRepository = Prius.Packages.Directory.Server.DirectoryPackageRepository;

var builder = WebApplication.CreateBuilder(args);

var storagePath = builder.Configuration["StoragePath"] ?? "packages";
if (!Directory.Exists(storagePath)) 
    Directory.CreateDirectory(storagePath);

builder.Services.AddSingleton<IPackageRepository>(new DirectoryPackageRepository(storagePath, new BinaryManager()));
builder.Services.AddPackagesRegistry();

var app = builder.Build();

app
    .UseForwardedHeaders()
    .UseHttpsRedirection();
    
app.UsePackagesRegistry();

app.Run();
