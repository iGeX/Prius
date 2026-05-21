namespace Prius.Engine.Abstractions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public interface IPriusModule
{
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
    
    ValueTask Activate(IServiceProvider serviceProvider, IConfiguration configuration);
    
    ValueTask Stasis();
}
