using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Wiltoga.Meppy
{
    internal class Program
    {
#pragma warning disable CS8618
        public static CancellationTokenSource CancellationTokenSource { get; set; }
#pragma warning restore CS8618

        [STAThread]
        private static void Main(string[] args)
        {
            var mutex = new Mutex(true, "Wiltoga.Meppy", out var createdMutex);
            if (!createdMutex)
                return;
            CancellationTokenSource = new CancellationTokenSource();

            using IHost host = Host.CreateDefaultBuilder(args)
                .UseWindowsService(options =>
                {
                    options.ServiceName = "Meppy";
                })
                .ConfigureServices(services =>
                {
                    LoggerProviderOptions.RegisterProviderOptions<
                        EventLogSettings, EventLogLoggerProvider>(services);

                    services.AddHostedService<MeppyBackgroundService>();
                })
                .ConfigureLogging((context, logging) =>
                {
                    logging.AddConfiguration(
                        context.Configuration.GetSection("Logging"));
                })
                .Build();

            var hostTask = host.RunAsync(CancellationTokenSource.Token);

            Application.Run();

            mutex.Dispose();
        }
    }
}