using Prius.Core.Packages;
using Prius.Core.Packages.Registry;

var builder = WebApplication.CreateBuilder(args);

var storagePath = builder.Configuration["StoragePath"] ?? "packages";
if (!Directory.Exists(storagePath)) 
    Directory.CreateDirectory(storagePath);

builder.Services.AddSingleton<IPackageRepository>(new DirectoryPackageRepository(storagePath));
builder.Services.AddPackagesRegistry();

var app = builder.Build();

app
    .UseForwardedHeaders()
    .UseHttpsRedirection();
    
app.UsePackagesRegistry();

app.Run();
