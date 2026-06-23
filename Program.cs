using DynamicsGP_EFT_Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => options.ServiceName = "DynamicsGP EFT Worker")
    .UseSystemd()
    .ConfigureServices((context, services) =>
    {
        services
            .AddOptions<EftOptions>()
            .Bind(context.Configuration.GetSection(EftOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<EftFileLogger>();
        services.AddSingleton<EftFileProcessor>();
        services.AddHostedService<EftWorkerService>();
    })
    .ConfigureLogging((_, logging) =>
    {
        logging.ClearProviders();
        logging.AddConsole();

        if (OperatingSystem.IsWindows())
        {
            logging.AddEventLog(settings =>
            {
                settings.SourceName = "DynamicsGP EFT Worker";
                settings.LogName    = "Application";
            });
        }
    })
    .Build();

await host.RunAsync();
