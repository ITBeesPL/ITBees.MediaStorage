using ITBees.MediaStorage.Interfaces;
using ITBees.MediaStorage.Services;
using ITBees.Models.Media;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ITBees.MediaStorage.Setup;

public class MediaStorageSetup
{
    public static void Register<TContext, TIdentityUser>(IServiceCollection services,
        IConfigurationRoot configurationRoot) where TContext : DbContext
        where TIdentityUser : IdentityUser<Guid>
    {
        services.AddScoped<IMediaService, MediaService>();
    }
}

public class DbModelBuilder
{
    public static void Register(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MediaFile>().HasKey(x => x.Guid);
    }
}